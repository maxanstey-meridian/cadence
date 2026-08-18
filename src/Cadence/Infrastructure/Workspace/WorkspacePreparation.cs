using System.Text.RegularExpressions;
using Cadence.Git;

namespace Cadence;

public sealed record WorkspacePreparationResult(string PinnedBaseSha, string WorkspacePath);

public sealed class WorkspacePreparationException : Exception
{
    public WorkspacePreparationException(string message)
        : base(message) { }

    public WorkspacePreparationException(string message, Exception inner)
        : base(message, inner) { }
}

public sealed class WorkspacePreparation(GitProcess git)
{
    private static readonly Regex _shaRegex = new(
        "^[0-9a-f]{40}$|^[0-9a-f]{64}$",
        RegexOptions.Compiled
    );

    public async Task<WorkspacePreparationResult> PrepareAsync(
        Packet packet,
        string workspacePath,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var pinnedSha = await PinBaseAsync(packet, cancellationToken);
            await CloneAsync(packet, workspacePath, cancellationToken);
            await CheckoutAsync(workspacePath, pinnedSha, cancellationToken);
            await RemoveOriginAsync(workspacePath, cancellationToken);
            await VerifyHeadAsync(workspacePath, pinnedSha, cancellationToken);
            return new WorkspacePreparationResult(pinnedSha, workspacePath);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            CleanupWorkspace(workspacePath);
            throw;
        }
        catch (WorkspacePreparationException)
        {
            CleanupWorkspace(workspacePath);
            throw;
        }
        catch (Exception ex)
        {
            CleanupWorkspace(workspacePath);
            throw new WorkspacePreparationException(ex.Message, ex);
        }
    }

    public async Task<WorkspacePreparationResult> ValidateReviewWorkspaceAsync(
        string workspacePath,
        CancellationToken cancellationToken
    )
    {
        if (!Directory.Exists(workspacePath))
        {
            throw new WorkspacePreparationException($"Resume workspace not found: {workspacePath}");
        }

        var remotes = await git.RunAsync(workspacePath, ["remote"], cancellationToken);
        if (remotes.TimedOut || remotes.ExitCode != 0)
        {
            throw new WorkspacePreparationException("Resume workspace remotes could not be read.");
        }
        if (!string.IsNullOrWhiteSpace(remotes.Stdout))
        {
            throw new WorkspacePreparationException(
                "Resume workspace must not have a Git remote configured."
            );
        }

        var head = await git.RunAsync(workspacePath, ["rev-parse", "HEAD"], cancellationToken);
        if (head.TimedOut || head.ExitCode != 0)
        {
            throw new WorkspacePreparationException("Resume workspace HEAD could not be read.");
        }

        return new WorkspacePreparationResult(head.Stdout.Trim(), workspacePath);
    }

    public async Task<WorkspacePreparationResult> ValidateExistingAsync(
        string pinnedBaseSha,
        string workspacePath,
        CancellationToken cancellationToken
    )
    {
        if (!Directory.Exists(workspacePath))
        {
            throw new WorkspacePreparationException($"Resume workspace not found: {workspacePath}");
        }

        var head = await git.RunAsync(workspacePath, ["rev-parse", "HEAD"], cancellationToken);
        if (head.TimedOut || head.ExitCode != 0)
        {
            throw new WorkspacePreparationException("Resume workspace HEAD could not be read.");
        }
        if (!string.Equals(head.Stdout.Trim(), pinnedBaseSha, StringComparison.Ordinal))
        {
            throw new WorkspacePreparationException(
                $"Resume workspace HEAD '{head.Stdout.Trim()}' does not match pinned base '{pinnedBaseSha}'."
            );
        }

        var remotes = await git.RunAsync(workspacePath, ["remote"], cancellationToken);
        if (remotes.TimedOut || remotes.ExitCode != 0)
        {
            throw new WorkspacePreparationException("Resume workspace remotes could not be read.");
        }
        if (!string.IsNullOrWhiteSpace(remotes.Stdout))
        {
            throw new WorkspacePreparationException(
                "Resume workspace must not have a Git remote configured."
            );
        }

        return new WorkspacePreparationResult(pinnedBaseSha, workspacePath);
    }

    private async Task<string> PinBaseAsync(Packet packet, CancellationToken ct)
    {
        var result = await git.RunAsync(
            packet.Repository,
            ["rev-parse", "--verify", $"{packet.Base}^{{commit}}"],
            ct
        );

        if (result.TimedOut)
        {
            throw new WorkspacePreparationException(
                $"git rev-parse timed out resolving base '{packet.Base}'."
            );
        }

        if (result.ExitCode != 0)
        {
            throw new WorkspacePreparationException(
                $"git rev-parse could not resolve base '{packet.Base}' (exit {result.ExitCode}): {result.Stderr.Trim()}"
            );
        }

        var sha = result.Stdout.Trim();
        if (!_shaRegex.IsMatch(sha))
        {
            throw new WorkspacePreparationException(
                $"git rev-parse returned an unexpected value for base '{packet.Base}': '{sha}'"
            );
        }

        return sha;
    }

    private async Task CloneAsync(Packet packet, string workspacePath, CancellationToken ct)
    {
        var result = await git.RunAsync(
            null,
            ["clone", "--no-local", "--no-checkout", packet.Repository, workspacePath],
            ct
        );

        if (result.TimedOut)
        {
            throw new WorkspacePreparationException(
                $"git clone timed out cloning '{packet.Repository}'."
            );
        }

        if (result.ExitCode != 0)
        {
            throw new WorkspacePreparationException(
                $"git clone failed cloning '{packet.Repository}' (exit {result.ExitCode}): {result.Stderr.Trim()}"
            );
        }
    }

    private async Task CheckoutAsync(string workspacePath, string sha, CancellationToken ct)
    {
        var result = await git.RunAsync(
            null,
            ["-C", workspacePath, "checkout", "--detach", sha],
            ct
        );

        if (result.TimedOut)
        {
            throw new WorkspacePreparationException("git checkout timed out.");
        }

        if (result.ExitCode != 0)
        {
            throw new WorkspacePreparationException(
                $"git checkout failed (exit {result.ExitCode}): {result.Stderr.Trim()}"
            );
        }
    }

    private async Task RemoveOriginAsync(string workspacePath, CancellationToken ct)
    {
        var result = await git.RunAsync(
            null,
            ["-C", workspacePath, "remote", "remove", "origin"],
            ct
        );

        if (result.TimedOut)
        {
            throw new WorkspacePreparationException("git remote remove timed out.");
        }

        if (result.ExitCode != 0)
        {
            throw new WorkspacePreparationException(
                $"git remote remove origin failed (exit {result.ExitCode}): {result.Stderr.Trim()}"
            );
        }
    }

    private async Task VerifyHeadAsync(string workspacePath, string pinnedSha, CancellationToken ct)
    {
        var result = await git.RunAsync(null, ["-C", workspacePath, "rev-parse", "HEAD"], ct);

        if (result.TimedOut)
        {
            throw new WorkspacePreparationException(
                "git rev-parse HEAD timed out during verification."
            );
        }

        if (result.ExitCode != 0)
        {
            throw new WorkspacePreparationException(
                $"git rev-parse HEAD failed during verification (exit {result.ExitCode}): {result.Stderr.Trim()}"
            );
        }

        var head = result.Stdout.Trim();
        if (!string.Equals(head, pinnedSha, StringComparison.Ordinal))
        {
            throw new WorkspacePreparationException(
                $"Workspace HEAD '{head}' does not match pinned base '{pinnedSha}'."
            );
        }
    }

    private static void CleanupWorkspace(string workspacePath)
    {
        try
        {
            if (Directory.Exists(workspacePath))
            {
                Directory.Delete(workspacePath, recursive: true);
            }
        }
        catch { }
    }
}
