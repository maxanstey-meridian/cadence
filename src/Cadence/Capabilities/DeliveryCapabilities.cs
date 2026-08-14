namespace Cadence;

internal static class CadenceCapabilities
{
    internal static CadenceCapabilitySet Create(
        TimeProvider timeProvider,
        DirtyWorkCheckpointPolicy dirtyWorkCheckpoint
    )
    {
        var askPlanner = AgentCapabilities.Create<CadenceState, AskPlannerRequest>(
            new AskPlannerCapability(),
            (state, request) =>
            {
                dirtyWorkCheckpoint.MarkContinuity(state.WorkspacePath);
                return state.RecordPlannerRequest(request, timeProvider.GetUtcNow());
            }
        );
        var submitReport = AgentCapabilities.Create<CadenceState, SubmitReportRequest>(
            new SubmitReportCapability(dirtyWorkCheckpoint),
            (state, request) => state.RecordImplementationReport(request)
        );
        var updateOutcomes = AgentCapabilities.Create<CadenceState, UpdateOutcomesRequest>(
            new UpdateOutcomesCapability(),
            (state, request) => state.RecordOutcomeUpdates(request)
        );
        var writeCheckpoint = AgentCapabilities.Create<CadenceState, WriteCheckpointRequest>(
            new WriteCheckpointCapability(),
            (state, request) =>
            {
                dirtyWorkCheckpoint.MarkContinuity(state.WorkspacePath);
                return state.RecordCheckpoint(request, timeProvider.GetUtcNow());
            }
        );
        return new CadenceCapabilitySet(askPlanner, updateOutcomes, submitReport, writeCheckpoint);
    }
}

internal sealed record CadenceCapabilitySet(
    AgentCapability<CadenceState> AskPlanner,
    AgentCapability<CadenceState> UpdateOutcomes,
    AgentCapability<CadenceState> SubmitReport,
    AgentCapability<CadenceState> WriteCheckpoint
);

internal sealed class UpdateOutcomesCapability
    : IAgentCapabilityDefinition<CadenceState, UpdateOutcomesRequest>
{
    public string ToolName => "update_outcomes";
    public string Instructions =>
        "Atomically update one or more entries in the authoritative outcome ledger and end the current turn.";
    public FluentValidation.IValidator<UpdateOutcomesRequest> Validator { get; } =
        new UpdateOutcomesRequestValidator();

    public FluentValidation.IValidator<UpdateOutcomesRequest>? ValidatorFor(CadenceState state) =>
        new UpdateOutcomesRequestValidator(state);

    public string Summarize(UpdateOutcomesRequest request) =>
        $"Outcome ledger updated: {string.Join(", ", request.Updates.Select(update => update.OutcomeId))}";
}

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

internal sealed class SubmitReportCapability(DirtyWorkCheckpointPolicy dirtyWorkCheckpoint)
    : IAgentCapabilityDefinition<CadenceState, SubmitReportRequest>
{
    public string ToolName => "submit_report";
    public string Instructions => "Submit the implementation report and end the current turn.";
    public FluentValidation.IValidator<SubmitReportRequest> Validator { get; } =
        new SubmitReportRequestValidator();

    public FluentValidation.IValidator<SubmitReportRequest>? ValidatorFor(CadenceState state) =>
        new SubmitReportRequestValidator(
            state,
            dirtyWorkCheckpoint.IsRequired(state.WorkspacePath)
        );

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
