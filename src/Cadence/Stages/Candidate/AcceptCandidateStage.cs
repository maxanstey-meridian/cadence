using Cadence.Git;

namespace Cadence;

[PipelineStage(CadenceIds.AcceptCandidate)]
public sealed partial class AcceptCandidateStage(GitProcess git)
{
    public async ValueTask<Outcome<CadenceState>> ExecuteAsync(
        CadenceState state,
        CancellationToken cancellationToken
    )
    {
        if (
            state.CandidateSha is not { } candidateSha
            || state.ReviewerDecision?.Decision != ReviewDecisionValue.Accept
            || !state.HasCompleteSuccessfulVerification
        )
        {
            throw new InvalidOperationException(
                "Only the exact candidate accepted by Reviewer can become publishable."
            );
        }

        var head = await git.RunAsync(
            state.WorkspacePath,
            ["rev-parse", "HEAD"],
            cancellationToken
        );
        var status = await git.RunAsync(
            state.WorkspacePath,
            ["status", "--porcelain"],
            cancellationToken
        );
        if (
            head.TimedOut
            || head.ExitCode != 0
            || !string.Equals(head.Stdout.Trim(), candidateSha, StringComparison.OrdinalIgnoreCase)
            || status.TimedOut
            || status.ExitCode != 0
            || !string.IsNullOrEmpty(status.Stdout)
        )
        {
            throw new InvalidOperationException(
                "Candidate acceptance requires the workspace HEAD to equal the exact candidate and the worktree to be clean."
            );
        }

        return new Outcome<CadenceState>.Success(
            state with
            {
                AcceptedCandidateSha = candidateSha,
            }
        );
    }
}
