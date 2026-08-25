namespace Cadence;

internal static class AuthorityLifecycle
{
    internal const string ExecutorMatrix = """
        Mutation authority is an invocation-scoped, revocable lease. It determines which
        workspace tools are visible, not which capabilities the Executor permanently has.

        When unauthorized (Mutation authorized: false):
          Read-only tools are visible: file_access_read, file_access_ls, file_access_grep,
          git_status, git_diff, git_log, git_show, git_blame, git_changed_files,
          git_compare, and gitnexus.
          Mutation tools are not visible: file_access_write, file_access_delete,
          file_access_replace, file_access_replace_lines, file_access_copy,
          file_access_move, file_access_create_directory.

        When authorized (Mutation authorized: true):
          All read-only tools above, fixed packet commands, diagnostic packet verification
          commands, and every mutation tool listed above are visible.

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
