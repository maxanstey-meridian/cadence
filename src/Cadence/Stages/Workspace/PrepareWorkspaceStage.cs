namespace Cadence;

[PipelineStage(CadenceIds.Prepare)]
public sealed partial class PrepareWorkspaceStage(WorkspacePreparation preparation)
{
    public async ValueTask<Outcome<CadenceState>> ExecuteAsync(
        CadenceState state,
        CancellationToken cancellationToken
    )
    {
        var recovering =
            state.ExecutorTransition is ExecutorTransition.PlannerRequested
            {
                Request.QuestionType: PlannerQuestionType.SessionReliability,
            };
        var prepared = recovering
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
