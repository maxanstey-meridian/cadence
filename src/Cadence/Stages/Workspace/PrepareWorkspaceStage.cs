namespace Cadence;

[PipelineStage(CadenceIds.Prepare)]
public sealed partial class PrepareWorkspaceStage(WorkspacePreparation preparation)
{
    public async ValueTask<Outcome<CadenceState>> ExecuteAsync(
        CadenceState state,
        CancellationToken cancellationToken
    )
    {
        var reviewRecovery = state.CandidateSha is not null && state.ReviewerDecision is null;
        var recovering =
            reviewRecovery
            || state.ExecutorTransition
                is ExecutorTransition.PlannerRequested
                {
                    Request.QuestionType: PlannerQuestionType.SessionReliability,
                };
        var prepared =
            reviewRecovery
                ? await preparation.ValidateReviewWorkspaceAsync(
                    state.WorkspacePath,
                    cancellationToken
                )
            : recovering
                ? await preparation.ValidateExistingAsync(
                    state.PinnedBaseSha,
                    state.WorkspacePath,
                    cancellationToken
                )
            : await preparation.PrepareAsync(state.Packet, state.WorkspacePath, cancellationToken);
        return new Outcome<CadenceState>.Success(
            state with
            {
                PinnedBaseSha = reviewRecovery ? state.PinnedBaseSha : prepared.PinnedBaseSha,
            }
        );
    }
}
