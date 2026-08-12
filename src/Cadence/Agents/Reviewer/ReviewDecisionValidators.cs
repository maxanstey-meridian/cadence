using FluentValidation;
using FluentValidation.Results;

namespace Cadence;

public sealed class ReviewDecisionValidator : AbstractValidator<ReviewDecision>
{
    public ReviewDecisionValidator(
        ReviewerDoctrine doctrine,
        IEnumerable<string>? expectedOutcomeIds = null,
        IEnumerable<string>? expectedConstraints = null,
        IEnumerable<VerificationResult>? verificationResults = null
    )
    {
        var outcomes = expectedOutcomeIds?.ToHashSet(StringComparer.Ordinal) ?? [];
        var constraints = expectedConstraints?.ToHashSet(StringComparer.Ordinal) ?? [];
        var verification = verificationResults?.ToArray() ?? [];
        var evidenceValidator = new ReviewEvidenceReferenceValidator(
            doctrine,
            outcomes,
            constraints,
            verification
        );

        RuleFor(decision => decision.Decision).IsInEnum().WithErrorCode("review.decision.invalid");
        RuleFor(decision => decision.DoctrineHash)
            .Equal(doctrine.Sha256)
            .WithErrorCode("review.doctrine_hash.mismatch");
        RuleFor(decision => decision.Summary)
            .Must(PlannerDecisionValidator.BeMeaningful)
            .WithErrorCode("review.summary.meaningful");
        RuleFor(decision => decision.Outcomes).NotNull().WithErrorCode("review.outcomes.required");
        RuleForEach(decision => decision.Outcomes)
            .SetValidator(new ReviewOutcomeAssessmentValidator(evidenceValidator));
        RuleFor(decision => decision.Findings).NotNull().WithErrorCode("review.findings.required");
        RuleForEach(decision => decision.Findings)
            .SetValidator(new ReviewFindingValidator(evidenceValidator));
        RuleFor(decision => decision.ConstraintAssessments)
            .NotNull()
            .WithErrorCode("review.constraint_assessments.required");
        RuleForEach(decision => decision.ConstraintAssessments)
            .SetValidator(new ReviewConstraintAssessmentValidator(evidenceValidator));
        RuleFor(decision => decision)
            .Custom((decision, context) => ValidateOutcomeCoverage(decision, outcomes, context));
        RuleFor(decision => decision)
            .Custom(
                (decision, context) => ValidateConstraintCoverage(decision, constraints, context)
            );
        RuleFor(decision => decision.Findings)
            .Must(findings =>
                findings is not null
                && findings.Any(finding =>
                    finding.Severity is ReviewFindingSeverity.Critical or ReviewFindingSeverity.High
                )
            )
            .WithErrorCode("review.findings.blocker_required_for_changes")
            .When(decision => decision.Decision == ReviewDecisionValue.RequestChanges);
        RuleFor(decision => decision.Findings)
            .Must(findings =>
                findings is not null
                && findings.All(finding =>
                    finding.Severity is ReviewFindingSeverity.Medium or ReviewFindingSeverity.Low
                )
            )
            .WithErrorCode("review.findings.blocker_forbidden_for_accept")
            .When(decision => decision.Decision == ReviewDecisionValue.Accept);
        RuleFor(decision => decision.HumanQuestion)
            .NotEmpty()
            .WithErrorCode("review.human_question.required")
            .Must(question => !string.Equals(question, "N/A", StringComparison.OrdinalIgnoreCase))
            .WithErrorCode("review.human_question.meaningful")
            .When(decision => decision.Decision == ReviewDecisionValue.NeedsHuman);
        RuleFor(decision => decision.HumanQuestion)
            .Null()
            .WithErrorCode("review.human_question.forbidden")
            .When(decision => decision.Decision != ReviewDecisionValue.NeedsHuman);
        RuleFor(decision => decision.HumanDecisionDomain)
            .NotNull()
            .WithErrorCode("review.human_decision_domain.required")
            .IsInEnum()
            .WithErrorCode("review.human_decision_domain.invalid")
            .When(decision => decision.Decision == ReviewDecisionValue.NeedsHuman);
        RuleFor(decision => decision.HumanDecisionDomain)
            .Null()
            .WithErrorCode("review.human_decision_domain.forbidden")
            .When(decision => decision.Decision != ReviewDecisionValue.NeedsHuman);
    }

    private static void ValidateConstraintCoverage(
        ReviewDecision decision,
        IReadOnlySet<string> expected,
        ValidationContext<ReviewDecision> context
    )
    {
        var assessments = decision.ConstraintAssessments ?? [];
        AddCoverageFailures(
            assessments.Select(x => x.Constraint),
            expected,
            "constraintAssessments",
            "Planner constraint",
            "review.constraint_assessments",
            context
        );
        foreach (var assessment in assessments)
        {
            if (
                !assessment.Evidence.Any(reference =>
                    reference.Kind == ReviewEvidenceKind.Constraint
                    && string.Equals(
                        reference.Constraint,
                        assessment.Constraint,
                        StringComparison.Ordinal
                    )
                )
            )
            {
                context.AddFailure(
                    new ValidationFailure(
                        "constraintAssessments",
                        $"Constraint '{assessment.Constraint}' requires its exact typed reference."
                    )
                    {
                        ErrorCode = "review.constraint_assessments.reference_required",
                    }
                );
            }
        }
        if (
            decision.Decision == ReviewDecisionValue.Accept
            && assessments.Any(assessment => !assessment.Satisfied)
        )
        {
            context.AddFailure(
                new ValidationFailure(
                    "constraintAssessments",
                    "Accept requires every constraint to be satisfied."
                )
                {
                    ErrorCode = "review.constraint_assessments.unsatisfied_for_accept",
                }
            );
        }
    }

    private static void ValidateOutcomeCoverage(
        ReviewDecision decision,
        IReadOnlySet<string> expected,
        ValidationContext<ReviewDecision> context
    )
    {
        if (expected.Count == 0)
        {
            return;
        }
        var assessments = decision.Outcomes ?? [];
        AddCoverageFailures(
            assessments.Select(x => x.OutcomeId),
            expected,
            "outcomes",
            "Outcome",
            "review.outcomes",
            context
        );
        if (
            decision.Decision == ReviewDecisionValue.Accept
            && assessments.Any(outcome => !outcome.Delivered)
        )
        {
            context.AddFailure(
                new ValidationFailure(
                    "outcomes",
                    "Accept requires every packet outcome to be delivered."
                )
                {
                    ErrorCode = "review.outcomes.undelivered_for_accept",
                }
            );
        }
    }

    private static void AddCoverageFailures(
        IEnumerable<string> actual,
        IReadOnlySet<string> expected,
        string property,
        string label,
        string codePrefix,
        ValidationContext<ReviewDecision> context
    )
    {
        var values = actual.ToArray();
        foreach (
            var duplicate in values
                .GroupBy(value => value, StringComparer.Ordinal)
                .Where(group => group.Count() > 1)
        )
        {
            context.AddFailure(
                new ValidationFailure(
                    property,
                    $"{label} '{duplicate.Key}' must be assessed exactly once."
                )
                {
                    ErrorCode = $"{codePrefix}.duplicate",
                }
            );
        }
        foreach (var unknown in values.Where(value => !expected.Contains(value)))
        {
            context.AddFailure(
                new ValidationFailure(property, $"Unknown {label.ToLowerInvariant()} '{unknown}'.")
                {
                    ErrorCode = $"{codePrefix}.unknown",
                }
            );
        }
        foreach (var missing in expected.Except(values))
        {
            context.AddFailure(
                new ValidationFailure(
                    property,
                    $"Missing assessment for {label.ToLowerInvariant()} '{missing}'."
                )
                {
                    ErrorCode = $"{codePrefix}.missing",
                }
            );
        }
    }
}

public sealed class ReviewDecisionOutput(ReviewerDoctrine doctrine)
    : IAgentOutputDefinition<CadenceState, ReviewDecision>
{
    public string Instructions =>
        "Return a doctrine-bound, reproducibly evidenced review decision covering every packet outcome.";

    public IValidator<ReviewDecision> Validator { get; } = new ReviewDecisionValidator(doctrine);

    public IValidator<ReviewDecision> ValidatorFor(CadenceState state) =>
        new ReviewDecisionValidator(
            doctrine,
            state.Packet.Outcomes.Select(outcome => outcome.Id),
            state.Constraints,
            state.VerificationResults
        );

    public IReadOnlyList<AgentOutputExample<ReviewDecision>> Examples(CadenceState state) => [];
}

public sealed class ReviewConstraintAssessmentValidator
    : AbstractValidator<ReviewConstraintAssessment>
{
    public ReviewConstraintAssessmentValidator(
        IValidator<ReviewEvidenceReference> evidenceValidator
    )
    {
        RuleFor(assessment => assessment.Constraint)
            .Must(PlannerDecisionValidator.BeMeaningful)
            .WithErrorCode("review.constraint_assessment.constraint.meaningful");
        RuleFor(assessment => assessment.Evidence)
            .NotEmpty()
            .WithErrorCode("review.constraint_assessment.evidence.required");
        RuleForEach(assessment => assessment.Evidence).SetValidator(evidenceValidator);
    }
}

public sealed class ReviewOutcomeAssessmentValidator : AbstractValidator<ReviewOutcomeAssessment>
{
    public ReviewOutcomeAssessmentValidator(IValidator<ReviewEvidenceReference> evidenceValidator)
    {
        RuleFor(outcome => outcome.OutcomeId)
            .Must(PlannerDecisionValidator.BeMeaningful)
            .WithErrorCode("review.outcome.id.meaningful");
        RuleFor(outcome => outcome.Evidence)
            .NotEmpty()
            .WithErrorCode("review.outcome.evidence.required");
        RuleForEach(outcome => outcome.Evidence).SetValidator(evidenceValidator);
    }
}

public sealed class ReviewFindingValidator : AbstractValidator<ReviewFinding>
{
    public ReviewFindingValidator(IValidator<ReviewEvidenceReference> evidenceValidator)
    {
        RuleFor(finding => finding.Severity)
            .IsInEnum()
            .WithErrorCode("review.finding.severity.invalid");
        RuleFor(finding => finding.Description)
            .Must(PlannerDecisionValidator.BeMeaningful)
            .WithErrorCode("review.finding.description.meaningful");
        RuleFor(finding => finding.Evidence)
            .NotEmpty()
            .WithErrorCode("review.finding.evidence.required")
            .Must(evidence => evidence.Any(x => x.Kind == ReviewEvidenceKind.DoctrineClause))
            .WithErrorCode("review.finding.doctrine_clause.required")
            .Must(evidence => evidence.Any(x => x.Kind != ReviewEvidenceKind.DoctrineClause))
            .WithErrorCode("review.finding.defect_evidence.required");
        RuleForEach(finding => finding.Evidence).SetValidator(evidenceValidator);
    }
}

public sealed class ReviewEvidenceReferenceValidator : AbstractValidator<ReviewEvidenceReference>
{
    public ReviewEvidenceReferenceValidator(
        ReviewerDoctrine doctrine,
        IReadOnlySet<string> outcomes,
        IReadOnlySet<string> constraints,
        IReadOnlyList<VerificationResult> verification
    )
    {
        RuleFor(reference => reference.Kind)
            .IsInEnum()
            .WithErrorCode("review.evidence.kind.invalid");
        RuleFor(reference => reference)
            .Custom(
                (reference, context) =>
                    Validate(reference, doctrine, outcomes, constraints, verification, context)
            );
    }

    private static void Validate(
        ReviewEvidenceReference reference,
        ReviewerDoctrine doctrine,
        IReadOnlySet<string> outcomes,
        IReadOnlySet<string> constraints,
        IReadOnlyList<VerificationResult> verification,
        ValidationContext<ReviewEvidenceReference> context
    )
    {
        var valid = reference.Kind switch
        {
            ReviewEvidenceKind.FileLine => !string.IsNullOrWhiteSpace(reference.Path)
                && reference.Line > 0,
            ReviewEvidenceKind.Symbol => !string.IsNullOrWhiteSpace(reference.Symbol),
            ReviewEvidenceKind.VerificationCommand => verification.Any(result =>
                string.Equals(result.Command, reference.Command, StringComparison.Ordinal)
                && (
                    result.ExitCode == reference.ExitCode
                        && string.Equals(result.Stdout, reference.Stdout, StringComparison.Ordinal)
                        && string.Equals(result.Stderr, reference.Stderr, StringComparison.Ordinal)
                    || reference.ExitCode is not null
                        && reference.Stdout is not null
                        && reference.Stderr is not null
                )
            ),
            ReviewEvidenceKind.PacketOutcome => reference.OutcomeId is not null
                && outcomes.Contains(reference.OutcomeId),
            ReviewEvidenceKind.Constraint => reference.Constraint is not null
                && constraints.Contains(reference.Constraint),
            ReviewEvidenceKind.DoctrineClause => !string.IsNullOrWhiteSpace(
                reference.DoctrineClause
            ) && doctrine.Content.Contains(reference.DoctrineClause, StringComparison.Ordinal),
            _ => false,
        };
        if (!valid)
        {
            context.AddFailure(
                new ValidationFailure(
                    "evidence",
                    $"{reference.Kind} evidence does not reproduce a current packet, constraint, doctrine, verification, file-line, or symbol fact."
                )
                {
                    ErrorCode = "review.evidence.invalid",
                }
            );
        }
    }
}
