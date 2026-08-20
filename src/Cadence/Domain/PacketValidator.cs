using FluentValidation;

namespace Cadence;

public sealed class PacketValidator : AbstractValidator<Packet>
{
    public PacketValidator()
    {
        RuleFor(packet => packet.Title).Must(BeNonBlank).WithMessage("Packet title is required.");
        RuleFor(packet => packet.Repository)
            .Must(BeNonBlank)
            .WithMessage("Packet repository is required.");
        RuleFor(packet => packet.Base).Must(BeNonBlank).WithMessage("Packet base is required.");
        RuleFor(packet => packet.Outcomes)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(outcomes => outcomes.All(outcome => outcome is not null))
            .WithMessage("Packet outcomes must not contain null values.")
            .Must(outcomes =>
                outcomes.Select(outcome => outcome.Id.Trim()).Distinct().Count() == outcomes.Count
            )
            .WithMessage("Packet outcomes must be non-empty with unique IDs.");
        RuleForEach(packet => packet.Outcomes)
            .ChildRules(outcome =>
            {
                outcome
                    .RuleFor(value => value.Id)
                    .Must(BeNonBlank)
                    .WithMessage("Packet outcome id is required.");
                outcome
                    .RuleFor(value => value.Description)
                    .Must(BeNonBlank)
                    .WithMessage("Packet outcome description is required.");
            });
        RuleFor(packet => packet.Acceptance)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Packet must declare at least one acceptance criterion.")
            .Must(criteria => criteria.All(criterion => criterion is not null))
            .WithMessage("Packet acceptance must not contain null values.")
            .Must(criteria =>
                criteria
                    .Where(criterion => !string.IsNullOrWhiteSpace(criterion.Id))
                    .Select(criterion => criterion.Id.Trim())
                    .Distinct()
                    .Count()
                == criteria.Count(criterion => !string.IsNullOrWhiteSpace(criterion.Id))
            )
            .WithMessage("Packet acceptance IDs must be non-empty and unique.");
        RuleForEach(packet => packet.Acceptance)
            .ChildRules(criterion =>
            {
                criterion
                    .RuleFor(value => value.Id)
                    .Must(BeNonBlank)
                    .WithMessage("Packet acceptance id is required.");
                criterion
                    .RuleFor(value => value.OutcomeId)
                    .Must(BeNonBlank)
                    .WithMessage("Packet acceptance outcome is required.");
                criterion
                    .RuleFor(value => value.Requirement)
                    .Must(BeNonBlank)
                    .WithMessage("Packet acceptance requirement is required.");
            });
        RuleFor(packet => packet)
            .Custom(
                (packet, context) =>
                {
                    if (
                        packet.Outcomes is null
                        || packet.Acceptance is null
                        || packet.Outcomes.Any(outcome => outcome is null)
                        || packet.Acceptance.Any(criterion => criterion is null)
                    )
                    {
                        return;
                    }
                    var outcomes = packet
                        .Outcomes.Where(outcome => !string.IsNullOrWhiteSpace(outcome.Id))
                        .Select(outcome => outcome.Id.Trim())
                        .ToHashSet(StringComparer.Ordinal);
                    foreach (var criterion in packet.Acceptance)
                    {
                        if (
                            !string.IsNullOrWhiteSpace(criterion.OutcomeId)
                            && !outcomes.Contains(criterion.OutcomeId.Trim())
                        )
                        {
                            context.AddFailure(
                                "Acceptance",
                                $"Acceptance criterion '{criterion.Id}' references unknown outcome '{criterion.OutcomeId}'."
                            );
                        }
                    }
                    foreach (
                        var outcome in outcomes.Where(id =>
                            !packet.Acceptance.Any(criterion =>
                                !string.IsNullOrWhiteSpace(criterion.OutcomeId)
                                && string.Equals(
                                    criterion.OutcomeId.Trim(),
                                    id,
                                    StringComparison.Ordinal
                                )
                            )
                        )
                    )
                    {
                        context.AddFailure(
                            "Acceptance",
                            $"Outcome '{outcome}' must have at least one acceptance criterion."
                        );
                    }
                }
            );
        RuleFor(packet => packet.Verification)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(commands =>
                commands.All(command =>
                    !string.IsNullOrWhiteSpace(command.Label)
                    && !string.IsNullOrWhiteSpace(command.Command)
                )
            )
            .WithMessage(
                "Packet must declare at least one verification entry with non-blank label and command."
            );
        RuleForEach(packet => packet.Commands)
            .Must(BeNonBlank)
            .WithMessage("Packet commands must not contain null or blank values.");
        RuleForEach(packet => packet.Constraints)
            .Must(BeNonBlank)
            .WithMessage("Packet constraints must not contain null or blank values.");
    }

    private static bool BeNonBlank(string value) => !string.IsNullOrWhiteSpace(value);
}
