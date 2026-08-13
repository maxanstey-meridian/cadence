using Tandem.Terminal;

namespace Cadence.Host;

internal sealed class TerminalHumanInteraction(RunRecordStore records)
{
    private readonly object _gate = new();
    private PendingInteraction? _pending;

    public bool HasPending()
    {
        lock (_gate)
        {
            return _pending is not null;
        }
    }

    public TerminalInteractionPrompt? FormatInteraction(
        PipelineInteractionRequestedObservation observation
    ) =>
        observation switch
        {
            PipelineInteractionRequested<PlannerHumanQuestion> planner => new(
                planner.Request.Question,
                $"{planner.Request.Reason}\nDomain: {planner.Request.Domain}"
            ),
            PipelineInteractionRequested<ReviewerHumanRequest> reviewer => FormatReviewer(
                reviewer.Request
            ),
            _ => null,
        };

    public ValueTask SubmitAsync(string text, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        PendingInteraction pending;
        lock (_gate)
        {
            pending =
                _pending
                ?? throw new InvalidOperationException(
                    "No human interaction is awaiting an answer."
                );
        }
        pending.Submit(text);
        return ValueTask.CompletedTask;
    }

    public async ValueTask<PlannerHumanAnswer> WaitForPlannerAsync(
        PipelineInteractionContext<PlannerHumanQuestion, PlannerHumanAnswer> context,
        CancellationToken cancellationToken
    )
    {
        var pending = SetPending(
            text => new PlannerHumanAnswer(RequireAnswer(text)),
            cancellationToken
        );
        try
        {
            var answer = await pending.Answer.Task.WaitAsync(cancellationToken);
            await records.RecordPlannerHumanAnswerAsync(context, answer, cancellationToken);
            return answer;
        }
        finally
        {
            ClearPending(pending);
        }
    }

    public async ValueTask<ReviewerHumanAnswer> WaitForReviewerAsync(
        PipelineInteractionContext<ReviewerHumanRequest, ReviewerHumanAnswer> context,
        CancellationToken cancellationToken
    )
    {
        var pending = SetPending(
            text => CreateReviewerAnswer(context.Request, text),
            cancellationToken
        );
        try
        {
            var answer = await pending.Answer.Task.WaitAsync(cancellationToken);
            await records.RecordReviewerHumanAnswerAsync(context, answer, cancellationToken);
            return answer;
        }
        finally
        {
            ClearPending(pending);
        }
    }

    private PendingInteraction<TAnswer> SetPending<TAnswer>(
        Func<string, TAnswer> createAnswer,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        var pending = new PendingInteraction<TAnswer>(createAnswer);
        lock (_gate)
        {
            if (_pending is not null)
            {
                throw new InvalidOperationException(
                    "Another human interaction is already pending."
                );
            }
            _pending = pending;
        }
        return pending;
    }

    private void ClearPending(PendingInteraction pending)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_pending, pending))
            {
                _pending = null;
            }
        }
    }

    private static TerminalInteractionPrompt FormatReviewer(ReviewerHumanRequest request) =>
        request switch
        {
            ReviewerHumanRequest.HumanDecision decision => new(
                decision.Question,
                $"{decision.Reason}\nDomain: {decision.Domain}"
            ),
            ReviewerHumanRequest.RepairCap repair => new(
                repair.Question,
                $"{repair.Reason}\nAnswer continue or stop."
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };

    private static ReviewerHumanAnswer CreateReviewerAnswer(
        ReviewerHumanRequest request,
        string text
    ) =>
        request switch
        {
            ReviewerHumanRequest.HumanDecision => new ReviewerHumanAnswer.HumanDecision(
                RequireAnswer(text)
            ),
            ReviewerHumanRequest.RepairCap
                when text.Trim().Equals("continue", StringComparison.OrdinalIgnoreCase) =>
                new ReviewerHumanAnswer.ContinueRepairs(),
            ReviewerHumanRequest.RepairCap
                when text.Trim().Equals("stop", StringComparison.OrdinalIgnoreCase) =>
                new ReviewerHumanAnswer.Stop(),
            ReviewerHumanRequest.RepairCap => throw new InvalidOperationException(
                "The repair-cap answer must be 'continue' or 'stop'."
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(request)),
        };

    private static string RequireAnswer(string text) =>
        !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : throw new InvalidOperationException("A non-empty human answer is required.");

    private abstract class PendingInteraction
    {
        public abstract void Submit(string text);
    }

    private sealed class PendingInteraction<TAnswer>(Func<string, TAnswer> createAnswer)
        : PendingInteraction
    {
        public TaskCompletionSource<TAnswer> Answer { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override void Submit(string text) => Answer.TrySetResult(createAnswer(text));
    }
}
