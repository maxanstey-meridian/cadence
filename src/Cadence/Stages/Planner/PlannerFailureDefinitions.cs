namespace Cadence;

public sealed class PlannerFailureStage(ICadenceRecordSink records)
{
    public IGeneratedPipelineStep<CadenceState, GeneratedStepCompletion> Definition { get; } =
        PipelineNodes.Stage<CadenceState>(
            CadenceIds.PlannerFailure,
            async (state, cancellationToken) =>
            {
                var failed = state.RecordPlannerFailure();
                await records.AcceptPlannerFailureCountAsync(
                    failed.PlannerFailureCount,
                    cancellationToken
                );
                return failed;
            }
        );
}

public sealed class PlannerUnavailable : IPipelineFailure<CadenceState>
{
    public string Id => CadenceIds.PlannerUnavailable;

    public string Summarize(CadenceState state) =>
        $"Planner unavailable after {state.PlannerFailureCount} agent failures.";
}
