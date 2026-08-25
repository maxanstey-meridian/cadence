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

public sealed class OutcomeProgressValidator : AbstractValidator<OutcomeProgress>
{
    public OutcomeProgressValidator()
    {
        RuleFor(x => x.OutcomeId).NotEmpty();
        RuleFor(x => x.Status).IsInEnum();
        RuleFor(x => x.Evidence)
            .Must(PlannerDecisionValidator.BeMeaningful)
            .When(x => x.Status != OutcomeStatus.NotStarted);
        RuleFor(x => x.Evidence)
            .Must(string.IsNullOrEmpty)
            .When(x => x.Status == OutcomeStatus.NotStarted);
        RuleFor(x => x.NextAction)
            .Must(PlannerDecisionValidator.BeMeaningful)
            .When(x => x.Status != OutcomeStatus.Complete);
        RuleFor(x => x.NextAction).Null().When(x => x.Status == OutcomeStatus.Complete);
    }
}

public sealed class UpdateOutcomesRequestValidator : AbstractValidator<UpdateOutcomesRequest>
{
    public UpdateOutcomesRequestValidator(CadenceState? state = null)
    {
        RuleFor(x => x.Updates).NotEmpty();
        RuleForEach(x => x.Updates).SetValidator(new OutcomeProgressValidator());
        RuleFor(x => x.Updates)
            .Must(x =>
                x.Select(y => y.OutcomeId).Distinct(StringComparer.Ordinal).Count() == x.Count
            )
            .WithMessage("Outcome IDs must not be duplicated.");
        if (state is not null)
        {
            RuleForEach(x => x.Updates)
                .Must(x => state.OutcomeProgress.Any(y => y.OutcomeId == x.OutcomeId))
                .WithMessage("Unknown outcome ID.");
        }
    }
}

public sealed class ObligationClaimValidator : AbstractValidator<ObligationClaim>
{
    public ObligationClaimValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Evidence).Must(PlannerDecisionValidator.BeMeaningful);
    }
}

public sealed class SubmitReportRequestValidator : AbstractValidator<SubmitReportRequest>
{
    public SubmitReportRequestValidator(CadenceState? state = null, bool checkpoint = false)
    {
        RuleFor(x => x.Summary).NotEmpty();
        RuleFor(x => x.CommitMessage).NotEmpty();
        RuleFor(x => x.ObligationClaims).NotNull();
        RuleForEach(x => x.ObligationClaims).SetValidator(new ObligationClaimValidator());
        RuleFor(x => x.RegressionTestEvidence).Must(PlannerDecisionValidator.BeMeaningful);
        if (state is not null)
        {
            RuleFor(x => x).Custom((r, c) => Validate(r, state, checkpoint, c));
        }
    }

    private static void Validate(
        SubmitReportRequest r,
        CadenceState s,
        bool checkpoint,
        ValidationContext<SubmitReportRequest> c
    )
    {
        if (s.OutcomeProgress.Any(x => x.Status != OutcomeStatus.Complete))
        {
            c.AddFailure("outcomes", "Every outcome must be complete.");
        }

        Check(
            r.ObligationClaims ?? [],
            DeliveryObligations
                .From(s)
                .Where(x => x.Kind != DeliveryObligationKind.Outcome)
                .Select(x => x.Reference),
            "obligationClaims",
            c
        );
        if (s.ReviewRepairRequired)
        {
            c.AddFailure(
                "reviewRepair",
                "Material outcome progress is required after RequestChanges."
            );
        }

        if (checkpoint)
        {
            c.AddFailure(
                "continuityCheckpoint",
                "Call write_checkpoint before submitting a report."
            );
        }
    }

    private static void Check(
        IReadOnlyList<ObligationClaim> claims,
        IEnumerable<string> expected,
        string name,
        ValidationContext<SubmitReportRequest> c
    )
    {
        var ids = claims.Select(x => x.Id).ToArray();
        var set = expected.ToHashSet(StringComparer.Ordinal);
        foreach (var id in ids.GroupBy(x => x).Where(x => x.Count() > 1))
        {
            c.AddFailure(name, $"Duplicate ID: {id.Key}");
        }

        foreach (var id in ids.Where(x => !set.Contains(x)))
        {
            c.AddFailure(name, $"Unknown ID: {id}");
        }

        foreach (var id in set.Except(ids))
        {
            c.AddFailure(name, $"Missing ID: {id}");
        }
    }
}

public sealed class WriteCheckpointRequestValidator : AbstractValidator<WriteCheckpointRequest>
{
    public WriteCheckpointRequestValidator()
    {
        RuleFor(x => x.Summary).Must(PlannerDecisionValidator.BeMeaningful);
        RuleFor(x => x.Uncertainties).NotNull();
        RuleForEach(x => x.Uncertainties).Must(PlannerDecisionValidator.BeMeaningful);
        RuleFor(x => x.NextAction).Must(PlannerDecisionValidator.BeMeaningful);
    }
}

public sealed class ResetContextRequestValidator : AbstractValidator<ResetContextRequest>
{
    public ResetContextRequestValidator()
    {
        RuleFor(x => x.Summary).Must(PlannerDecisionValidator.BeMeaningful);
        RuleFor(x => x.Uncertainties).NotNull();
        RuleForEach(x => x.Uncertainties).Must(PlannerDecisionValidator.BeMeaningful);
        RuleFor(x => x.NextAction).Must(PlannerDecisionValidator.BeMeaningful);
        RuleFor(x => x.Reason).Must(PlannerDecisionValidator.BeMeaningful);
    }
}
