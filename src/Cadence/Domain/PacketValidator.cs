using FluentValidation;

namespace Cadence;

public sealed class PacketValidator : AbstractValidator<Packet>
{
    public PacketValidator(bool requireVerification = true)
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
        if (requireVerification)
        {
            RuleFor(packet => packet.Verification)
                .NotEmpty()
                .WithMessage("Packet must declare at least one verification entry.");
        }
        RuleFor(packet => packet.Verification)
            .Custom(
                (commands, context) =>
                    ValidateCommands(commands, "Verification", "run_verification_", context)
            );
        RuleFor(packet => packet.Commands)
            .Custom(
                (commands, context) =>
                    ValidateCommands(commands, "Commands", "run_command_", context)
            );
        RuleFor(packet => packet.Constraints)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .WithMessage("Packet constraints must not be null.")
            .Must(constraints => constraints.All(constraint => constraint is not null))
            .WithMessage("Packet constraints must not contain null values.")
            .Must(constraints =>
                constraints
                    .Where(constraint => !string.IsNullOrWhiteSpace(constraint.Id))
                    .Select(constraint => constraint.Id.Trim())
                    .Distinct(StringComparer.Ordinal)
                    .Count()
                == constraints.Count(constraint => !string.IsNullOrWhiteSpace(constraint.Id))
            )
            .WithMessage("Packet constraint IDs must be unique.");
        RuleForEach(packet => packet.Constraints)
            .ChildRules(constraint =>
            {
                constraint
                    .RuleFor(value => value.Id)
                    .Must(BeStableId)
                    .WithMessage(
                        "Packet constraint id must be 1-64 ASCII letters, digits, underscores, or hyphens."
                    );
                constraint
                    .RuleFor(value => value.Requirement)
                    .Must(BeNonBlank)
                    .WithMessage("Packet constraint requirement is required.");
            });
    }

    private static void ValidateCommands(
        IReadOnlyList<PacketCommand>? commands,
        string property,
        string prefix,
        ValidationContext<Packet> context
    )
    {
        if (commands is null)
        {
            context.AddFailure(property, $"Packet {property.ToLowerInvariant()} must not be null.");
            return;
        }

        var labels = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < commands.Count; index++)
        {
            var command = commands[index];
            if (command is null)
            {
                context.AddFailure(
                    $"{property}[{index}]",
                    $"Packet {property.ToLowerInvariant()} must not contain null entries."
                );
                continue;
            }

            var label = command.Label?.Trim();
            if (string.IsNullOrWhiteSpace(label))
            {
                context.AddFailure($"{property}[{index}].Label", "Command label is required.");
            }
            else
            {
                if (!labels.Add(label))
                {
                    context.AddFailure(
                        $"{property}[{index}].Label",
                        "Command labels must be unique."
                    );
                }
                if (
                    !label.All(character =>
                        character
                            is >= 'A'
                                and <= 'Z'
                                or >= 'a'
                                and <= 'z'
                                or >= '0'
                                and <= '9'
                                or '_'
                                or '-'
                    )
                )
                {
                    context.AddFailure(
                        $"{property}[{index}].Label",
                        "Command labels must contain only ASCII letters, digits, underscores, or hyphens."
                    );
                }
                if (prefix.Length + label.Length > 64)
                {
                    context.AddFailure(
                        $"{property}[{index}].Label",
                        "The combined Tandem tool name must be at most 64 characters."
                    );
                }
            }

            if (string.IsNullOrWhiteSpace(command.Command))
            {
                context.AddFailure($"{property}[{index}].Command", "Command is required.");
            }
        }
    }

    private static bool BeNonBlank(string value) => !string.IsNullOrWhiteSpace(value);

    private static bool BeStableId(string value) =>
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
}
