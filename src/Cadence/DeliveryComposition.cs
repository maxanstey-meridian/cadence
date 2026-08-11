namespace Cadence;

public sealed class CadenceComposition
{
    private readonly CadenceParticipants _cadence;

    public CadenceComposition(CadenceParticipantsFactory participantsFactory)
    {
        _cadence = participantsFactory.Create();
    }

    public PipelineInteraction<
        CadenceState,
        PlannerHumanQuestion,
        PlannerHumanAnswer
    > PlannerHumanInput => _cadence.PlannerHumanInput;

    public PipelineInteraction<
        CadenceState,
        ReviewerHumanRequest,
        ReviewerHumanAnswer
    > ReviewerHumanInput => _cadence.ReviewerHumanInput;

    public Pipeline<CadenceState> Build()
    {
        var cadence = _cadence;
        return Pipeline
            .Start(
                at: cadence.PrepareWorkspace,
                name: "cadence",
                description: "The Executor implements with Planner guidance and Reviewer approval."
            )
            .Persist()
            .Route(
                on: cadence.PrepareWorkspace.Success,
                to: cadence.Executor,
                label: "workspace prepared"
            )
            .Route(
                on: cadence.PrepareWorkspace.Failed,
                to: cadence.FailRun,
                label: "workspace failed"
            )
            .Route(
                on: cadence.Executor.Success,
                when: state => state.ExecutorTransition is ExecutorTransition.PlannerRequested,
                to: cadence.Planner,
                label: "planner requested"
            )
            .Route(
                on: cadence.Executor.Success,
                when: state => state.ExecutorTransition is ExecutorTransition.OutcomeLedgerUpdated,
                to: cadence.Executor,
                label: "outcome ledger updated"
            )
            .Route(
                on: cadence.Executor.Success,
                when: state => state.ExecutorTransition is ExecutorTransition.ReportSubmitted,
                to: cadence.CaptureCandidate,
                label: "report submitted"
            )
            .Route(
                on: cadence.Executor.Success,
                when: state => state.ExecutorTransition is ExecutorTransition.CheckpointWritten,
                to: cadence.Executor,
                label: "checkpoint written"
            )
            .Route(on: cadence.Executor.Failed, to: cadence.FailRun, label: "agent failed")
            .Route(
                on: cadence.Planner.Success,
                when: IsPlannerProceed,
                to: cadence.Executor,
                label: "proceed / proceed with constraints"
            )
            .Route(
                on: cadence.Planner.Success,
                when: IsPlannerRevision,
                to: cadence.Executor,
                label: "revise approach"
            )
            .Route(
                on: cadence.Planner.Success,
                when: IsPlannerNeedsHuman,
                to: cadence.PlannerHumanInput,
                label: "needs human"
            )
            .Route(
                on: cadence.Planner.Success,
                when: IsPlannerStop,
                to: cadence.FailRun,
                label: "stop"
            )
            .Route(on: cadence.Planner.Failed, to: cadence.PlannerFailure, label: "planner failed")
            .Route(
                from: cadence.PlannerFailure,
                when: state => state.PlannerFailureCount == 1,
                to: cadence.Planner,
                label: "retry planner once"
            )
            .Route(
                from: cadence.PlannerFailure,
                when: state => state.PlannerFailureCount >= 2,
                to: cadence.PlannerUnavailable,
                label: "planner unavailable"
            )
            .Route(
                on: cadence.CaptureCandidate.Success,
                to: cadence.Verification,
                label: "candidate captured"
            )
            .Route(
                on: cadence.CaptureCandidate.Failed,
                to: cadence.FailRun,
                label: "capture failed"
            )
            .Route(
                on: cadence.Verification.Success,
                when: LatestCommandPassedAndCommandsRemain,
                to: cadence.Verification,
                label: "commands remain"
            )
            .Route(
                on: cadence.Verification.Success,
                when: LatestCommandPassedAndAllComplete,
                to: cadence.Reviewer,
                label: "verification complete"
            )
            .Route(
                on: cadence.Verification.Success,
                when: LatestCommandFailed,
                to: cadence.Executor,
                label: "command failed"
            )
            .Route(
                on: cadence.Verification.Failed,
                to: cadence.FailRun,
                label: "verification failed"
            )
            .Route(
                on: cadence.Reviewer.Success,
                when: IsReviewAccepted,
                to: cadence.AcceptCandidate,
                label: "accepted"
            )
            .Route(
                on: cadence.AcceptCandidate.Success,
                to: cadence.CompleteRun,
                label: "candidate accepted for publication"
            )
            .Route(
                on: cadence.AcceptCandidate.Failed,
                to: cadence.FailRun,
                label: "candidate acceptance failed"
            )
            .Route(
                on: cadence.Reviewer.Success,
                when: IsReviewChangesRequested,
                to: cadence.Executor,
                label: "changes requested"
            )
            .Route(
                on: cadence.Reviewer.Success,
                when: IsReviewNeedsHuman,
                to: cadence.ReviewerHumanInput,
                label: "needs human"
            )
            .Route(on: cadence.Reviewer.Failed, to: cadence.FailRun, label: "agent failed")
            .Route(cadence.PlannerHumanInput, cadence.Planner, "answer for planner")
            .Route(
                when: IsReviewerHumanDecision,
                from: cadence.ReviewerHumanInput,
                to: cadence.Reviewer,
                label: "human decision for reviewer"
            )
            .Route(
                when: ShouldContinueRepairs,
                from: cadence.ReviewerHumanInput,
                to: cadence.Executor,
                label: "repair budget renewed"
            )
            .Route(
                when: ShouldStopAfterReviewCap,
                from: cadence.ReviewerHumanInput,
                to: cadence.FailRun,
                label: "human stopped repairs"
            )
            .Build(cadence.CompleteRun, cadence.FailRun);
    }

    private static bool LatestCommandPassed(CadenceState state) =>
        state.VerificationResults.LastOrDefault()?.ExitCode == 0;

    private static bool LatestCommandFailed(CadenceState state) =>
        state.VerificationResults.LastOrDefault()?.ExitCode is not (null or 0);

    private static bool LatestCommandPassedAndCommandsRemain(CadenceState state) =>
        LatestCommandPassed(state) && state.VerificationIndex < state.Packet.Verification.Count;

    private static bool LatestCommandPassedAndAllComplete(CadenceState state) =>
        LatestCommandPassed(state) && state.VerificationIndex >= state.Packet.Verification.Count;

    private static bool IsPlannerProceed(CadenceState state) =>
        state.PlannerDecision?.Decision
            is PlannerDecisionValue.Proceed
                or PlannerDecisionValue.ProceedWithConstraints;

    private static bool IsPlannerNeedsHuman(CadenceState state) =>
        state.PlannerDecision?.Decision == PlannerDecisionValue.NeedsHuman;

    private static bool IsPlannerRevision(CadenceState state) =>
        state.PlannerDecision?.Decision
            is PlannerDecisionValue.ReviseApproach
                or PlannerDecisionValue.Reorient;

    private static bool IsPlannerStop(CadenceState state) =>
        state.PlannerDecision?.Decision == PlannerDecisionValue.Stop;

    private static bool IsReviewAccepted(CadenceState state) =>
        state.ReviewerDecision?.Decision == ReviewDecisionValue.Accept;

    private static bool IsReviewChangesRequested(CadenceState state) =>
        state.ReviewerDecision?.Decision == ReviewDecisionValue.RequestChanges
        && state.ReviewAttempt < state.MaximumReviewAttempts;

    private static bool IsReviewNeedsHuman(CadenceState state) =>
        state.ReviewerDecision?.Decision == ReviewDecisionValue.NeedsHuman
        || state.ReviewerDecision?.Decision == ReviewDecisionValue.RequestChanges
            && state.ReviewAttempt >= state.MaximumReviewAttempts;

    private static bool IsReviewerHumanDecision(CadenceState state) =>
        state.ReviewerHumanResolution == ReviewerHumanResolution.HumanDecision;

    private static bool ShouldContinueRepairs(CadenceState state) =>
        state.ReviewerHumanResolution == ReviewerHumanResolution.ContinueRepairs;

    private static bool ShouldStopAfterReviewCap(CadenceState state) =>
        state.ReviewerHumanResolution == ReviewerHumanResolution.Stop;
}
