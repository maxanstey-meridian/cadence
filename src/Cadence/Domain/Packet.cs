using System.Text.Json.Serialization;

namespace Cadence;

public sealed record PacketOutcome(string Id, string Description);

public sealed record PacketAcceptanceCriterion(
    string Id,
    [property: JsonPropertyName("outcome")] string OutcomeId,
    string Requirement
);

public sealed record PacketConstraint(string Id, string Requirement);

public sealed record PacketCommandEntry(
    string Label,
    string Command,
    IReadOnlyList<string>? Arguments = null
);

public sealed record Packet(
    string Title,
    string Repository,
    string Base,
    IReadOnlyList<PacketOutcome> Outcomes,
    IReadOnlyList<PacketCommandEntry>? Verification,
    IReadOnlyList<PacketConstraint> Constraints,
    string ImplementationContext = "",
    IReadOnlyList<PacketCommandEntry>? Commands = null,
    IReadOnlyList<PacketAcceptanceCriterion>? Acceptance = null
)
{
    public IReadOnlyList<PacketCommandEntry> Verification { get; init; } = Verification ?? [];

    public IReadOnlyList<PacketCommandEntry> Commands { get; init; } = Commands ?? [];

    [JsonRequired]
    public IReadOnlyList<PacketAcceptanceCriterion> Acceptance { get; init; } = Acceptance ?? [];
}
