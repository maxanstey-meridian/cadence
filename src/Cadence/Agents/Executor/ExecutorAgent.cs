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
        AgentCapability<CadenceState> resetContext,
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
                    .WithCapability(resetContext)
                    .WithMessage(ExecutorPrompts.BuildMessage)
                    .WithWorkspace(
                        agents.ExecutorWorkspace,
                        [
                            AgentTools.Always<CadenceState>(
                                "read_file",
                                "ls",
                                "grep",
                                "git:ro",
                                agents.ExecutorGitNexus,
                                agents.ExecutorWorkspace.Commands
                            ),
                            AgentTools.When<CadenceState>(
                                state => state.MutationAuthorized,
                                "write_file",
                                "delete_file",
                                "replace",
                                "replace_lines",
                                "copy_file",
                                "move_file",
                                "create_directory"
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
                            Mutation authority is not yet granted. Establish enough repository state
                            to present a concrete grounded approach, then call ask_planner with that
                            approach and evidence. Reads remain available for establishing the required
                            repository facts. Continue only on Proceed.
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
        )
        {
            DisableCompaction = profile.DisableCompaction,
        };
}
