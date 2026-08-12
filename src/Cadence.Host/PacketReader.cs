using Tandem.Packets;

namespace Cadence.Host;

internal static class PacketReader
{
    internal static Packet Read(string path)
    {
        PacketFile<Packet> input = PacketFile.Read(path, new PacketValidator());
        var repository = input.Source.ResolvePath(input.Value.Repository.Trim());
        if (!Directory.Exists(repository))
        {
            throw new InvalidOperationException($"Packet repository does not exist: {repository}");
        }

        return input.Value with
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
            Constraints = input.Value.Constraints ?? [],
            ImplementationContext = input.Context,
        };
    }
}
