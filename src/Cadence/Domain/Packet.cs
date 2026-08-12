namespace Cadence;

public sealed record PacketOutcome(string Id, string Description);

public sealed record Packet(
    string Title,
    string Repository,
    string Base,
    IReadOnlyList<PacketOutcome> Outcomes,
    IReadOnlyList<string> Verification,
    IReadOnlyList<string> Constraints,
    string ImplementationContext = ""
);
