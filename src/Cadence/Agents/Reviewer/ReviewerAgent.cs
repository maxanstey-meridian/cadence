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
                            AgentTools.Always<CadenceState>(
                                "read_file",
                                "ls",
                                "grep",
                                "git:ro",
                                agents.ReviewerGitNexus,
                                agents.ReviewerWorkspace.Commands
                            ),
                        ]
                    )
                    .WithMessage(ReviewerPrompts.BuildMessage)
                    .WithOutput(
                        new ReviewDecisionOutput(),
                        (state, decision) => state.RecordReviewDecision(decision)
                    )
                    .RequireOutputAcceptance(ReviewerPolicies.ContractComplete())
                    .WithConversationPolicy(ReviewerPolicies.DiscardAfterDecision)
        );
}
