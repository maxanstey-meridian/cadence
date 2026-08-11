using System.Collections.Concurrent;
using Cadence.Git;
using Tandem.Advanced;

namespace Cadence;

public sealed class DirtyWorkCheckpointPolicy(GitProcess git, TimeProvider timeProvider)
{
    private static readonly TimeSpan _interval = TimeSpan.FromMinutes(5);
    private readonly ConcurrentDictionary<string, byte> _required = new(StringComparer.Ordinal);

    public bool IsRequired(string workspacePath) => _required.ContainsKey(workspacePath);

    public void MarkContinuity(string workspacePath) => _required.TryRemove(workspacePath, out _);

    public async ValueTask<ToolInterceptionResult?> InterceptAsync(
        AgentMessageContext<CadenceState> context,
        ToolInvocation invocation,
        CancellationToken cancellationToken
    )
    {
        if (
            invocation.Effect != ToolEffect.WorkspaceMutation
            || timeProvider.GetUtcNow() - context.State.LastContinuityAt < _interval
        )
        {
            return null;
        }

        var status = await git.RunAsync(
            context.State.WorkspacePath,
            ["status", "--porcelain"],
            cancellationToken
        );
        if (status.ExitCode != 0 || status.TimedOut)
        {
            throw new InvalidOperationException(
                $"Could not inspect dirty-work checkpoint state: {status.Stderr.Trim()}"
            );
        }

        if (string.IsNullOrWhiteSpace(status.Stdout))
        {
            return null;
        }
        _required[context.State.WorkspacePath] = 0;
        return new ToolInterceptionResult.Blocked(
            "CONTINUITY CHECKPOINT REQUIRED: Your edit was NOT applied. "
                + "Call write_checkpoint with your current understanding, uncertainties, "
                + "and exact next action. This checkpoint retains your current Executor session."
        );
    }
}
