using Tandem.Advanced;

namespace Cadence;

public static class ReviewerPolicies
{
    public static AgentConversationDecision DiscardAfterDecision(
        AgentMessageContext<CadenceState> _,
        AgentMessageOutcome __
    ) => new(AgentConversationRetention.Discard);

    public static OutputAcceptancePolicy<CadenceState, ReviewDecision> RepositoryGrounded() =>
        observation =>
        {
            var state = observation.Context.State;
            return
                observation.Output.Decision != ReviewDecisionValue.Accept
                || state.HasCompleteSuccessfulVerification
                ? []
                :
                [
                    new StructuredOutputProblem(
                        "$verification",
                        "Accept requires successful deterministic verification for the current candidate."
                    ),
                ];
        };
}
