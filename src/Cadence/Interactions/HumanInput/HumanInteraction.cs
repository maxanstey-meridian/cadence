namespace Cadence;

public static class HumanInteraction
{
    public static PlannerHumanQuestion BuildPlannerQuestion(CadenceState state) =>
        state.PlannerDecision is { Decision: PlannerDecisionValue.NeedsHuman } planner
            ? new PlannerHumanQuestion(
                planner.HumanQuestion ?? "No question provided.",
                planner.Rationale,
                planner.HumanDecisionDomain
                    ?? throw new InvalidOperationException(
                        "Planner Human questions require a decision domain."
                    )
            )
            : throw new InvalidOperationException("No pending planner question exists.");

    public static CadenceState ApplyPlannerAnswer(CadenceState state, PlannerHumanAnswer answer) =>
        state with
        {
            PlannerDecision = null,
            PlannerHumanAnswer = answer,
        };

    public static ReviewerHumanRequest BuildReviewerQuestion(CadenceState state)
    {
        var reviewer =
            state.ReviewerDecision
            ?? throw new InvalidOperationException("No pending reviewer question exists.");
        if (reviewer.Decision == ReviewDecisionValue.NeedsHuman)
        {
            return new ReviewerHumanRequest.HumanDecision(
                reviewer.HumanQuestion ?? "No question provided.",
                reviewer.Summary,
                reviewer.HumanDecisionDomain
                    ?? throw new InvalidOperationException(
                        "Reviewer Human questions require a decision domain."
                    )
            );
        }
        if (
            reviewer.Decision == ReviewDecisionValue.RequestChanges
            && state.ReviewAttempt >= state.MaximumReviewAttempts
        )
        {
            return new ReviewerHumanRequest.RepairCap(
                "The in-run repair limit was reached. Continue repairs or stop the run?",
                reviewer.Summary
            );
        }
        throw new InvalidOperationException("No pending reviewer question exists.");
    }

    public static CadenceState ApplyReviewerAnswer(
        CadenceState state,
        ReviewerHumanAnswer answer
    ) =>
        (BuildReviewerQuestion(state), answer) switch
        {
            (ReviewerHumanRequest.HumanDecision, ReviewerHumanAnswer.HumanDecision humanDecision) =>
                state with
                {
                    ReviewerDecision = null,
                    ReviewerHumanAnswer = humanDecision,
                },
            (ReviewerHumanRequest.RepairCap, ReviewerHumanAnswer.ContinueRepairs) => state with
            {
                ReviewerHumanAnswer = answer,
                ReviewAttempt = 0,
            },
            (ReviewerHumanRequest.RepairCap, ReviewerHumanAnswer.Stop) => state with
            {
                ReviewerDecision = null,
                ReviewerHumanAnswer = answer,
            },
            _ => throw new InvalidOperationException(
                "The Human answer does not match the pending Reviewer request."
            ),
        };
}
