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
            maxContinuationAttempts: 8,
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
                            implementation autonomously, call update_outcomes as progress changes,
                            write_checkpoint when the runtime requests a checkpoint, or submit_report only
                            when every packet outcome is ready for verification. Do not stop at
                            narration: take the next concrete repository action for the current
                            slice. Use a lifecycle tool when its actual boundary is reached.
                             Use ask_planner only for a runtime-required consultation, consequential
                             unresolved direction, or genuine blockage after bounded investigation;
                             never for ordinary implementation or deterministic gate repair.
                            """
                        )
                )
        );
}
