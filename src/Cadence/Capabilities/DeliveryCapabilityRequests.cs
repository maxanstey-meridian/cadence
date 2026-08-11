namespace Cadence;

public sealed record AskPlannerRequest(
    PlannerQuestionType QuestionType,
    string CurrentSlice,
    string Question,
    string ProposedApproach,
    IReadOnlyList<string> Evidence,
    FailedPlannerInstructionContext? FailedInstruction = null
);

public enum PlannerQuestionType
{
    ArchitectureOrEngineeringDirection,
    RepositoryProcedure,
    ImplementationSurfaceReview,
    VerificationStrategy,
    DiffOrObligationClosureAudit,
    HandoffInterpretation,
    StopConditionReview,
    FailedInstruction,
    SessionReliability,
}

public sealed record FailedPlannerInstructionContext(
    string PriorInstruction,
    string AttemptedChange,
    string FailingCommand,
    string RelevantOutput,
    string Contradiction,
    string RevisedUnderstanding,
    string ProposedNextApproach
);

public sealed record SubmitReportRequest(
    string Summary,
    IReadOnlyList<ConstraintClaim> AddressedConstraints,
    RegressionTestClaim RegressionTests
);

public sealed record RegressionTestClaim(
    RegressionTestDisposition Disposition,
    IReadOnlyList<string> Evidence
);

public enum RegressionTestDisposition
{
    Added,
    ExistingCoverage,
    NotApplicable,
}

public sealed record OutcomeLedgerEntry(
    string OutcomeId,
    string Description,
    OutcomeStatus Status,
    IReadOnlyList<string> Evidence,
    string ImplementationState,
    string? NextAction
);

public enum OutcomeStatus
{
    NotStarted,
    InProgress,
    Blocked,
    Complete,
}

public sealed record UpdateOutcomesRequest(IReadOnlyList<OutcomeUpdate> Updates);

public sealed record OutcomeUpdate(
    string OutcomeId,
    OutcomeStatus Status,
    IReadOnlyList<string> Evidence,
    string ImplementationState,
    string? NextAction
);

public sealed record ConstraintClaim(string Constraint, string Evidence);

public sealed record WriteCheckpointRequest(
    string Summary,
    IReadOnlyList<string> Uncertainties,
    string NextAction
);
