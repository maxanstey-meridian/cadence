namespace Cadence;

public sealed record PlannerDecision(
    PlannerDecisionValue Decision,
    string Rationale,
    IReadOnlyList<string> Constraints,
    IReadOnlyList<string> EvidenceUsed,
    string SafeNextAction,
    string? CorrectedApproach = null,
    string? HumanQuestion = null,
    HumanDecisionDomain? HumanDecisionDomain = null
);

public enum PlannerDecisionValue
{
    Proceed,
    ProceedWithConstraints,
    ReviseApproach,
    Reorient,
    NeedsHuman,
    Stop,
}
