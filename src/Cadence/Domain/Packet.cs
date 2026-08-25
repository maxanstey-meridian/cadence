using System.Text.Json.Serialization;

namespace Cadence;

public sealed record PacketOutcome(string Id, string Description);

public sealed record PacketAcceptanceCriterion(
    string Id,
    [property: JsonPropertyName("outcome")] string OutcomeId,
    string Requirement
);

public sealed record PacketConstraint(string Id, string Requirement);

public sealed record PacketCommand(string Label, string Command);

public sealed record Packet(
    string Title,
    string Repository,
    string Base,
    IReadOnlyList<PacketOutcome> Outcomes,
    IReadOnlyList<PacketCommand>? Verification,
    IReadOnlyList<PacketConstraint> Constraints,
    string ImplementationContext = "",
    IReadOnlyList<PacketCommand>? Commands = null,
    IReadOnlyList<PacketAcceptanceCriterion>? Acceptance = null
)
{
    public IReadOnlyList<PacketCommand> Verification { get; init; } = Verification ?? [];

    public IReadOnlyList<PacketCommand> Commands { get; init; } = Commands ?? [];

    [JsonRequired]
    public IReadOnlyList<PacketAcceptanceCriterion> Acceptance { get; init; } = Acceptance ?? [];
}
