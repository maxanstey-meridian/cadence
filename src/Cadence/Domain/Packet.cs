using System.Text.Json.Serialization;

namespace Cadence;

public sealed record PacketOutcome(string Id, string Description);

public sealed record PacketAcceptanceCriterion(
    string Id,
    [property: JsonPropertyName("outcome")] string OutcomeId,
    string Requirement
);

public sealed record Packet(
    string Title,
    string Repository,
    string Base,
    IReadOnlyList<PacketOutcome> Outcomes,
    IReadOnlyList<string> Verification,
    IReadOnlyList<string> Constraints,
    string ImplementationContext = "",
    IReadOnlyList<string>? Commands = null,
    IReadOnlyList<PacketAcceptanceCriterion>? Acceptance = null
)
{
    public IReadOnlyList<string> Commands { get; init; } = Commands ?? [];
    public IReadOnlyList<PacketAcceptanceCriterion> Acceptance { get; init; } = Acceptance ?? [];
}
