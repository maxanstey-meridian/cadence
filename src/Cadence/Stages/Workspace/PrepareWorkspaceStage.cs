namespace Cadence;

[PipelineStage(CadenceIds.Prepare)]
public sealed partial class PrepareWorkspaceStage(WorkspacePreparation preparation)
{
    public async ValueTask<Outcome<CadenceState>> ExecuteAsync(
        CadenceState state,
        CancellationToken cancellationToken
    )
    {
        var cappedReviewAwaitingHuman =
            state.ReviewerDecision?.Decision == ReviewDecisionValue.RequestChanges
            && state.ReviewAttempt >= state.MaximumReviewAttempts
            && state.ReviewerHumanAnswer is null or ReviewerHumanAnswer.HumanDecision;
        var reviewRecovery =
            state.CandidateSha is not null
            && (
                state.ReviewerDecision?.Decision != ReviewDecisionValue.RequestChanges
                || cappedReviewAwaitingHuman
            );
        var workspaceExists = Directory.Exists(state.WorkspacePath);
        if (state.ResumeRequested && !workspaceExists)
        {
            throw new WorkspacePreparationException(
                $"Retained workspace '{state.WorkspacePath}' does not exist."
            );
        }

        if (reviewRecovery)
        {
            await preparation.ValidateReviewWorkspaceAsync(
                state.CandidateSha!,
                state.WorkspacePath,
                cancellationToken
            );
            return new Outcome<CadenceState>.Success(state);
        }

        var prepared = state.ResumeRequested
            ? await preparation.ValidateExistingAsync(
                state.PinnedBaseSha,
                state.WorkspacePath,
                cancellationToken
            )
            : await preparation.PrepareAsync(state.Packet, state.WorkspacePath, cancellationToken);
        return new Outcome<CadenceState>.Success(
            state with
            {
                PinnedBaseSha = prepared.PinnedBaseSha,
            }
        );
    }
}
