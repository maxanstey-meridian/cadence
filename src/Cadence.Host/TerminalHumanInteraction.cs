namespace Cadence.Host;

internal sealed class TerminalHumanInteraction(RunRecordStore records)
{
    public async ValueTask<PlannerHumanAnswer> WaitForPlannerAsync(
        PipelineInteractionContext<PlannerHumanQuestion, PlannerHumanAnswer> context,
        CancellationToken cancellationToken
    )
    {
        if (Console.IsInputRedirected)
        {
            throw new InvalidOperationException(
                $"{context.InteractionId} requires human input, but stdin is redirected."
            );
        }

        Console.WriteLine();
        Console.WriteLine($"{context.InteractionId}: {context.Request.Question}");
        Console.WriteLine($"Reason: {context.Request.Reason}");
        Console.WriteLine($"Domain: {context.Request.Domain}");
        Console.Write("> ");
        var text = await Console.In.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("A non-empty human answer is required.");
        }

        var answer = new PlannerHumanAnswer(text.Trim());
        await records.RecordPlannerHumanAnswerAsync(context, answer, cancellationToken);
        return answer;
    }

    public async ValueTask<ReviewerHumanAnswer> WaitForReviewerAsync(
        PipelineInteractionContext<ReviewerHumanRequest, ReviewerHumanAnswer> context,
        CancellationToken cancellationToken
    )
    {
        if (Console.IsInputRedirected)
        {
            throw new InvalidOperationException(
                $"{context.InteractionId} requires human input, but stdin is redirected."
            );
        }

        Console.WriteLine();
        Console.WriteLine($"{context.InteractionId}: {context.Request.Question}");
        Console.WriteLine($"Reason: {context.Request.Reason}");
        if (context.Request is ReviewerHumanRequest.HumanDecision humanDecision)
        {
            Console.WriteLine($"Domain: {humanDecision.Domain}");
        }
        if (context.Request is ReviewerHumanRequest.RepairCap)
        {
            Console.Write("[continue/stop] > ");
        }
        else
        {
            Console.Write("> ");
        }
        var text = await Console.In.ReadLineAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new InvalidOperationException("A non-empty human answer is required.");
        }

        ReviewerHumanAnswer answer = context.Request switch
        {
            ReviewerHumanRequest.HumanDecision => new ReviewerHumanAnswer.HumanDecision(
                text.Trim()
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
            _ => throw new ArgumentOutOfRangeException(nameof(context)),
        };
        await records.RecordReviewerHumanAnswerAsync(context, answer, cancellationToken);
        return answer;
    }
}
