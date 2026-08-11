namespace Cadence;

public sealed record OutcomeProgress(
    string Id,
    string Description,
    OutcomeStatus Status,
    IReadOnlyList<string> Evidence,
    string ImplementationState,
    string? NextAction
);

public sealed record OutcomeProgressDocument(
    string AcceptedDecisionId,
    IReadOnlyList<OutcomeProgress> Outcomes
);

public sealed record ProgressCheckpointRecord(
    string Summary,
    IReadOnlyList<string> ChangedFiles,
    IReadOnlyList<string> AcceptedConstraints,
    IReadOnlyList<string> Uncertainties,
    string NextAction
);

public sealed record PublicationCandidateDocument(
    string AcceptedCandidateId,
    string Repository,
    string WorkspacePath,
    string PacketTitle,
    string PinnedBaseSha,
    string CandidateSha,
    string ReviewerDoctrineSource,
    string ReviewerDoctrineHash,
    IReadOnlyList<ReviewOutcomeAssessment> OutcomeAssessments,
    IReadOnlyList<VerificationResult> VerificationEvidence,
    ReviewDecision ReviewerDecision
);

public sealed record PublicationResultRecord(
    string Repository,
    string Branch,
    string CandidateSha,
    bool Reconciled
);

public sealed record HumanAnswerRecord(
    string RequestId,
    string InteractionId,
    string Question,
    string Answer
);

public sealed record VerificationResultRecord(string CandidateSha, VerificationResult Result);
