using Cadence.Git;
using Microsoft.Extensions.AI;
using Tandem.Advanced;

namespace Cadence;

public sealed class CadenceParticipantsFactory(
    Func<string, IChatClient> chatClients,
    Func<string, CadenceAgentProfile> profileResolver,
    ICadenceRecordSink records,
    ReviewerDoctrine reviewerDoctrine,
    IReadOnlyList<AgentSkill> skills,
    WorkspacePreparation workspacePreparation,
    GitProcess git,
    DirtyWorkCheckpointPolicy dirtyWorkCheckpoint,
    AgentCapability<CadenceState> askPlanner,
    AgentCapability<CadenceState> updateOutcomes,
    AgentCapability<CadenceState> submitReport,
    AgentCapability<CadenceState> writeCheckpoint
)
{
    public CadenceParticipants Create()
    {
        var executorWorkspace = AgentWorkspace<CadenceState>.Define(
            state => state.WorkspacePath,
            state =>
                state.MutationAuthorized
                    ?
                    [
                        .. state.Packet.Verification.Select(
                            (command, index) =>
                                AgentCommand.Define(
                                    $"run_verification_{index + 1}",
                                    $"Run verification command {index + 1}: {command}",
                                    command
                                )
                        ),
                        .. state.Packet.Commands.Select(
                            (command, index) =>
                                AgentCommand.Define(
                                    $"run_command_{index + 1}",
                                    $"Run repository command {index + 1}: {command}",
                                    command
                                )
                        ),
                    ]
                    : []
        );
        var reviewerWorkspace = AgentWorkspace<CadenceState>.Define(
            state => state.WorkspacePath,
            state =>
                state
                    .Packet.Verification.Select(
                        (command, index) =>
                            AgentCommand.Define(
                                $"run_verification_{index + 1}",
                                $"Run verification command {index + 1}: {command}",
                                command
                            )
                    )
                    .ToArray()
        );
        var agents = new CadenceAgentFactory(
            chatClients,
            profileResolver,
            records,
            executorWorkspace,
            reviewerWorkspace,
            skills
        );
        return new CadenceParticipants(
            new PrepareWorkspaceStage(workspacePreparation, records),
            ExecutorAgent.Create(
                agents,
                askPlanner,
                updateOutcomes,
                submitReport,
                writeCheckpoint,
                dirtyWorkCheckpoint
            ),
            PlannerAgent.Create(agents),
            new PlannerFailureStage(records).Definition,
            new CaptureCandidateStage(git),
            new VerificationStage(new VerificationOperation(git, records)),
            ReviewerAgent.Create(agents, reviewerDoctrine),
            new AcceptCandidateStage(records, reviewerDoctrine, git),
            PipelineNodes.Complete(new RunReady()),
            PipelineNodes.Failed(new RunFailed()),
            PipelineNodes.Failed(new PlannerUnavailable()),
            PipelineNodes.WaitFor<CadenceState, PlannerHumanQuestion, PlannerHumanAnswer>(
                "PlannerHumanInput",
                HumanInteraction.BuildPlannerQuestion,
                HumanInteraction.ApplyPlannerAnswer
            ),
            PipelineNodes.WaitFor<CadenceState, ReviewerHumanRequest, ReviewerHumanAnswer>(
                "ReviewerHumanInput",
                HumanInteraction.BuildReviewerQuestion,
                HumanInteraction.ApplyReviewerAnswer
            )
        );
    }
}
