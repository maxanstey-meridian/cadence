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
        var commitMessage = state.ExecutorTransition
            is ExecutorTransition.ReportSubmitted { Report: { CommitMessage: var message } }
            ? message
            : state.Packet.Title;
        var addResult = await git.RunAsync(state.WorkspacePath, ["add", "-A"], cancellationToken);
        EnsureSucceeded("git add", addResult, cancellationToken);
        var stagedTree = await git.RunAsync(state.WorkspacePath, ["write-tree"], cancellationToken);
        EnsureSucceeded("git write-tree", stagedTree, cancellationToken);
        var headTree = await git.RunAsync(
            state.WorkspacePath,
            ["rev-parse", "HEAD^{tree}"],
            cancellationToken
        );
        EnsureSucceeded("git rev-parse HEAD^{tree}", headTree, cancellationToken);
        var treeUnchanged = string.Equals(
            stagedTree.Stdout.Trim(),
            headTree.Stdout.Trim(),
            StringComparison.OrdinalIgnoreCase
        );
        if (treeUnchanged && state.ActiveReviewFindings.Count > 0)
        {
            return new Outcome<CadenceState>.Success(
                state with
                {
                    CandidateSha = null,
                    VerificationIndex = 0,
                    VerificationResults = [],
                    ReviewerDecision = null,
                    AcceptedCandidateSha = null,
                    ExecutorTransition = new ExecutorTransition.CandidateUnchanged(
                        "The submitted report did not change the repository tree. Make a concrete repository repair before submitting another report; claims, outcome updates, verification, or a different empty commit do not qualify."
                    ),
                }
            );
        }
        var commitResult = await git.RunAsync(
            state.WorkspacePath,
            [
                "-c",
                "user.name=Cadence",
                "-c",
                "user.email=cadence@localhost",
                "commit",
                .. treeUnchanged ? new[] { "--allow-empty" } : Array.Empty<string>(),
                "-m",
                commitMessage,
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
                ReviewerDecision = null,
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
