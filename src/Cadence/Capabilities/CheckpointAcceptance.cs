using Cadence.Git;

namespace Cadence;

internal sealed class CheckpointAcceptance(GitProcess git, ICadenceRecordSink records)
{
    public async ValueTask AcceptAsync(
        string acceptedCallId,
        CadenceState state,
        WriteCheckpointRequest request,
        CancellationToken cancellationToken
    )
    {
        var changed = await ReadChangedFilesAsync(state, cancellationToken);
        var constraints = state
            .Packet.Constraints.Concat(state.PlannerConstraints)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        await records.AcceptCheckpointAsync(
            acceptedCallId,
            new ProgressCheckpointRecord(
                request.Summary,
                changed,
                constraints,
                request.Uncertainties,
                request.NextAction
            ),
            cancellationToken
        );
    }

    private async ValueTask<IReadOnlyList<string>> ReadChangedFilesAsync(
        CadenceState state,
        CancellationToken cancellationToken
    )
    {
        var tracked = await git.RunAsync(
            state.WorkspacePath,
            ["diff", "--name-only", state.PinnedBaseSha],
            cancellationToken
        );
        EnsureSucceeded("git diff --name-only", tracked);
        var untracked = await git.RunAsync(
            state.WorkspacePath,
            ["ls-files", "--others", "--exclude-standard"],
            cancellationToken
        );
        EnsureSucceeded("git ls-files", untracked);
        return Lines(tracked.Stdout)
            .Concat(Lines(untracked.Stdout))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<string> Lines(string value) =>
        value.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
        );

    private static void EnsureSucceeded(string operation, GitResult result)
    {
        if (result.ExitCode != 0 || result.TimedOut)
        {
            throw new InvalidOperationException($"{operation} failed: {result.Stderr.Trim()}");
        }
    }
}
