using Cadence.Git;

namespace Cadence;

[PipelineStage(CadenceIds.CaptureCandidate)]
public sealed partial class CaptureCandidateStage(GitProcess git)
{
    public async ValueTask<Outcome<CadenceState>> ExecuteAsync(
        CadenceState state,
        CancellationToken cancellationToken
    )
    {
        var addResult = await git.RunAsync(state.WorkspacePath, ["add", "-A"], cancellationToken);
        EnsureSucceeded("git add", addResult, cancellationToken);
        var commitResult = await git.RunAsync(
            state.WorkspacePath,
            [
                "-c",
                "user.name=Cadence",
                "-c",
                "user.email=cadence@localhost",
                "commit",
                "--allow-empty",
                "-m",
                "Cadence candidate",
            ],
            cancellationToken
        );
        EnsureSucceeded("git commit", commitResult, cancellationToken);
        var revResult = await git.RunAsync(
            state.WorkspacePath,
            ["rev-parse", "HEAD"],
            cancellationToken
        );
        EnsureSucceeded("git rev-parse", revResult, cancellationToken);
        var candidateSha = revResult.Stdout.Trim();
        return new Outcome<CadenceState>.Success(
            state with
            {
                CandidateSha = candidateSha,
                VerificationIndex = 0,
                VerificationResults = [],
                VerifiedCandidateSha = null,
                ReviewerDecision = null,
                ReviewerCandidateSha = null,
                AcceptedCandidateSha = null,
            }
        );
    }

    private static void EnsureSucceeded(
        string operation,
        GitResult result,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (result.ExitCode == 0 && !result.TimedOut)
        {
            return;
        }
        var evidence = string.IsNullOrWhiteSpace(result.Stderr)
            ? result.Stdout.Trim()
            : result.Stderr.Trim();
        throw new InvalidOperationException(
            $"{operation} failed (exit code {result.ExitCode}, timed out: {result.TimedOut}). {evidence}"
        );
    }
}
