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
        RuleFor(packet => packet.Verification)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .Must(commands => commands.All(command => !string.IsNullOrWhiteSpace(command)))
            .WithMessage("Packet must declare at least one non-blank verification command.");
        RuleForEach(packet => packet.Constraints)
            .Must(BeNonBlank)
            .WithMessage("Packet constraints must not contain null or blank values.");
    }

    private static bool BeNonBlank(string value) => !string.IsNullOrWhiteSpace(value);
}
