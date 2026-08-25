namespace Cadence;

public sealed record PlannerConstraint(string Id, string Requirement);

public sealed record PlannerDecision(
    PlannerDecisionValue Decision,
    string Rationale,
    IReadOnlyList<PlannerConstraint> Constraints,
    IReadOnlyList<string> EvidenceUsed,
    string SafeNextAction,
    string? CorrectedApproach = null,
    string? HumanQuestion = null,
    HumanDecisionDomain? HumanDecisionDomain = null
);

public enum PlannerDecisionValue
{
    Proceed,
    ReviseApproach,
    NeedsHuman,
    Stop,
}
