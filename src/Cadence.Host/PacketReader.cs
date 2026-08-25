using Tandem.Packets;

namespace Cadence.Host;

internal static class PacketReader
{
    internal static Packet Read(string path, HostConfiguration? configuration = null)
    {
        PacketFile<Packet> input = PacketFile.Read(
            path,
            new PacketValidator(requireVerification: false)
        );
        var repository = input.Source.ResolvePath(input.Value.Repository.Trim());
        if (!Directory.Exists(repository))
        {
            throw new InvalidOperationException($"Packet repository does not exist: {repository}");
        }

        var packet = input.Value with
        {
            Title = input.Value.Title.Trim(),
            Repository = repository,
            Base = input.Value.Base.Trim(),
            Outcomes = input
                .Value.Outcomes.Select(outcome => new PacketOutcome(
                    outcome.Id.Trim(),
                    outcome.Description.Trim()
                ))
                .ToArray(),
            Acceptance = input
                .Value.Acceptance.Select(criterion => new PacketAcceptanceCriterion(
                    criterion.Id.Trim(),
                    criterion.OutcomeId.Trim(),
                    criterion.Requirement.Trim()
                ))
                .ToArray(),
            Verification = input
                .Value.Verification.Select(entry => new PacketCommand(
                    entry.Label.Trim(),
                    entry.Command.Trim()
                ))
                .ToArray(),
            Commands = input
                .Value.Commands.Select(entry => new PacketCommand(
                    entry.Label.Trim(),
                    entry.Command.Trim()
                ))
                .ToArray(),
            Constraints = input
                .Value.Constraints.Select(constraint => new PacketConstraint(
                    constraint.Id.Trim(),
                    constraint.Requirement.Trim()
                ))
                .ToArray(),
            ImplementationContext = input.Context,
        };
        packet = configuration?.ApplyRepositoryDefaults(packet) ?? packet;
        var validation = new PacketValidator().Validate(packet);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(
                string.Join(
                    "; ",
                    validation.Errors.Select(error => $"{error.PropertyName}: {error.ErrorMessage}")
                )
            );
        }
        return packet;
    }
}
