using System.Text.Json.Serialization;

namespace Cadence;

public sealed record CadenceState(
    Packet Packet,
    string PinnedBaseSha,
    string WorkspacePath,
    int ApproachRevision,
    int? ApprovedApproachRevision,
    PlannerDecision? PlannerDecision,
    IReadOnlyList<string> PlannerConstraints,
    IReadOnlyList<OutcomeLedgerEntry> OutcomeLedger,
    WriteCheckpointRequest? LatestCheckpoint,
    DateTimeOffset LastContinuityAt,
    string? CandidateSha,
    int VerificationIndex,
    IReadOnlyList<VerificationResult> VerificationResults,
    string? VerifiedCandidateSha,
    ExecutorTransition? ExecutorTransition,
    ReviewDecision? ReviewerDecision,
    string? ReviewerCandidateSha,
    int ReviewAttempt,
    int MaximumReviewAttempts,
    int PlannerFailureCount,
    PlannerHumanAnswer? PlannerHumanAnswer,
    ReviewerHumanAnswer? ReviewerHumanAnswer,
    ReviewerHumanResolution? ReviewerHumanResolution,
    bool ReviewRepairRequired
)
{
    public bool MutationAuthorized =>
        ApprovedApproachRevision is { } approved && approved == ApproachRevision;

    public IReadOnlyList<string> Constraints =>
        Packet.Constraints.Concat(PlannerConstraints).Distinct(StringComparer.Ordinal).ToArray();

    public static CadenceState Create(
        Packet packet,
        string pinnedBaseSha,
        string workspacePath,
        TimeProvider? timeProvider = null,
        int maximumReviewAttempts = 3
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packet.Title);
        if (packet.Outcomes.Count == 0)
        {
            throw new ArgumentException(
                "A packet must declare at least one outcome.",
                nameof(packet)
            );
        }
        if (
            packet.Outcomes.Any(outcome =>
                string.IsNullOrWhiteSpace(outcome.Id)
                || string.IsNullOrWhiteSpace(outcome.Description)
            )
        )
        {
            throw new ArgumentException(
                "Packet outcome IDs and descriptions must not be blank.",
                nameof(packet)
            );
        }
        if (
            packet
                .Outcomes.GroupBy(outcome => outcome.Id, StringComparer.Ordinal)
                .Any(group => group.Count() > 1)
        )
        {
            throw new ArgumentException("Packet outcome IDs must be unique.", nameof(packet));
        }
        if (packet.Verification.Count == 0)
        {
            throw new ArgumentException(
                "A packet must declare at least one verification command.",
                nameof(packet)
            );
        }
        if (packet.Verification.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException(
                "Packet verification commands must not be blank.",
                nameof(packet)
            );
        }
        if (maximumReviewAttempts <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumReviewAttempts),
                "The maximum review-attempt count must be positive."
            );
        }

        return new(
            packet,
            pinnedBaseSha,
            workspacePath,
            ApproachRevision: 0,
            ApprovedApproachRevision: null,
            PlannerDecision: null,
            PlannerConstraints: [],
            OutcomeLedger: packet
                .Outcomes.Select(outcome => new OutcomeLedgerEntry(
                    outcome.Id,
                    outcome.Description,
                    OutcomeStatus.NotStarted,
                    [],
                    "No implementation has been recorded.",
                    "Inspect the repository and determine the smallest implementation step."
                ))
                .ToArray(),
            LatestCheckpoint: null,
            LastContinuityAt: (timeProvider ?? TimeProvider.System).GetUtcNow(),
            CandidateSha: null,
            VerificationIndex: 0,
            VerificationResults: [],
            VerifiedCandidateSha: null,
            ExecutorTransition: null,
            ReviewerDecision: null,
            ReviewerCandidateSha: null,
            ReviewAttempt: 0,
            MaximumReviewAttempts: maximumReviewAttempts,
            PlannerFailureCount: 0,
            PlannerHumanAnswer: null,
            ReviewerHumanAnswer: null,
            ReviewerHumanResolution: null,
            ReviewRepairRequired: false
        );
    }

    public CadenceState RecordPlannerDecision(PlannerDecision decision)
    {
        var authorizesMutation =
            decision.Decision
            is PlannerDecisionValue.Proceed
                or PlannerDecisionValue.ProceedWithConstraints;
        return this with
        {
            PlannerDecision = decision,
            PlannerConstraints = authorizesMutation ? decision.Constraints : PlannerConstraints,
            ApprovedApproachRevision = authorizesMutation ? ApproachRevision : null,
            PlannerFailureCount = 0,
            PlannerHumanAnswer = null,
        };
    }

    public CadenceState RecordReviewDecision(ReviewDecision decision)
    {
        var repairRequired = decision.Decision == ReviewDecisionValue.RequestChanges;
        return this with
        {
            ReviewerDecision = decision,
            ReviewerCandidateSha = CandidateSha,
            ReviewAttempt = ReviewAttempt + (repairRequired ? 1 : 0),
            ReviewerHumanAnswer = null,
            ReviewerHumanResolution = null,
            ReviewRepairRequired = repairRequired,
        };
    }

    public CadenceState RecordPlannerRequest(
        AskPlannerRequest request,
        DateTimeOffset acceptedAt
    ) =>
        this with
        {
            ApproachRevision = ApproachRevision + 1,
            ApprovedApproachRevision = null,
            PlannerFailureCount = 0,
            LastContinuityAt = acceptedAt,
            ExecutorTransition = new ExecutorTransition.PlannerRequested(request),
        };

    public CadenceState RecordPlannerFailure() =>
        this with
        {
            PlannerFailureCount = PlannerFailureCount + 1,
            ApprovedApproachRevision = null,
        };

    public CadenceState RecordOutcomeUpdates(UpdateOutcomesRequest request)
    {
        var updates = request.Updates.ToDictionary(
            update => update.OutcomeId,
            StringComparer.Ordinal
        );
        var updatedLedger = OutcomeLedger
            .Select(entry =>
                updates.TryGetValue(entry.OutcomeId, out var update)
                    ? entry with
                    {
                        Status = update.Status,
                        Evidence = update.Evidence,
                        ImplementationState = update.ImplementationState,
                        NextAction = update.NextAction,
                    }
                    : entry
            )
            .ToArray();
        var materiallyChanged = updatedLedger
            .Where(
                (entry, index) =>
                    entry.Status != OutcomeLedger[index].Status
                    || !entry.Evidence.SequenceEqual(
                        OutcomeLedger[index].Evidence,
                        StringComparer.Ordinal
                    )
                    || !string.Equals(
                        entry.ImplementationState,
                        OutcomeLedger[index].ImplementationState,
                        StringComparison.Ordinal
                    )
                    || !string.Equals(
                        entry.NextAction,
                        OutcomeLedger[index].NextAction,
                        StringComparison.Ordinal
                    )
            )
            .Any();
        if (!materiallyChanged)
        {
            return this with
            {
                ExecutorTransition = new ExecutorTransition.OutcomeLedgerUpdated(request),
            };
        }

        return this with
        {
            OutcomeLedger = updatedLedger,
            CandidateSha = null,
            VerificationIndex = 0,
            VerificationResults = [],
            VerifiedCandidateSha = null,
            ReviewerDecision = null,
            ReviewerCandidateSha = null,
            ExecutorTransition = new ExecutorTransition.OutcomeLedgerUpdated(request),
            ReviewRepairRequired = false,
        };
    }

    public CadenceState RecordImplementationReport(SubmitReportRequest request) =>
        this with
        {
            CandidateSha = null,
            VerificationIndex = 0,
            VerificationResults = [],
            VerifiedCandidateSha = null,
            ReviewerDecision = null,
            ReviewerCandidateSha = null,
            ExecutorTransition = new ExecutorTransition.ReportSubmitted(request),
        };

    public CadenceState RecordCheckpoint(
        WriteCheckpointRequest request,
        DateTimeOffset acceptedAt
    ) =>
        this with
        {
            LatestCheckpoint = request,
            ApprovedApproachRevision =
                request.Uncertainties.Count > 0 ? null : ApprovedApproachRevision,
            LastContinuityAt = acceptedAt,
            ExecutorTransition = new ExecutorTransition.CheckpointWritten(request),
        };
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ExecutorTransition.PlannerRequested), "planner-requested")]
[JsonDerivedType(typeof(ExecutorTransition.ReportSubmitted), "report-submitted")]
[JsonDerivedType(typeof(ExecutorTransition.CheckpointWritten), "checkpoint-written")]
[JsonDerivedType(typeof(ExecutorTransition.OutcomeLedgerUpdated), "outcome-ledger-updated")]
public abstract record ExecutorTransition
{
    public sealed record PlannerRequested(AskPlannerRequest Request) : ExecutorTransition;

    public sealed record ReportSubmitted(SubmitReportRequest Report) : ExecutorTransition;

    public sealed record CheckpointWritten(WriteCheckpointRequest Checkpoint) : ExecutorTransition;

    public sealed record OutcomeLedgerUpdated(UpdateOutcomesRequest Request) : ExecutorTransition;
}
