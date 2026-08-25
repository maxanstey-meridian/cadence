namespace Cadence;

public sealed record AskPlannerRequest(
    string CurrentSlice,
    string Question,
    string ProposedApproach,
    IReadOnlyList<string> Evidence
);

public enum OutcomeStatus
{
    NotStarted,
    InProgress,
    Blocked,
    Complete,
}

public sealed record OutcomeProgress(
    string OutcomeId,
    OutcomeStatus Status,
    string Evidence,
    string? NextAction
);

public sealed record UpdateOutcomesRequest(IReadOnlyList<OutcomeProgress> Updates);

public sealed record ObligationClaim(string Id, string Evidence);

public sealed record SubmitReportRequest(
    string Summary,
    string CommitMessage,
    IReadOnlyList<ObligationClaim> ObligationClaims,
    string RegressionTestEvidence
);

public sealed record WriteCheckpointRequest(
    string Summary,
    IReadOnlyList<string> Uncertainties,
    string NextAction
);

public sealed record ResetContextRequest(
    string Summary,
    IReadOnlyList<string> Uncertainties,
    string NextAction,
    string Reason
);
