using Tandem.Advanced;

namespace Cadence;

internal static class ExecutorGroundingPolicy
{
    private static readonly HashSet<string> _repositoryInspectionTools = new(
        [
            "file_access_read",
            "file_access_ls",
            "file_access_grep",
            "git_status",
            "git_diff",
            "git_log",
            "git_show",
            "git_blame",
            "git_changed_files",
            "git_compare",
            GitNexusTool.Name,
        ],
        StringComparer.Ordinal
    );

    internal static ValueTask AcceptInitialPlannerRequestAsync(
        AgentCapabilityAcceptanceContext<CadenceState, AskPlannerRequest> context,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (
            !context.State.MutationAuthorized
            && context.State.PlannerDecision is null
            && !context.ToolInvocations.Any(invocation =>
                invocation.Status == ToolInvocationStatus.Completed
                && _repositoryInspectionTools.Contains(invocation.Name)
            )
        )
        {
            throw new InvalidOperationException(
                "No completed repository inspection exists in this visit. Inspect implementation, tests, or call sites relevant to your proposed slice, then retry ask_planner as described in its instructions."
            );
        }

        return ValueTask.CompletedTask;
    }
}
