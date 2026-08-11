namespace Cadence;

public enum CadenceLedgerRole
{
    Executor,
    Planner,
    Reviewer,
}

public sealed record CadenceLedgerContext(
    OutcomeProgressDocument? Outcomes,
    SubmitReportRequest? Report,
    ProgressCheckpointRecord? LatestCheckpoint,
    IReadOnlyList<string> ActivePlannerConstraints,
    IReadOnlyList<PlannerDecision> PlannerDecisions,
    IReadOnlyList<ReviewDecision> Reviews,
    IReadOnlyList<VerificationResult> VerificationResults,
    IReadOnlyList<HumanAnswerRecord> HumanAnswers
);
