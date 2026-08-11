namespace Cadence;

[PipelineStage(CadenceIds.Prepare)]
public sealed partial class PrepareWorkspaceStage(WorkspacePreparation preparation)
{
    public async ValueTask<Outcome<CadenceState>> ExecuteAsync(
        CadenceState state,
        CancellationToken cancellationToken
    )
    {
        var prepared = await preparation.PrepareAsync(
            state.Packet,
            state.WorkspacePath,
            cancellationToken
        );
        return new Outcome<CadenceState>.Success(
            state with
            {
                PinnedBaseSha = prepared.PinnedBaseSha,
            }
        );
    }
}
