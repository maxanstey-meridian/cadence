using Cadence.Git;
using Microsoft.Extensions.AI;
using Tandem.Advanced;

namespace Cadence;

public sealed class CadenceParticipantsFactory(
    Func<string, IChatClient> chatClients,
    Func<string, CadenceAgentProfile> profileResolver,
    ReviewerDoctrine reviewerDoctrine,
    IReadOnlyList<AgentSkill> skills,
    WorkspacePreparation workspacePreparation,
    GitProcess git,
    DirtyWorkCheckpointPolicy dirtyWorkCheckpoint,
    AgentCapability<CadenceState> askPlanner,
    AgentCapability<CadenceState> updateOutcomes,
    AgentCapability<CadenceState> submitReport,
    AgentCapability<CadenceState> writeCheckpoint,
    AgentCapability<CadenceState> resetContext
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
                        .. state.Packet.Verification.Select(command =>
                            AgentCommand.Define(
                                $"run_verification_{command.Label}",
                                $"Run diagnostic verification command {command.Label}: {command.Command}",
                                command.Command
                            )
                        ),
                        .. state.Packet.Commands.Select(command =>
                            AgentCommand.Define(
                                $"run_command_{command.Label}",
                                $"Run repository command {command.Label}: {command.Command}",
                                command.Command
                            )
                        ),
                    ]
                    : []
        );
        var reviewerWorkspace = AgentWorkspace<CadenceState>.Define(
            state => state.WorkspacePath,
            _ => []
        );
        var agents = new CadenceAgentFactory(
            chatClients,
            profileResolver,
            executorWorkspace,
            reviewerWorkspace,
            skills
        );
        return new CadenceParticipants(
            new PrepareWorkspaceStage(workspacePreparation),
            ExecutorAgent.Create(
                agents,
                askPlanner,
                updateOutcomes,
                submitReport,
                writeCheckpoint,
                resetContext,
                dirtyWorkCheckpoint
            ),
            PlannerAgent.Create(agents),
            new PlannerFailureStage().Definition,
            new CaptureCandidateStage(git),
            new VerificationStage(new VerificationOperation(git)),
            ReviewerAgent.Create(agents, reviewerDoctrine),
            new AcceptCandidateStage(git),
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
