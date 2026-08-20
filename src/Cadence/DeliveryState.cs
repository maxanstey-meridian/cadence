using System.Text.Json.Serialization;

namespace Cadence;

public sealed record CadenceState(
    Packet Packet,
    string PinnedBaseSha,
    string WorkspacePath,
    bool MutationAuthorized,
    PlannerDecision? PlannerDecision,
    IReadOnlyList<string> PlannerConstraints,
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

    public bool HasCompleteSuccessfulVerification =>
        VerificationIndex == Packet.Verification.Count
        && VerificationResults.Count == Packet.Verification.Count
        && VerificationResults.All(result => result.ExitCode == 0 && !result.TimedOut);

    public IReadOnlyList<string> Constraints =>
        Packet.Constraints.Concat(PlannerConstraints).Distinct(StringComparer.Ordinal).ToArray();

    public static CadenceState Create(
        Packet packet,
        string pinnedBaseSha,
        string workspacePath,
        int maximumReviewAttempts = 3
    )
    {
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
    }

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
        };

    public CadenceState RecordReviewDecision(ReviewDecision decision)
    {
        var repairRequired = decision.Decision == ReviewDecisionValue.RequestChanges;
        return this with
        {
            ReviewerDecision = decision,
            ReviewAttempt = ReviewAttempt + (repairRequired ? 1 : 0),
            ReviewerHumanAnswer = null,
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

    public CadenceState RecordCheckpoint(WriteCheckpointRequest request) =>
        this with
        {
            LatestCheckpoint = request,
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
public abstract record ExecutorTransition
{
    public sealed record PlannerRequested(AskPlannerRequest Request) : ExecutorTransition;

    public sealed record ReportSubmitted(SubmitReportRequest Report) : ExecutorTransition;

    public sealed record CheckpointWritten(WriteCheckpointRequest Checkpoint) : ExecutorTransition;

    public sealed record CandidateUnchanged(string Explanation) : ExecutorTransition;
}
