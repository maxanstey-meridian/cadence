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
                or ExecutorTransition.ContextResetRequested
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
                            Mutation authority is closed. Establish the repository facts needed for
                            the proposed implementation direction, then call ask_planner with the
                            question, proposed approach, and concrete repository evidence. Do not
                            answer with prose.
                            """,
                            RequiredToolName: "ask_planner"
                        )
                        : new AgentTurnDirective(
                            """
                            Continue with the next concrete repository action in the accepted approach
                            rather than narration. Use write_checkpoint or submit_report only when its
                            lifecycle boundary is reached. Ask Planner when new evidence requires
                            materially different direction, consequential direction remains unresolved
                            after bounded investigation, or a genuine blocker remains after materially
                            distinct attempts at the same problem; not for routine implementation decisions.
                            """
                        )
                )
        );
}
