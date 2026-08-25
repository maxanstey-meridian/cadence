namespace Cadence;

public sealed record ReviewDecision(
    ReviewDecisionValue Decision,
    string Summary,
    IReadOnlyList<ReviewAssessment> Assessments,
    IReadOnlyList<ReviewFinding> Findings,
    string? HumanQuestion = null,
    HumanDecisionDomain? HumanDecisionDomain = null
);

public sealed record ReviewAssessment(string Id, bool Satisfied, string Evidence);

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
