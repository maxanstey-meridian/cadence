using Tandem.Advanced;

namespace Cadence;

internal static class ExecutorAgent
{
    internal static AgentDefinition<CadenceState> Create(
        CadenceAgentFactory agents,
        AgentCapability<CadenceState> askPlanner,
        AgentCapability<CadenceState> updateOutcomes,
        AgentCapability<CadenceState> submitReport,
        AgentCapability<CadenceState> writeCheckpoint,
        DirtyWorkCheckpointPolicy dirtyWorkCheckpoint
    ) =>
        agents.Create(
            CadenceIds.Executor,
            "executor",
            ExecutorPrompts.Instructions,
            builder =>
                builder
                    .WithCapability(askPlanner)
                    .WithCapability(updateOutcomes)
                    .WithCapability(submitReport)
                    .WithMessage(ExecutorPrompts.BuildMessage)
                    .WithWorkspace(
                        agents.Workspace,
                        [
                            AgentTools.Always<CadenceState>(
                                "read_file",
                                "ls",
                                "grep",
                                "git:ro",
                                agents.Workspace.Commands
                            ),
                            AgentTools.When<CadenceState>(
                                state => state.MutationAuthorized,
                                "write_file",
                                "delete_file",
                                "replace",
                                "replace_lines"
                            ),
                        ],
                        dirtyWorkCheckpoint.InterceptAsync
                    )
                    .WithStateGuard(
                        new AgentStateGuard<CadenceState>(
                            "planner-authorization",
                            state => !state.MutationAuthorized,
                            new HashSet<ToolEffect> { ToolEffect.WorkspaceMutation },
                            """
                            MUTATION GATE CLOSED: Your edit was NOT applied — no file was changed.
                            Mutation authority is not yet granted. Call ask_planner with your
                            proposed approach and evidence. Reads remain available for gathering
                            evidence. Continue only on proceed or proceed_with_constraints.
                            """,
                            askPlanner
                        )
                    )
                    .WithCheckpoint(
                        CreateCheckpointPolicy(agents.ResolveProfile("executor"), writeCheckpoint)
                    )
                    .WithContinuationPolicy(ExecutorPolicies.CreateTurnPolicy())
                    .ContinueSession()
                    .WithConversationPolicy(ExecutorPolicies.RetainUntilAcceptedReport)
        );

    private static CheckpointPolicy<CadenceState> CreateCheckpointPolicy(
        CadenceAgentProfile profile,
        AgentCapability<CadenceState> writeCheckpoint
    ) =>
        new(
            profile.ContextWindowTokens,
            profile.MaxOutputTokens,
            profile.CheckpointAtPercent,
            writeCheckpoint,
            ExecutorPrompts.CheckpointInstructions,
            ExecutorPrompts.BuildCheckpointMessage
        );
}
