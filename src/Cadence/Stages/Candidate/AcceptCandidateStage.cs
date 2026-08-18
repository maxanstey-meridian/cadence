using Cadence.Git;

namespace Cadence;

[PipelineStage(CadenceIds.AcceptCandidate)]
public sealed partial class AcceptCandidateStage(ReviewerDoctrine reviewerDoctrine, GitProcess git)
{
    public async ValueTask<Outcome<CadenceState>> ExecuteAsync(
        CadenceState state,
        CancellationToken cancellationToken
    )
    {
        var reviewIsStructurallyComplete =
            state.ReviewerDecision is { } review
            && new ReviewDecisionValidator(
                reviewerDoctrine,
                state.Packet.Outcomes.Select(outcome => outcome.Id),
                state.Constraints,
                state.VerificationResults,
                state.Packet.Acceptance.Select(criterion => criterion.Id)
            )
                .Validate(review)
                .IsValid;
        if (
            !reviewIsStructurallyComplete
            || state.CandidateSha is not { } candidateSha
            || state.ReviewerDecision?.Decision != ReviewDecisionValue.Accept
            || !string.Equals(
                state.ReviewerCandidateSha,
                candidateSha,
                StringComparison.OrdinalIgnoreCase
            )
            || !string.Equals(
                state.VerifiedCandidateSha,
                candidateSha,
                StringComparison.OrdinalIgnoreCase
            )
            || state.VerificationIndex != state.Packet.Verification.Count
            || state.VerificationResults.Count != state.Packet.Verification.Count
            || state.VerificationResults.Any(result => result.ExitCode != 0)
            || !string.Equals(
                state.ReviewerDecision.DoctrineHash,
                reviewerDoctrine.Sha256,
                StringComparison.Ordinal
            )
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
