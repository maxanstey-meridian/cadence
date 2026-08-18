using FluentValidation;
using FluentValidation.Results;

namespace Cadence;

public sealed class AskPlannerRequestValidator : AbstractValidator<AskPlannerRequest>
{
    public AskPlannerRequestValidator()
    {
        RuleFor(request => request.QuestionType)
            .IsInEnum()
            .WithErrorCode("ask_planner.question_type.invalid");
        RuleFor(request => request.CurrentSlice)
            .NotEmpty()
            .WithErrorCode("ask_planner.current_slice.required");
        RuleFor(request => request.Question)
            .NotEmpty()
            .WithErrorCode("ask_planner.question.required");
        RuleFor(request => request.ProposedApproach)
            .NotEmpty()
            .WithErrorCode("ask_planner.proposed_approach.required");
        RuleFor(request => request.Evidence)
            .NotEmpty()
            .WithErrorCode("ask_planner.evidence.required");
        RuleForEach(request => request.Evidence)
            .NotEmpty()
            .WithErrorCode("ask_planner.evidence.item_required");
        RuleFor(request => request.FailedInstruction)
            .NotNull()
            .WithErrorCode("ask_planner.failed_instruction.required")
            .SetValidator(new FailedPlannerInstructionContextValidator()!)
            .When(request => request.QuestionType == PlannerQuestionType.FailedInstruction);
        RuleFor(request => request.FailedInstruction)
            .Null()
            .WithErrorCode("ask_planner.failed_instruction.forbidden")
            .When(request => request.QuestionType != PlannerQuestionType.FailedInstruction);
    }
}

public sealed class FailedPlannerInstructionContextValidator
    : AbstractValidator<FailedPlannerInstructionContext>
{
    public FailedPlannerInstructionContextValidator()
    {
        RuleFor(context => context.PriorInstruction).NotEmpty();
        RuleFor(context => context.AttemptedChange).NotEmpty();
        RuleFor(context => context.FailingCommand).NotEmpty();
        RuleFor(context => context.RelevantOutput).NotEmpty();
        RuleFor(context => context.Contradiction).NotEmpty();
        RuleFor(context => context.RevisedUnderstanding).NotEmpty();
        RuleFor(context => context.ProposedNextApproach).NotEmpty();
    }
}

public sealed class SubmitReportRequestValidator : AbstractValidator<SubmitReportRequest>
{
    public SubmitReportRequestValidator(
        CadenceState? state = null,
        bool continuityCheckpointRequired = false
    )
    {
        RuleFor(request => request.Summary)
            .NotEmpty()
            .WithErrorCode("submit_report.summary.required");
        RuleFor(request => request.CommitMessage)
            .NotEmpty()
            .WithErrorCode("submit_report.commit_message.required");
        RuleFor(request => request.AddressedConstraints)
            .NotNull()
            .WithErrorCode("submit_report.addressed_constraints.required");
        RuleForEach(request => request.AddressedConstraints)
            .SetValidator(new ConstraintClaimValidator());
        RuleFor(request => request.RegressionTests)
            .NotNull()
            .WithErrorCode("submit_report.regression_tests.required");
        RuleFor(request => request.RegressionTests)
            .SetValidator(new RegressionTestClaimValidator());
        RuleFor(request => request.AcceptanceClaims)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithErrorCode("submit_report.acceptance_claims.required")
            .Must(claims => claims.All(claim => claim is not null))
            .WithErrorCode("submit_report.acceptance_claims.null_item");
        RuleForEach(request => request.AcceptanceClaims)
            .SetValidator(new AcceptanceClaimValidator());
        if (state is not null)
        {
            RuleFor(_ => state.ReviewRepairRequired)
                .Equal(false)
                .WithErrorCode("submit_report.review_repair.required")
                .WithMessage(
                    "Update at least one outcome materially before resubmitting after RequestChanges."
                );
            RuleFor(request => request)
                .Custom((request, context) => ValidateAgainstState(request, state, context));
        }
        if (continuityCheckpointRequired)
        {
            RuleFor(_ => _)
                .Custom(
                    (_, context) =>
                        context.AddFailure(
                            new ValidationFailure(
                                "continuityCheckpoint",
                                "Call write_checkpoint before submitting a report."
                            )
                            {
                                ErrorCode = "submit_report.continuity_checkpoint.required",
                            }
                        )
                );
        }
    }

    private static void ValidateAgainstState(
        SubmitReportRequest request,
        CadenceState state,
        ValidationContext<SubmitReportRequest> context
    )
    {
        foreach (
            var incomplete in state.OutcomeLedger.Where(outcome =>
                outcome.Status != OutcomeStatus.Complete
            )
        )
        {
            AddFailure(
                context,
                nameof(state.OutcomeLedger),
                $"Outcome '{incomplete.OutcomeId}' is not complete in the authoritative ledger.",
                "submit_report.outcomes.incomplete"
            );
        }
        var acceptanceIds = (request.AcceptanceClaims ?? [])
            .Where(claim => claim is not null)
            .Select(claim => claim.AcceptanceId)
            .ToArray();
        var expectedAcceptance = state
            .Packet.Acceptance.Select(criterion => criterion.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (
            var duplicate in acceptanceIds
                .GroupBy(value => value, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
        )
        {
            AddFailure(
                context,
                nameof(request.AcceptanceClaims),
                $"Acceptance criterion '{duplicate.Key}' must be claimed exactly once.",
                "submit_report.acceptance_claims.duplicate"
            );
        }

        foreach (var unknown in acceptanceIds.Where(value => !expectedAcceptance.Contains(value)))
        {
            AddFailure(
                context,
                nameof(request.AcceptanceClaims),
                $"Unknown acceptance criterion: {unknown}",
                "submit_report.acceptance_claims.unknown"
            );
        }

        foreach (var missing in expectedAcceptance.Except(acceptanceIds))
        {
            AddFailure(
                context,
                nameof(request.AcceptanceClaims),
                $"Unaddressed acceptance criterion: {missing}",
                "submit_report.acceptance_claims.missing"
            );
        }

        var addressed = request.AddressedConstraints.Select(claim => claim.Constraint).ToArray();
        foreach (
            var duplicate in addressed
                .GroupBy(value => value, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
        )
        {
            AddFailure(
                context,
                nameof(request.AddressedConstraints),
                $"Constraint '{duplicate.Key}' must be addressed exactly once.",
                "submit_report.addressed_constraints.duplicate"
            );
        }
        foreach (
            var unknown in addressed.Where(value =>
                !state.Constraints.Contains(value, StringComparer.Ordinal)
            )
        )
        {
            AddFailure(
                context,
                nameof(request.AddressedConstraints),
                $"Unknown constraint: {unknown}",
                "submit_report.addressed_constraints.unknown"
            );
        }
        foreach (var constraint in state.Constraints.Except(addressed, StringComparer.Ordinal))
        {
            AddFailure(
                context,
                nameof(request.AddressedConstraints),
                $"Unaddressed constraint: {constraint}",
                "submit_report.addressed_constraints.missing"
            );
        }
    }

    private static void AddFailure(
        ValidationContext<SubmitReportRequest> context,
        string propertyName,
        string message,
        string errorCode
    ) => context.AddFailure(new ValidationFailure(propertyName, message) { ErrorCode = errorCode });
}

public sealed class AcceptanceClaimValidator : AbstractValidator<AcceptanceClaim>
{
    public AcceptanceClaimValidator()
    {
        RuleFor(claim => claim.AcceptanceId)
            .NotEmpty()
            .WithErrorCode("acceptance_claim.id.required");
        RuleFor(claim => claim.Evidence)
            .Must(PlannerDecisionValidator.BeMeaningful)
            .WithErrorCode("acceptance_claim.evidence.meaningful");
    }
}

public sealed class RegressionTestClaimValidator : AbstractValidator<RegressionTestClaim>
{
    public RegressionTestClaimValidator()
    {
        RuleFor(claim => claim.Disposition)
            .IsInEnum()
            .WithErrorCode("regression_tests.disposition.invalid");
        RuleFor(claim => claim.Evidence)
            .NotEmpty()
            .WithErrorCode("regression_tests.evidence.required");
        RuleForEach(claim => claim.Evidence)
            .Must(PlannerDecisionValidator.BeMeaningful)
            .WithErrorCode("regression_tests.evidence.meaningful");
    }
}

public sealed class ConstraintClaimValidator : AbstractValidator<ConstraintClaim>
{
    public ConstraintClaimValidator()
    {
        RuleFor(claim => claim.Constraint)
            .NotEmpty()
            .WithErrorCode("constraint_claim.constraint.required");
        RuleFor(claim => claim.Evidence)
            .NotEmpty()
            .WithErrorCode("constraint_claim.evidence.required");
    }
}

public sealed class UpdateOutcomesRequestValidator : AbstractValidator<UpdateOutcomesRequest>
{
    public UpdateOutcomesRequestValidator(CadenceState? state = null)
    {
        RuleFor(request => request.Updates)
            .NotEmpty()
            .WithErrorCode("update_outcomes.updates.required");
        RuleForEach(request => request.Updates).SetValidator(new OutcomeUpdateValidator());
        RuleFor(request => request.Updates)
            .Must(updates =>
                updates.Select(update => update.OutcomeId).Distinct(StringComparer.Ordinal).Count()
                == updates.Count
            )
            .WithErrorCode("update_outcomes.updates.duplicate");
        if (state is not null)
        {
            var expected = state
                .OutcomeLedger.Select(outcome => outcome.OutcomeId)
                .ToHashSet(StringComparer.Ordinal);
            RuleForEach(request => request.Updates)
                .Must(update => expected.Contains(update.OutcomeId))
                .WithErrorCode("update_outcomes.outcome.unknown");
        }
    }
}

public sealed class OutcomeUpdateValidator : AbstractValidator<OutcomeUpdate>
{
    public OutcomeUpdateValidator()
    {
        RuleFor(update => update.OutcomeId).NotEmpty().WithErrorCode("outcome_update.id.required");
        RuleFor(update => update.Status).IsInEnum().WithErrorCode("outcome_update.status.invalid");
        RuleFor(update => update.Evidence)
            .NotNull()
            .WithErrorCode("outcome_update.evidence.required");
        RuleForEach(update => update.Evidence)
            .NotEmpty()
            .WithErrorCode("outcome_update.evidence.item_required");
        RuleFor(update => update.Evidence)
            .NotEmpty()
            .When(update =>
                update.Status
                    is OutcomeStatus.InProgress
                        or OutcomeStatus.Blocked
                        or OutcomeStatus.Complete
            )
            .WithErrorCode("outcome_update.evidence.empty");
        RuleFor(update => update.ImplementationState)
            .NotEmpty()
            .WithErrorCode("outcome_update.implementation_state.required");
        RuleFor(update => update.NextAction)
            .NotEmpty()
            .When(update => update.Status != OutcomeStatus.Complete)
            .WithErrorCode("outcome_update.next_action.required");
        RuleFor(update => update.NextAction)
            .Null()
            .When(update => update.Status == OutcomeStatus.Complete)
            .WithErrorCode("outcome_update.next_action.forbidden");
    }
}

public sealed class WriteCheckpointRequestValidator : AbstractValidator<WriteCheckpointRequest>
{
    public WriteCheckpointRequestValidator()
    {
        RuleFor(request => request.Summary)
            .NotEmpty()
            .WithErrorCode("write_checkpoint.summary.required");
        RuleFor(request => request.Uncertainties)
            .NotNull()
            .WithErrorCode("write_checkpoint.uncertainties.required");
        RuleForEach(request => request.Uncertainties)
            .NotEmpty()
            .WithErrorCode("write_checkpoint.uncertainties.item_required");
        RuleFor(request => request.NextAction)
            .NotEmpty()
            .WithErrorCode("write_checkpoint.next_action.required");
    }
}
