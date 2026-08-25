using FluentValidation;

namespace Cadence;

public sealed class ReviewDecisionValidator : AbstractValidator<ReviewDecision>
{
    public ReviewDecisionValidator()
    {
        RuleFor(x => x.Decision).IsInEnum();
        RuleFor(x => x.Summary).Must(PlannerDecisionValidator.BeMeaningful);
        RuleFor(x => x.Assessments).NotNull();
        RuleForEach(x => x.Assessments).SetValidator(new ReviewAssessmentValidator());
        RuleFor(x => x.Findings).NotNull();
        RuleForEach(x => x.Findings).SetValidator(new ReviewFindingValidator());
        RuleFor(x => x.Findings)
            .Must(findings =>
                findings is not null
                && findings.Any(f =>
                    f.Severity is ReviewFindingSeverity.Critical or ReviewFindingSeverity.High
                )
            )
            .When(x => x.Decision == ReviewDecisionValue.RequestChanges);
        RuleFor(x => x.Findings)
            .Must(findings =>
                findings is not null
                && findings.All(f =>
                    f.Severity is ReviewFindingSeverity.Medium or ReviewFindingSeverity.Low
                )
            )
            .When(x => x.Decision == ReviewDecisionValue.Accept);
        RuleFor(x => x.HumanQuestion)
            .NotEmpty()
            .When(x => x.Decision == ReviewDecisionValue.NeedsHuman);
        RuleFor(x => x.HumanQuestion)
            .Null()
            .When(x => x.Decision != ReviewDecisionValue.NeedsHuman);
        RuleFor(x => x.HumanDecisionDomain)
            .NotNull()
            .IsInEnum()
            .When(x => x.Decision == ReviewDecisionValue.NeedsHuman);
        RuleFor(x => x.HumanDecisionDomain)
            .Null()
            .When(x => x.Decision != ReviewDecisionValue.NeedsHuman);
    }
}

public sealed class ReviewDecisionOutput : IAgentOutputDefinition<CadenceState, ReviewDecision>
{
    public string Instructions =>
        "Return the established truth of the exact candidate against the complete delivery contract. Each assessment states whether its obligation is satisfied and cites candidate evidence sufficient for that obligation; each finding identifies a concrete blocking defect and location.";
    public IValidator<ReviewDecision> Validator { get; } = new ReviewDecisionValidator();

    public IReadOnlyList<AgentOutputExample<ReviewDecision>> Examples(CadenceState state) =>
        [
            new(
                "A removal is incomplete even though related work is present",
                new ReviewDecision(
                    ReviewDecisionValue.RequestChanges,
                    "The replacement path is delivered, but the candidate still exposes a legacy registration prohibited by the delivery contract.",
                    [
                        new(
                            "outcome:replace-legacy-capability",
                            false,
                            "src/CurrentCapability.cs:18 provides the replacement, but src/LegacyModule.cs:42 still registers the superseded capability."
                        ),
                        new(
                            "acceptance:legacy-registration-absent",
                            false,
                            "src/LegacyModule.cs:42 still registers LegacyCapability, so the required absence is not established."
                        ),
                        new(
                            "packet-constraint:preserve-current-contract",
                            true,
                            "src/CurrentCapability.cs:18 retains the required current contract."
                        ),
                    ],
                    [
                        new(
                            ReviewFindingSeverity.High,
                            "The candidate retains LegacyCapability registration even though the packet requires the legacy capability to be absent.",
                            "src/LegacyModule.cs:42"
                        ),
                    ]
                )
            ),
        ];
}

public sealed class ReviewFindingValidator : AbstractValidator<ReviewFinding>
{
    public ReviewFindingValidator()
    {
        RuleFor(x => x.Severity).IsInEnum();
        RuleFor(x => x.Description).Must(PlannerDecisionValidator.BeMeaningful);
        RuleFor(x => x.Location).Must(PlannerDecisionValidator.BeMeaningful);
    }
}

public sealed class ReviewAssessmentValidator : AbstractValidator<ReviewAssessment>
{
    public ReviewAssessmentValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Evidence).NotEmpty();
    }
}
