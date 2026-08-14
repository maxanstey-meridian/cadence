namespace Cadence;

public sealed class PlannerFailureStage
{
    public IGeneratedPipelineStep<CadenceState, GeneratedStepCompletion> Definition { get; } =
        PipelineNodes.Stage<CadenceState>(
            CadenceIds.PlannerFailure,
            (state, _) => ValueTask.FromResult(state.RecordPlannerFailure())
        );
}

public sealed class PlannerUnavailable : IPipelineFailure<CadenceState>
{
    public string Id => CadenceIds.PlannerUnavailable;

    public string Summarize(CadenceState state) =>
        $"Planner unavailable after {state.PlannerFailureCount} agent failures.";
}
