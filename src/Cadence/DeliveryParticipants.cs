namespace Cadence;

public sealed record CadenceParticipants(
    PrepareWorkspaceStage PrepareWorkspace,
    AgentDefinition<CadenceState> Executor,
    AgentDefinition<CadenceState> Planner,
    IGeneratedPipelineStep<CadenceState, GeneratedStepCompletion> PlannerFailure,
    CaptureCandidateStage CaptureCandidate,
    VerificationStage Verification,
    AgentDefinition<CadenceState> Reviewer,
    AcceptCandidateStage AcceptCandidate,
    IPipelineNode<CadenceState> CompleteRun,
    IPipelineNode<CadenceState> FailRun,
    IPipelineNode<CadenceState> PlannerUnavailable,
    PipelineInteraction<CadenceState, PlannerHumanQuestion, PlannerHumanAnswer> PlannerHumanInput,
    PipelineInteraction<CadenceState, ReviewerHumanRequest, ReviewerHumanAnswer> ReviewerHumanInput
);
