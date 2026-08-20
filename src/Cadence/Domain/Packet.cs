using System.Text.Json.Serialization;

namespace Cadence;

public sealed record PacketOutcome(string Id, string Description);

public sealed record PacketAcceptanceCriterion(
    string Id,
    [property: JsonPropertyName("outcome")] string OutcomeId,
    string Requirement
);

public sealed record VerificationCommand(string Label, string Command);

public sealed record Packet(
    string Title,
    string Repository,
    string Base,
    IReadOnlyList<PacketOutcome> Outcomes,
    IReadOnlyList<VerificationCommand> Verification,
    IReadOnlyList<string> Constraints,
    string ImplementationContext = "",
    IReadOnlyList<string>? Commands = null,
    IReadOnlyList<PacketAcceptanceCriterion>? Acceptance = null
)
{
    public IReadOnlyList<string> Commands { get; init; } = Commands ?? [];

    [JsonRequired]
    public IReadOnlyList<PacketAcceptanceCriterion> Acceptance { get; init; } = Acceptance ?? [];
}
