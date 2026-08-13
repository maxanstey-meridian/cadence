using Tandem.Advanced;

namespace Cadence;

internal static class ReviewerAgent
{
    internal static AgentDefinition<CadenceState> Create(
        CadenceAgentFactory agents,
        ReviewerDoctrine doctrine
    ) =>
        agents.Create(
            CadenceIds.Reviewer,
            "reviewer",
            ReviewerPrompts.BuildInstructions(doctrine),
            builder =>
                builder
                    .WithWorkspace(
                        agents.ReviewerWorkspace,
                        [
                            AgentTools.Always<CadenceState>("read_file", "ls", "grep", "git:ro"),
                            AgentTools.Always<CadenceState>(agents.ReviewerWorkspace.Commands),
                        ]
                    )
                    .WithMessage(state => ReviewerPrompts.BuildMessage(state, doctrine))
                    .WithOutput(
                        new ReviewDecisionOutput(doctrine),
                        (state, decision) => state.RecordReviewDecision(decision)
                    )
                    .RequireOutputAcceptance(ReviewerPolicies.RepositoryGrounded())
                    .WithConversationPolicy(ReviewerPolicies.DiscardAfterDecision)
        );
}
