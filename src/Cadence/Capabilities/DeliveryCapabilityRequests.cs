namespace Cadence;

public sealed record AskPlannerRequest(
    string CurrentSlice,
    string Question,
    string ProposedApproach,
    IReadOnlyList<string> Evidence
);

public sealed record SubmitReportRequest(string Summary, string CommitMessage);

public sealed record WriteCheckpointRequest(
    string Summary,
    IReadOnlyList<string> Uncertainties,
    string NextAction
);
