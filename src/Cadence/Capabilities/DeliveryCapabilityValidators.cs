using FluentValidation;

namespace Cadence;

public sealed class AskPlannerRequestValidator : AbstractValidator<AskPlannerRequest>
{
    public AskPlannerRequestValidator()
    {
        RuleFor(x => x.CurrentSlice).NotEmpty();
        RuleFor(x => x.Question).NotEmpty();
        RuleFor(x => x.ProposedApproach).NotEmpty();
        RuleFor(x => x.Evidence).NotNull();
        RuleForEach(x => x.Evidence).NotEmpty();
    }
}

public sealed class SubmitReportRequestValidator : AbstractValidator<SubmitReportRequest>
{
    public SubmitReportRequestValidator()
    {
        RuleFor(x => x.Summary).NotEmpty();
        RuleFor(x => x.CommitMessage).NotEmpty();
    }
}

public sealed class WriteCheckpointRequestValidator : AbstractValidator<WriteCheckpointRequest>
{
    public WriteCheckpointRequestValidator()
    {
        RuleFor(x => x.Summary).NotEmpty();
        RuleFor(x => x.Uncertainties).NotNull();
        RuleForEach(x => x.Uncertainties).NotEmpty();
        RuleFor(x => x.NextAction).NotEmpty();
    }
}
