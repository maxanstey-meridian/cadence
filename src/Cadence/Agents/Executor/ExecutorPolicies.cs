using Tandem.Advanced;

namespace Cadence;

public static class ExecutorPolicies
{
    public static AgentConversationDecision RetainUntilAcceptedReport(
        AgentMessageContext<CadenceState> context,
        AgentMessageOutcome _
    ) =>
        context.State.ExecutorTransition is ExecutorTransition.ReportSubmitted
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
                            Mutation authority is closed. Call ask_planner now with the question,
                            proposed approach, and repository evidence. Do not answer with prose.
                            """,
                            RequiredToolName: "ask_planner"
                        )
                        : new AgentTurnDirective(
                            """
                            Continue with the next concrete repository action rather than narration.
                            Use write_checkpoint or submit_report only when its lifecycle boundary is
                            reached. Ask Planner when consequential direction remains unclear after
                            bounded investigation, genuine blockage remains, or two attempts at the
                            same problem have failed; not for routine implementation decisions.
                            """
                        )
                )
        );
}
