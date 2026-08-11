using Tandem.Advanced;

namespace Cadence;

[PipelineStage(CadenceIds.Verify)]
public sealed partial class VerificationStage(VerificationOperation operation)
{
    public ValueTask<Outcome<CadenceState>> ExecuteAsync(
        CadenceState state,
        CancellationToken cancellationToken
    ) =>
        PipelineOperation.RunOutcomeAsync(
            state,
            pipeline => operation.ExecuteAsync(pipeline, cancellationToken),
            result =>
                result.Outcome.Kind is OutcomeKinds.CommandPassed or OutcomeKinds.CommandFailed
                    ? new Outcome<CadenceState>.Success(result.State)
                    : StageOutcome.Unexpected(result, CadenceIds.Verify)
        );
}
