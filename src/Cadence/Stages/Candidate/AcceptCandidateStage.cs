using Cadence.Git;

namespace Cadence;

[PipelineStage(CadenceIds.AcceptCandidate)]
public sealed partial class AcceptCandidateStage(
    ICadenceRecordSink records,
    ReviewerDoctrine reviewerDoctrine,
    GitProcess git
)
{
    public async ValueTask<Outcome<CadenceState>> ExecuteAsync(
        CadenceState state,
        CancellationToken cancellationToken
    )
    {
        if (
            state.CandidateSha is not { } candidateSha
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

        var acceptedCandidateId = $"accepted-candidate--{candidateSha}";
        await records.AcceptPublicationCandidateAsync(
            acceptedCandidateId,
            new PublicationCandidateDocument(
                acceptedCandidateId,
                state.Packet.Repository,
                state.WorkspacePath,
                state.Packet.Title,
                state.PinnedBaseSha,
                candidateSha,
                reviewerDoctrine.Source,
                reviewerDoctrine.Sha256,
                state.ReviewerDecision.Outcomes,
                state.VerificationResults,
                state.ReviewerDecision
            ),
            cancellationToken
        );
        return new Outcome<CadenceState>.Success(state);
    }
}
