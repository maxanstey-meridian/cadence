namespace Cadence;

internal static class CadenceCapabilities
{
    internal static CadenceCapabilitySet Create()
    {
        var askPlanner = AgentCapabilities.Create<CadenceState, AskPlannerRequest>(
            new AskPlannerCapability(),
            (state, request) => state.RecordPlannerRequest(request)
        );
        var submitReport = AgentCapabilities.Create<CadenceState, SubmitReportRequest>(
            new SubmitReportCapability(),
            (state, request) => state.RecordImplementationReport(request)
        );
        var writeCheckpoint = AgentCapabilities.Create<CadenceState, WriteCheckpointRequest>(
            new WriteCheckpointCapability(),
            (state, request) => state.RecordCheckpoint(request)
        );
        return new CadenceCapabilitySet(askPlanner, submitReport, writeCheckpoint);
    }
}

internal sealed record CadenceCapabilitySet(
    AgentCapability<CadenceState> AskPlanner,
    AgentCapability<CadenceState> SubmitReport,
    AgentCapability<CadenceState> WriteCheckpoint
);

internal sealed class AskPlannerCapability
    : IAgentCapabilityDefinition<CadenceState, AskPlannerRequest>
{
    public string ToolName => "ask_planner";
    public string Instructions =>
        "Escalate consequential unresolved engineering direction or genuine blockage and end the current turn. Do not use for ordinary implementation decisions, deterministic gate repair, or reassurance.";
    public FluentValidation.IValidator<AskPlannerRequest> Validator { get; } =
        new AskPlannerRequestValidator();

    public string Summarize(AskPlannerRequest request) => $"Planner asked: {request.Question}";
}

internal sealed class SubmitReportCapability
    : IAgentCapabilityDefinition<CadenceState, SubmitReportRequest>
{
    public string ToolName => "submit_report";
    public string Instructions => "Submit the implementation report and end the current turn.";
    public FluentValidation.IValidator<SubmitReportRequest> Validator { get; } =
        new SubmitReportRequestValidator();

    public string Summarize(SubmitReportRequest request) => $"Report submitted: {request.Summary}";
}

internal sealed class WriteCheckpointCapability
    : IAgentCapabilityDefinition<CadenceState, WriteCheckpointRequest>
{
    public string ToolName => "write_checkpoint";
    public string Instructions =>
        "Write a checkpoint of current work state and end the current turn.";
    public FluentValidation.IValidator<WriteCheckpointRequest> Validator { get; } =
        new WriteCheckpointRequestValidator();

    public string Summarize(WriteCheckpointRequest request) =>
        $"Checkpoint written: {request.Summary}";
}
