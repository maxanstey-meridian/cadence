using Tandem.Advanced;

namespace Cadence;

public static class ExecutorPolicies
{
    public static AgentConversationDecision RetainUntilAcceptedReport(
        AgentMessageContext<CadenceState> context,
        AgentMessageOutcome _
    ) =>
        context.State.ExecutorTransition
            is ExecutorTransition.ReportSubmitted
                or ExecutorTransition.PlannerRequested
                {
                    Request.QuestionType: PlannerQuestionType.SessionReliability,
                }
            ? new(AgentConversationRetention.Discard)
            : new(AgentConversationRetention.Retain);

    public static AgentTurnPolicy<CadenceState> CreateTurnPolicy() =>
        new(
            maxContinuationAttempts: 2,
            (observation, _) =>
                ValueTask.FromResult<AgentTurnDirective?>(
                    !observation.Context.State.MutationAuthorized
                        ? new AgentTurnDirective(
                            """
                            Your previous response was not a lifecycle route. Continue the
                            executor turn by calling ask_planner now with the question you
                            need answered, your proposed approach, and repository evidence.
                            Do not answer with prose; the next action must be the ask_planner
                            tool call.
                            """,
                            RequiredToolName: "ask_planner"
                        )
                        : new AgentTurnDirective(
                            """
                            Your previous response was not a lifecycle route. Continue the
                             implementation, call update_outcomes as progress changes, call
                             ask_planner when direction is needed, write_checkpoint when preserving continuity, or submit_report only
                            when every packet outcome is ready for verification. Do not treat
                            prose as completion; the next response must use one lifecycle tool.
                            """
                        )
                )
        );
}
