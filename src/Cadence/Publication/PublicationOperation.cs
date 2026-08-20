using System.Text;
using Cadence.Git;

namespace Cadence;

public sealed class PublicationOperation(GitProcess git)
{
    public async ValueTask<PublicationResultRecord> ExecuteAsync(
        CadenceState state,
        string? explicitBranch,
        CancellationToken cancellationToken
    )
    {
        var candidateSha =
            state.AcceptedCandidateSha
            ?? throw new InvalidOperationException("Run has no accepted publication candidate.");
        if (!string.Equals(state.CandidateSha, candidateSha, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The accepted publication candidate no longer matches the current candidate."
            );
        }
        var branch = string.IsNullOrWhiteSpace(explicitBranch)
            ? $"cadence/{Slugify(state.Packet.Title)}-{candidateSha[..8]}"
            : explicitBranch;
        if (!branch.StartsWith("cadence/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Publication branch '{branch}' must use the isolated 'cadence/' namespace."
            );
        }
        await RequireSuccessAsync(
            null,
            ["check-ref-format", "--branch", branch],
            $"Invalid branch name '{branch}'",
            cancellationToken
        );
        var head = await RequireSuccessAsync(
            state.WorkspacePath,
            ["rev-parse", "HEAD"],
            "Could not read workspace HEAD",
            cancellationToken
        );
        if (!string.Equals(head, candidateSha, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Workspace HEAD '{head}' does not equal candidate '{candidateSha}'."
            );
        }
        await RequireSuccessAsync(
            state.Packet.Repository,
            ["cat-file", "-e", state.PinnedBaseSha],
            $"Pinned base '{state.PinnedBaseSha}' is not available",
            cancellationToken
        );

        var existing = await ReadBranchAsync(state.Packet.Repository, branch, cancellationToken);
        if (existing is not null)
        {
            return Reconcile(state.Packet.Repository, candidateSha, branch, existing);
        }

        var push = await git.RunAsync(
            state.WorkspacePath,
            ["push", state.Packet.Repository, $"{candidateSha}:refs/heads/{branch}"],
            cancellationToken
        );
        if (push.ExitCode != 0 || push.TimedOut)
        {
            var afterFailure = await ReadBranchAsync(
                state.Packet.Repository,
                branch,
                cancellationToken
            );
            if (afterFailure is null)
            {
                throw new InvalidOperationException($"git push failed: {push.Stderr.Trim()}");
            }
            return Reconcile(state.Packet.Repository, candidateSha, branch, afterFailure);
        }

        var published =
            await ReadBranchAsync(state.Packet.Repository, branch, cancellationToken)
            ?? throw new InvalidOperationException("Published branch could not be resolved.");
        return Reconcile(state.Packet.Repository, candidateSha, branch, published);
    }

    private static PublicationResultRecord Reconcile(
        string repository,
        string candidateSha,
        string branch,
        string publishedSha
    )
    {
        if (!string.Equals(publishedSha, candidateSha, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Branch '{branch}' resolves to '{publishedSha}', not candidate '{candidateSha}'."
            );
        }
        var result = new PublicationResultRecord(repository, branch, candidateSha);
        return result;
    }

    private async ValueTask<string?> ReadBranchAsync(
        string repository,
        string branch,
        CancellationToken cancellationToken
    )
    {
        var result = await git.RunAsync(
            repository,
            ["rev-parse", "--verify", $"refs/heads/{branch}"],
            cancellationToken
        );
        return result.ExitCode == 0 && !result.TimedOut ? result.Stdout.Trim() : null;
    }

    private async ValueTask<string> RequireSuccessAsync(
        string? workingDirectory,
        IReadOnlyList<string> arguments,
        string message,
        CancellationToken cancellationToken
    )
    {
        var result = await git.RunAsync(workingDirectory, arguments, cancellationToken);
        if (result.ExitCode != 0 || result.TimedOut)
        {
            throw new InvalidOperationException($"{message}: {result.Stderr.Trim()}");
        }
        return result.Stdout.Trim();
    }

    private static string Slugify(string input)
    {
        var slug = new StringBuilder();
        var previousDash = false;
        foreach (var character in input.ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                slug.Append(character);
                previousDash = false;
            }
            else if (!previousDash && slug.Length > 0)
            {
                slug.Append('-');
                previousDash = true;
            }
        }
        return slug.ToString().Trim('-');
    }
}
