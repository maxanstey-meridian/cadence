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
    AgentCapability<CadenceState> askPlanner,
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
            ExecutorAgent.Create(agents, askPlanner, submitReport, writeCheckpoint),
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
