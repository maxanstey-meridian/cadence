using FluentValidation;

namespace Cadence;

public sealed class PlannerDecisionValidator : AbstractValidator<PlannerDecision>
{
    private static readonly HashSet<string> _placeholders = new(
        ["todo", "lgtm", "done", "looks good", "n/a"],
        StringComparer.OrdinalIgnoreCase
    );

    public PlannerDecisionValidator()
    {
        RuleFor(decision => decision.Decision).IsInEnum().WithErrorCode("planner.decision.invalid");
        RuleFor(decision => decision.Rationale)
            .Must(BeMeaningful)
            .WithErrorCode("planner.rationale.meaningful");
        RuleFor(decision => decision.EvidenceUsed)
            .NotEmpty()
            .WithErrorCode("planner.evidence.required");
        RuleForEach(decision => decision.EvidenceUsed)
            .Must(BeMeaningful)
            .WithErrorCode("planner.evidence.meaningful");
        RuleFor(decision => decision.Constraints)
            .NotNull()
            .WithErrorCode("planner.constraints.required");
        RuleFor(decision => decision.Constraints)
            .Must(constraints =>
                constraints is not null
                && constraints.All(constraint => constraint is not null)
                && constraints
                    .Select(constraint => constraint.Id)
                    .Distinct(StringComparer.Ordinal)
                    .Count() == constraints.Count
            )
            .WithErrorCode("planner.constraints.unique");
        RuleForEach(decision => decision.Constraints)
            .ChildRules(constraint =>
            {
                constraint
                    .RuleFor(value => value.Id)
                    .Must(BeStableId)
                    .WithErrorCode("planner.constraint.id");
                constraint
                    .RuleFor(value => value.Requirement)
                    .Must(BeMeaningful)
                    .WithErrorCode("planner.constraint.requirement");
            });
        RuleFor(decision => decision.Constraints)
            .Empty()
            .WithErrorCode("planner.constraints.forbidden_for_non_authorizing_decision")
            .When(decision =>
                decision.Decision
                    is PlannerDecisionValue.ReviseApproach
                        or PlannerDecisionValue.NeedsHuman
                        or PlannerDecisionValue.Stop
            );
        RuleFor(decision => decision.SafeNextAction)
            .Must(BeMeaningful)
            .WithErrorCode("planner.safe_next_action.required");
        RuleFor(decision => decision.CorrectedApproach)
            .Must(BeMeaningful)
            .WithErrorCode("planner.corrected_approach.required")
            .When(decision => decision.Decision == PlannerDecisionValue.ReviseApproach);
        RuleFor(decision => decision.CorrectedApproach)
            .Null()
            .WithErrorCode("planner.corrected_approach.forbidden")
            .When(decision => decision.Decision != PlannerDecisionValue.ReviseApproach);
        RuleFor(decision => decision.HumanQuestion)
            .NotEmpty()
            .WithErrorCode("planner.human_question.required")
            .Must(question => !string.Equals(question, "N/A", StringComparison.OrdinalIgnoreCase))
            .WithErrorCode("planner.human_question.meaningful")
            .When(decision => decision.Decision == PlannerDecisionValue.NeedsHuman);
        RuleFor(decision => decision.HumanQuestion)
            .Null()
            .WithErrorCode("planner.human_question.forbidden")
            .When(decision => decision.Decision != PlannerDecisionValue.NeedsHuman);
        RuleFor(decision => decision.HumanDecisionDomain)
            .NotNull()
            .WithErrorCode("planner.human_decision_domain.required")
            .IsInEnum()
            .WithErrorCode("planner.human_decision_domain.invalid")
            .When(decision => decision.Decision == PlannerDecisionValue.NeedsHuman);
        RuleFor(decision => decision.HumanDecisionDomain)
            .Null()
            .WithErrorCode("planner.human_decision_domain.forbidden")
            .When(decision => decision.Decision != PlannerDecisionValue.NeedsHuman);
    }

    private static bool BeStableId(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && value.Trim().Length <= 64
        && value
            .Trim()
            .All(character =>
                character
                    is >= 'A'
                        and <= 'Z'
                        or >= 'a'
                        and <= 'z'
                        or >= '0'
                        and <= '9'
                        or '_'
                        or '-'
            );

    internal static bool BeMeaningful(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !_placeholders.Contains(value.Trim());
}

public sealed class PlannerDecisionOutput : IAgentOutputDefinition<CadenceState, PlannerDecision>
{
    public string Instructions =>
        """
            Assess the bounded approach against the applicable packet requirements and active constraints.
            Explain in rationale why the approach satisfies or fails those requirements, including material
            gaps and unsupported premises. evidenceUsed must identify the source and decisive fact, and
            support the assessment rather than merely catalogue inspected files. Reassess prior approvals
            where their premises no longer hold. SafeNextAction is one immediate action consistent with
            the decision, not a scope definition or implementation sequence. Authorization is not final
            candidate acceptance.
            """;

    public IValidator<PlannerDecision> Validator { get; } = new PlannerDecisionValidator();
}
