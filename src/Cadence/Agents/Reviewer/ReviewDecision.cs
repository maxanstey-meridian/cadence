namespace Cadence;

public sealed record ReviewDecision(
    ReviewDecisionValue Decision,
    string DoctrineHash,
    string Summary,
    IReadOnlyList<ReviewOutcomeAssessment> Outcomes,
    IReadOnlyList<ReviewFinding> Findings,
    IReadOnlyList<ReviewConstraintAssessment> ConstraintAssessments,
    string? HumanQuestion = null,
    HumanDecisionDomain? HumanDecisionDomain = null,
    IReadOnlyList<ReviewAcceptanceAssessment>? AcceptanceAssessments = null
)
{
    public IReadOnlyList<ReviewAcceptanceAssessment> AcceptanceAssessments { get; init; } =
        AcceptanceAssessments ?? [];
}

public sealed record ReviewAcceptanceAssessment(
    string AcceptanceId,
    bool Satisfied,
    IReadOnlyList<ReviewEvidenceReference> Evidence
);

public sealed record ReviewOutcomeAssessment(
    string OutcomeId,
    bool Delivered,
    IReadOnlyList<ReviewEvidenceReference> Evidence
);

public sealed record ReviewConstraintAssessment(
    string Constraint,
    bool Satisfied,
    IReadOnlyList<ReviewEvidenceReference> Evidence
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
    IReadOnlyList<ReviewEvidenceReference> Evidence
);

public sealed record ReviewEvidenceReference(
    ReviewEvidenceKind Kind,
    string? Path = null,
    int? Line = null,
    string? Symbol = null,
    string? Command = null,
    int? ExitCode = null,
    string? Stdout = null,
    string? Stderr = null,
    string? OutcomeId = null,
    string? Constraint = null,
    string? DoctrineClause = null,
    string? AcceptanceId = null
);

public enum ReviewEvidenceKind
{
    FileLine,
    Symbol,
    VerificationCommand,
    PacketOutcome,
    Constraint,
    DoctrineClause,
    AcceptanceCriterion,
}

public enum ReviewFindingSeverity
{
    Critical,
    High,
    Medium,
    Low,
}
