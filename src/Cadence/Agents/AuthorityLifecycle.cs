namespace Cadence;

internal static class AuthorityLifecycle
{
    internal const string ExecutorMatrix = """
        Mutation authority is an invocation-scoped, revocable lease. It determines which
        workspace tools are visible, not which capabilities the Executor permanently has.
        The visible tool list for this invocation is the mechanical truth; it is assembled
        from your registered tool set and changes with authority.

        When unauthorized, read-only repository inspection tools (file reads, listing,
        search, read-only Git, and gitnexus) are visible; mutation tools are not.
        When authorized, mutation tools, fixed packet commands, and diagnostic packet
        verification commands become visible alongside the read-only set.

        How authority changes:
          ask_planner closes authority and routes to Planner. Planner Proceed opens
          authority and returns to Executor. write_checkpoint and reset_context also
          close authority.

        The current authority value and visible tool set are mechanical facts for this
        invocation. Mutation authorized: true means Planner authorization for the current
        accepted approach has already been obtained; do not reconstruct an earlier
        authorization gate from conversation or ledger history.
        """;
}
