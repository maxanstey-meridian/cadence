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
            .Empty()
            .WithErrorCode("planner.constraints.forbidden_for_proceed")
            .When(decision => decision.Decision == PlannerDecisionValue.Proceed);
        RuleFor(decision => decision.Constraints)
            .NotEmpty()
            .WithErrorCode("planner.constraints.required")
            .When(decision => decision.Decision == PlannerDecisionValue.ProceedWithConstraints);
        RuleForEach(decision => decision.Constraints)
            .Must(BeMeaningful)
            .WithErrorCode("planner.constraints.meaningful");
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
            .When(decision =>
                decision.Decision
                    is PlannerDecisionValue.ReviseApproach
                        or PlannerDecisionValue.Reorient
            );
        RuleFor(decision => decision.CorrectedApproach)
            .Null()
            .WithErrorCode("planner.corrected_approach.forbidden")
            .When(decision =>
                decision.Decision
                    is not (PlannerDecisionValue.ReviseApproach or PlannerDecisionValue.Reorient)
            );
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

    internal static bool BeMeaningful(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !_placeholders.Contains(value.Trim());
}

public sealed class PlannerDecisionOutput : IAgentOutputDefinition<CadenceState, PlannerDecision>
{
    public string Instructions =>
        "Return a validated planning decision grounded in repository evidence with one bounded SafeNextAction for the next Executor session.";

    public IValidator<PlannerDecision> Validator { get; } = new PlannerDecisionValidator();

    public IReadOnlyList<AgentOutputExample<PlannerDecision>> Examples(CadenceState state) =>
        [
            new(
                state.Packet.Title,
                new PlannerDecision(
                    PlannerDecisionValue.Proceed,
                    "The packet is actionable and repository evidence supports direct implementation.",
                    [],
                    ["README.md"],
                    "Implement the approved approach through the inspected seam.",
                    null,
                    null,
                    null
                )
            ),
        ];
}
