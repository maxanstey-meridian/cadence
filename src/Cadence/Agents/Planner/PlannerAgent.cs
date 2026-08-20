using Tandem.Advanced;

namespace Cadence;

internal static class PlannerAgent
{
    internal static AgentDefinition<CadenceState> Create(CadenceAgentFactory agents) =>
        agents.Create(
            CadenceIds.Planner,
            "planner",
            PlannerPrompts.Instructions,
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
                                "web_search",
                                "web_fetch"
                            ),
                        ]
                    )
                    .WithMessage(PlannerPrompts.BuildMessage)
                    .WithOutput(
                        new PlannerDecisionOutput(),
                        (state, decision) => state.RecordPlannerDecision(decision)
                    )
                    .ContinueSession()
        );
}
