using Tandem.Advanced;

namespace Cadence;

public static class ExecutorPolicies
{
    public static AgentConversationDecision RetainUntilAcceptedReport(
        AgentMessageContext<CadenceState> context,
        OperationOutcome _
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
                            Mutation authority is closed. Propose the next bounded slice via ask_planner
                            as described in its instructions, grounded in repository facts you have
                            inspected; do not answer with prose instead of the tool.
                            """,
                            RequiredToolName: "ask_planner"
                        )
                        : new AgentTurnDirective(
                            """
                            Continue with the next concrete repository action in the accepted approach
                            rather than narration. If that work is finished, record material outcome progress
                            and choose the next piece; ask Planner before mutations outside existing approval.
                            Checkpoint the current piece at its lifecycle boundary. Submit a report only
                            when the whole final delivery contract is satisfied. Ask Planner when new evidence requires
                            materially different direction, consequential direction remains unresolved
                            after bounded investigation, or a genuine blocker remains after materially
                            distinct attempts at the same problem; not for routine implementation decisions.
                            """
                        )
                )
        );
}
