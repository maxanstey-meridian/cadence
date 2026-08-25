using System.Text.Json.Serialization;

namespace Cadence;

public sealed record CadenceState(
    Packet Packet,
    string PinnedBaseSha,
    string WorkspacePath,
    bool MutationAuthorized,
    PlannerDecision? PlannerDecision,
    IReadOnlyList<PlannerConstraint> PlannerConstraints,
    WriteCheckpointRequest? LatestCheckpoint,
    string? CandidateSha,
    int VerificationIndex,
    IReadOnlyList<VerificationResult> VerificationResults,
    ExecutorTransition? ExecutorTransition,
    ReviewDecision? ReviewerDecision,
    string? AcceptedCandidateSha,
    int ReviewAttempt,
    int MaximumReviewAttempts,
    int PlannerFailureCount,
    PlannerHumanAnswer? PlannerHumanAnswer,
    ReviewerHumanAnswer? ReviewerHumanAnswer,
    IReadOnlyList<ReviewFinding> ActiveReviewFindings
)
{
    public bool ResumeRequested { get; init; }
    public DateTimeOffset LastContinuityAt { get; init; }
    public IReadOnlyList<OutcomeProgress> OutcomeProgress { get; init; } = [];
    public bool ReviewRepairRequired { get; init; }
    public string? OperatorInstruction { get; init; }
    public bool OperatorInstructionPending { get; init; }

    public bool HasCompleteSuccessfulVerification =>
        VerificationIndex == Packet.Verification.Count
        && VerificationResults.Count == Packet.Verification.Count
        && VerificationResults.All(result => result.ExitCode == 0 && !result.TimedOut);

    public static CadenceState Create(
        Packet packet,
        string pinnedBaseSha,
        string workspacePath,
        int maximumReviewAttempts = 3,
        TimeProvider? timeProvider = null
    )
    {
        if (maximumReviewAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumReviewAttempts),
                "The maximum review-attempt count must be positive."
            );
        }
        var created = new CadenceState(
            packet,
            pinnedBaseSha,
            workspacePath,
            false,
            null,
            [],
            null,
            null,
            0,
            [],
            null,
            null,
            null,
            0,
            maximumReviewAttempts,
            0,
            null,
            null,
            []
        );
        return created with
        {
            LastContinuityAt = (timeProvider ?? TimeProvider.System).GetUtcNow(),
            OutcomeProgress = CreateInitialOutcomeProgress(packet),
        };
    }

    public static IReadOnlyList<OutcomeProgress> CreateInitialOutcomeProgress(Packet packet) =>
        packet
            .Outcomes.Select(o => new OutcomeProgress(
                o.Id,
                OutcomeStatus.NotStarted,
                "",
                "Produce the complete candidate state required by this outcome."
            ))
            .ToArray();

    public CadenceState RecordPlannerDecision(PlannerDecision decision) =>
        this with
        {
            PlannerDecision = decision,
            PlannerConstraints =
                decision.Decision == PlannerDecisionValue.Proceed
                    ? decision.Constraints
                    : PlannerConstraints,
            MutationAuthorized = decision.Decision == PlannerDecisionValue.Proceed,
            PlannerFailureCount = 0,
            PlannerHumanAnswer = null,
            OperatorInstructionPending = false,
        };

    public CadenceState RecordReviewDecision(ReviewDecision decision)
    {
        var repairRequired = decision.Decision == ReviewDecisionValue.RequestChanges;
        return this with
        {
            ReviewerDecision = decision,
            ReviewAttempt = ReviewAttempt + (repairRequired ? 1 : 0),
            ReviewerHumanAnswer = null,
            ReviewRepairRequired = repairRequired,
            ActiveReviewFindings = decision.Decision switch
            {
                ReviewDecisionValue.Accept => [],
                ReviewDecisionValue.RequestChanges => decision.Findings.ToArray(),
                _ => ActiveReviewFindings,
            },
        };
    }

    public CadenceState RecordPlannerRequest(AskPlannerRequest request) =>
        this with
        {
            MutationAuthorized = false,
            PlannerFailureCount = 0,
            ExecutorTransition = new ExecutorTransition.PlannerRequested(request),
        };

    public CadenceState RecordPlannerFailure() =>
        this with
        {
            PlannerFailureCount = PlannerFailureCount + 1,
            MutationAuthorized = false,
        };

    public CadenceState RecordOutcomeUpdates(UpdateOutcomesRequest request)
    {
        var updates = request.Updates.ToDictionary(x => x.OutcomeId, StringComparer.Ordinal);
        var progress = OutcomeProgress
            .Select(x => updates.TryGetValue(x.OutcomeId, out var value) ? value : x)
            .ToArray();
        var changed = !progress.SequenceEqual(OutcomeProgress);
        return this with
        {
            OutcomeProgress = progress,
            ExecutorTransition = new ExecutorTransition.OutcomeProgressUpdated(request),
            ReviewRepairRequired = changed ? false : ReviewRepairRequired,
            CandidateSha = changed ? null : CandidateSha,
            VerificationIndex = changed ? 0 : VerificationIndex,
            VerificationResults = changed ? [] : VerificationResults,
            ReviewerDecision = changed ? null : ReviewerDecision,
            AcceptedCandidateSha = changed ? null : AcceptedCandidateSha,
        };
    }

    public CadenceState RecordContextReset(
        ResetContextRequest request,
        DateTimeOffset acceptedAt
    ) =>
        this with
        {
            LatestCheckpoint = new(request.Summary, request.Uncertainties, request.NextAction),
            LastContinuityAt = acceptedAt,
            MutationAuthorized = false,
            ExecutorTransition = new ExecutorTransition.ContextResetRequested(request),
        };

    public CadenceState RecordImplementationReport(SubmitReportRequest request) =>
        this with
        {
            CandidateSha = null,
            VerificationIndex = 0,
            VerificationResults = [],
            ReviewerDecision = null,
            AcceptedCandidateSha = null,
            ExecutorTransition = new ExecutorTransition.ReportSubmitted(request),
        };

    public CadenceState RecordCheckpoint(
        WriteCheckpointRequest request,
        DateTimeOffset acceptedAt
    ) =>
        this with
        {
            LatestCheckpoint = request,
            LastContinuityAt = acceptedAt,
            MutationAuthorized = false,
            ExecutorTransition = new ExecutorTransition.CheckpointWritten(request),
        };

    public CadenceState CloseMutationAuthority() => this with { MutationAuthorized = false };
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ExecutorTransition.PlannerRequested), "planner-requested")]
[JsonDerivedType(typeof(ExecutorTransition.ReportSubmitted), "report-submitted")]
[JsonDerivedType(typeof(ExecutorTransition.CheckpointWritten), "checkpoint-written")]
[JsonDerivedType(typeof(ExecutorTransition.CandidateUnchanged), "candidate-unchanged")]
[JsonDerivedType(typeof(ExecutorTransition.ContextResetRequested), "context-reset-requested")]
[JsonDerivedType(typeof(ExecutorTransition.OutcomeProgressUpdated), "outcome-progress-updated")]
public abstract record ExecutorTransition
{
    public sealed record PlannerRequested(AskPlannerRequest Request) : ExecutorTransition;

    public sealed record ReportSubmitted(SubmitReportRequest Report) : ExecutorTransition;

    public sealed record CheckpointWritten(WriteCheckpointRequest Checkpoint) : ExecutorTransition;

    public sealed record ContextResetRequested(ResetContextRequest Request) : ExecutorTransition;

    public sealed record OutcomeProgressUpdated(UpdateOutcomesRequest Request) : ExecutorTransition;

    public sealed record CandidateUnchanged(string Explanation) : ExecutorTransition;
}
