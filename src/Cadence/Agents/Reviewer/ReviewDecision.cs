namespace Cadence;

public sealed record ReviewDecision(
    ReviewDecisionValue Decision,
    string Summary,
    IReadOnlyList<ReviewFinding> Findings,
    string? HumanQuestion = null,
    HumanDecisionDomain? HumanDecisionDomain = null
);

public enum ReviewDecisionValue
{
    Accept,
    RequestChanges,
    NeedsHuman,
}

public sealed record ReviewFinding(
    ReviewFindingSeverity Severity,
    string Description,
    string Location
);

public enum ReviewFindingSeverity
{
    Critical,
    High,
    Medium,
    Low,
}
