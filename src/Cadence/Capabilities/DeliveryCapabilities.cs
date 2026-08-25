using Tandem.Advanced;

namespace Cadence;

internal static class CadenceCapabilities
{
    internal static CadenceCapabilitySet Create(TimeProvider time, DirtyWorkCheckpointPolicy dirty)
    {
        var ask = AgentCapabilities
            .Create<CadenceState, AskPlannerRequest>(
                new AskPlannerCapability(),
                (s, r) => s.RecordPlannerRequest(r) with { LastContinuityAt = time.GetUtcNow() }
            )
            .WithAcceptance(ExecutorGroundingPolicy.AcceptInitialPlannerRequestAsync);
        var update = AgentCapabilities.Create<CadenceState, UpdateOutcomesRequest>(
            new UpdateOutcomesCapability(),
            (s, r) => s.RecordOutcomeUpdates(r)
        );
        var report = AgentCapabilities.Create<CadenceState, SubmitReportRequest>(
            new SubmitReportCapability(dirty),
            (s, r) => s.RecordImplementationReport(r)
        );
        var checkpoint = AgentCapabilities.Create<CadenceState, WriteCheckpointRequest>(
            new WriteCheckpointCapability(),
            (s, r) =>
            {
                dirty.ClearRequirement(s.WorkspacePath);
                return s.RecordCheckpoint(r, time.GetUtcNow());
            }
        );
        var reset = AgentCapabilities.Create<CadenceState, ResetContextRequest>(
            new ResetContextCapability(),
            (s, r) =>
            {
                dirty.ClearRequirement(s.WorkspacePath);
                return s.RecordContextReset(r, time.GetUtcNow());
            }
        );
        return new(ask, update, report, checkpoint, reset);
    }
}

internal sealed record CadenceCapabilitySet(
    AgentCapability<CadenceState> AskPlanner,
    AgentCapability<CadenceState> UpdateOutcomes,
    AgentCapability<CadenceState> SubmitReport,
    AgentCapability<CadenceState> WriteCheckpoint,
    AgentCapability<CadenceState> ResetContext
);

internal sealed class AskPlannerCapability
    : IAgentCapabilityDefinition<CadenceState, AskPlannerRequest>
{
    public string ToolName => "ask_planner";
    public string Instructions =>
        "Request Planner authorization for a concrete repository-grounded approach when mutation authority is closed, or escalate consequential unresolved engineering direction when new evidence invalidates the accepted approach. Ends the turn. Do not use for progress reports, check-ins, reassurance, or routine implementation decisions.";
    public FluentValidation.IValidator<AskPlannerRequest> Validator { get; } =
        new AskPlannerRequestValidator();

    public string Summarize(AskPlannerRequest r) => string.Empty;
}

internal sealed class UpdateOutcomesCapability
    : IAgentCapabilityDefinition<CadenceState, UpdateOutcomesRequest>
{
    public string ToolName => "update_outcomes";
    public string Instructions =>
        "Replace one or more durable outcome-progress entries and end the turn. Use when established repository evidence materially changes an outcome's durable status, evidence, or remaining work.";
    public FluentValidation.IValidator<UpdateOutcomesRequest> Validator { get; } =
        new UpdateOutcomesRequestValidator();

    public FluentValidation.IValidator<UpdateOutcomesRequest>? ValidatorFor(CadenceState s) =>
        new UpdateOutcomesRequestValidator(s);

    public string Summarize(UpdateOutcomesRequest r) => string.Empty;
}

internal sealed class SubmitReportCapability(DirtyWorkCheckpointPolicy dirty)
    : IAgentCapabilityDefinition<CadenceState, SubmitReportRequest>
{
    public string ToolName => "submit_report";
    public string Instructions =>
        "Submit the implementation report and end the turn. The report records Executor claims about the delivered implementation and the concrete repository evidence supporting them; acceptance of the report does not substitute for Planner or Reviewer inspection.";
    public FluentValidation.IValidator<SubmitReportRequest> Validator { get; } =
        new SubmitReportRequestValidator();

    public FluentValidation.IValidator<SubmitReportRequest>? ValidatorFor(CadenceState s) =>
        new SubmitReportRequestValidator(s, dirty.IsRequired(s.WorkspacePath));

    public string Summarize(SubmitReportRequest r) => string.Empty;
}

internal sealed class WriteCheckpointCapability
    : IAgentCapabilityDefinition<CadenceState, WriteCheckpointRequest>
{
    public string ToolName => "write_checkpoint";
    public string Instructions => "Checkpoint current work and route through Planner.";
    public FluentValidation.IValidator<WriteCheckpointRequest> Validator { get; } =
        new WriteCheckpointRequestValidator();

    public string Summarize(WriteCheckpointRequest r) => string.Empty;
}

internal sealed class ResetContextCapability
    : IAgentCapabilityDefinition<CadenceState, ResetContextRequest>
{
    public string ToolName => "reset_context";
    public string Instructions =>
        "Use only when this Executor conversation is unreliable or contradictory; checkpoint and discard it.";
    public FluentValidation.IValidator<ResetContextRequest> Validator { get; } =
        new ResetContextRequestValidator();

    public string Summarize(ResetContextRequest r) => string.Empty;
}
