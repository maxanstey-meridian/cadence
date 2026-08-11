namespace Cadence;

public sealed class RunReady : IPipelineCompletion<CadenceState>
{
    public string Id => CadenceIds.Complete;

    public string Summarize(CadenceState state) => "Run ready";
}

public sealed class RunFailed : IPipelineFailure<CadenceState>
{
    public string Id => CadenceIds.Failed;

    public string Summarize(CadenceState state) => "Run failed";
}
