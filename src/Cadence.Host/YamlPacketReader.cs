using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Cadence.Host;

internal sealed class YamlPacketReader
{
    private readonly IDeserializer _yaml = new DeserializerBuilder()
        .WithNamingConvention(UnderscoredNamingConvention.Instance)
        .Build();

    public Packet Read(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            throw new InvalidOperationException($"Packet not found: {fullPath}");
        }

        var raw = File.ReadAllText(fullPath).ReplaceLineEndings("\n");
        if (!raw.StartsWith("---\n", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Packet must start with YAML frontmatter '---'.");
        }
        var closing = raw.IndexOf("\n---", 4, StringComparison.Ordinal);
        if (closing < 0 || raw.Length > closing + 4 && raw[closing + 4] != '\n')
        {
            throw new InvalidOperationException(
                "Packet YAML frontmatter is not closed with '---'."
            );
        }

        var document =
            _yaml.Deserialize<PacketYaml>(raw[4..closing])
            ?? throw new InvalidOperationException("Packet YAML frontmatter is empty.");
        var repository = Required(document.Repository, "repository");
        if (!Path.IsPathRooted(repository))
        {
            repository = Path.GetFullPath(
                Path.Combine(Path.GetDirectoryName(fullPath)!, repository)
            );
        }
        if (!Directory.Exists(repository))
        {
            throw new InvalidOperationException($"Packet repository does not exist: {repository}");
        }

        var outcomes = (document.Outcomes ?? [])
            .Select(outcome => new PacketOutcome(
                Required(outcome.Id, "outcome id"),
                Required(outcome.Description, "outcome description")
            ))
            .ToArray();
        if (
            outcomes.Length == 0
            || outcomes.Select(x => x.Id).Distinct().Count() != outcomes.Length
        )
        {
            throw new InvalidOperationException(
                "Packet outcomes must be non-empty with unique IDs."
            );
        }
        if (
            document.Verification is not { Count: > 0 }
            || document.Verification.Any(string.IsNullOrWhiteSpace)
        )
        {
            throw new InvalidOperationException(
                "Packet must declare at least one non-blank verification command."
            );
        }

        return new Packet(
            Required(document.Title, "title"),
            repository,
            Required(document.Base, "base"),
            outcomes,
            document.Verification,
            document.Constraints ?? [],
            raw[Math.Min(closing + 5, raw.Length)..].Trim()
        );
    }

    private static string Required(string? value, string field) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException($"Packet {field} is required.")
            : value.Trim();

    public sealed class PacketYaml
    {
        public string? Title { get; init; }
        public string? Repository { get; init; }
        public string? Base { get; init; }
        public List<OutcomeYaml>? Outcomes { get; init; }
        public List<string>? Verification { get; init; }
        public List<string>? Constraints { get; init; }
    }

    public sealed class OutcomeYaml
    {
        public string? Id { get; init; }
        public string? Description { get; init; }
    }
}
