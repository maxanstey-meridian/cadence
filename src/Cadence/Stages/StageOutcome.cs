using Tandem.Advanced;

namespace Cadence;

internal static class StageOutcome
{
    internal static Outcome<CadenceState>.Failed Unexpected(
        OperationResult<CadenceState> result,
        string participantId
    ) =>
        new(
            result.State,
            new FailureEvidence(
                "cadence.unexpected_outcome",
                $"Participant '{participantId}' produced unexpected outcome '{result.Outcome.Kind}'."
            )
        );
}
