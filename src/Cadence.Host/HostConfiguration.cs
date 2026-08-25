using System.Text.Json;
using FluentValidation;

namespace Cadence.Host;

internal sealed record HostConfiguration(
    IReadOnlyDictionary<string, ProviderConfiguration> Providers,
    IReadOnlyDictionary<string, ProfileConfiguration> Profiles,
    string ReviewerDoctrineFile,
    IReadOnlyList<string>? SkillDirectories = null,
    int GitTimeoutSeconds = 120,
    IReadOnlyDictionary<string, RepositoryConfiguration>? Repositories = null
)
{
    public static HostConfiguration Load(string path)
    {
        if (!File.Exists(path))
        {
            throw new InvalidOperationException($"Configuration not found: {path}");
        }

        try
        {
            var configuration =
                JsonSerializer.Deserialize<HostConfiguration>(
                    File.ReadAllText(path),
                    JsonSerializerOptions.Web
                ) ?? throw new InvalidOperationException($"Configuration is empty: {path}");
            foreach (var name in new[] { "executor", "planner", "reviewer" })
            {
                if (!configuration.Profiles.ContainsKey(name))
                {
                    throw new InvalidOperationException($"Profile '{name}' is required.");
                }
            }

            if (configuration.GitTimeoutSeconds <= 0)
            {
                throw new InvalidOperationException("gitTimeoutSeconds must be positive.");
            }

            if (string.IsNullOrWhiteSpace(configuration.ReviewerDoctrineFile))
            {
                throw new InvalidOperationException("reviewerDoctrineFile is required.");
            }

            ValidateSkillList(configuration.SkillDirectories, "skillDirectories");

            var identities = new HashSet<string>(RepositoryPathIdentity.Comparer);
            foreach (
                var (key, repository) in configuration.Repositories is null
                    ? Enumerable.Empty<KeyValuePair<string, RepositoryConfiguration>>()
                    : configuration.Repositories
            )
            {
                if (string.IsNullOrWhiteSpace(key) || !Path.IsPathRooted(key))
                {
                    throw new InvalidOperationException(
                        "Repository configuration keys must be nonblank absolute paths."
                    );
                }

                var identity = RepositoryPathIdentity.Normalize(key);
                if (!identities.Add(identity))
                {
                    throw new InvalidOperationException(
                        $"Repository configuration keys must resolve to distinct paths: {identity}"
                    );
                }

                if (repository is null)
                {
                    throw new InvalidOperationException(
                        $"Repository configuration must not be null: {key}"
                    );
                }

                ValidateSkillList(
                    repository.SkillDirectories,
                    $"repositories['{key}'].skillDirectories"
                );
                ValidateRepositoryCommands(repository, key);
            }
            return configuration;
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                $"Configuration is not valid JSON: {path}",
                exception
            );
        }
    }

    private static void ValidateSkillList(IReadOnlyList<string>? directories, string property)
    {
        if (directories?.Any(string.IsNullOrWhiteSpace) == true)
        {
            throw new InvalidOperationException($"{property} must not contain blank paths.");
        }
    }

    private static void ValidateRepositoryCommands(RepositoryConfiguration repository, string key)
    {
        var packet = new Packet(
            "configuration",
            "/",
            "main",
            [new("outcome", "outcome")],
            repository.Verification ?? [],
            [],
            Commands: repository.Commands ?? [],
            Acceptance: [new("acceptance", "outcome", "acceptance")]
        );
        var result = new PacketValidator(false).Validate(packet);
        var failures = result
            .Errors.Where(error =>
                error.PropertyName.StartsWith("Commands", StringComparison.Ordinal)
                || error.PropertyName.StartsWith("Verification", StringComparison.Ordinal)
            )
            .ToArray();
        if (failures.Length != 0)
        {
            throw new ValidationException(
                $"Repository configuration '{key}' has invalid commands: {string.Join("; ", failures.Select(f => $"{f.PropertyName}: {f.ErrorMessage}"))}"
            );
        }
    }

    public string ResolveReviewerDoctrinePath(string configurationPath) =>
        Path.GetFullPath(
            Path.IsPathRooted(ReviewerDoctrineFile)
                ? ReviewerDoctrineFile
                : Path.Combine(
                    Path.GetDirectoryName(Path.GetFullPath(configurationPath))!,
                    ReviewerDoctrineFile
                )
        );

    public RepositoryConfiguration? FindRepository(string repository)
    {
        var identity = RepositoryPathIdentity.Normalize(repository);
        return (
            Repositories is null
                ? Enumerable.Empty<KeyValuePair<string, RepositoryConfiguration>>()
                : Repositories
        )
            .FirstOrDefault(pair => RepositoryPathIdentity.Equals(pair.Key, identity))
            .Value;
    }

    public Packet ApplyRepositoryDefaults(Packet packet)
    {
        var repository = FindRepository(packet.Repository);
        if (repository is null)
        {
            return packet;
        }

        return packet with
        {
            Commands = Merge(repository.Commands ?? [], packet.Commands),
            Verification = Merge(repository.Verification ?? [], packet.Verification),
        };
    }

    private static IReadOnlyList<PacketCommand> Merge(
        IReadOnlyList<PacketCommand> defaults,
        IReadOnlyList<PacketCommand> authored
    )
    {
        var merged = defaults
            .Select(command => new PacketCommand(command.Label.Trim(), command.Command.Trim()))
            .ToList();
        var positions = merged
            .Select((command, index) => (command.Label, index))
            .ToDictionary(item => item.Label, item => item.index, StringComparer.Ordinal);
        foreach (var command in authored)
        {
            if (positions.TryGetValue(command.Label, out var index))
            {
                merged[index] = command;
            }
            else
            {
                positions.Add(command.Label, merged.Count);
                merged.Add(command);
            }
        }
        return merged;
    }

    public IReadOnlyList<string> ResolveSkillDirectories(
        string configurationPath,
        string? repository = null
    )
    {
        var configurationDirectory = Path.GetDirectoryName(Path.GetFullPath(configurationPath))!;
        string Resolve(string directory) =>
            Path.GetFullPath(
                Path.IsPathRooted(directory)
                    ? directory
                    : Path.Combine(configurationDirectory, directory)
            );
        var global = (SkillDirectories ?? []).Select(Resolve).ToArray();
        var local = (repository is null ? [] : FindRepository(repository)?.SkillDirectories ?? [])
            .Select(Resolve)
            .ToArray();
        if (
            global.Distinct(StringComparer.Ordinal).Count() != global.Length
            || local.Distinct(StringComparer.Ordinal).Count() != local.Length
        )
        {
            throw new InvalidOperationException(
                "skillDirectories must resolve to distinct paths within each scope."
            );
        }

        var resolved = global.Concat(local).Distinct(StringComparer.Ordinal).ToArray();
        foreach (var directory in resolved)
        {
            if (!Directory.Exists(directory))
            {
                throw new InvalidOperationException($"Skill directory not found: {directory}");
            }

            if (!File.Exists(Path.Combine(directory, "SKILL.md")))
            {
                throw new InvalidOperationException(
                    $"Skill directory does not contain SKILL.md: {directory}"
                );
            }
        }
        return resolved;
    }
}

internal static class RepositoryPathIdentity
{
    internal static StringComparer Comparer { get; } =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    internal static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    internal static bool Equals(string left, string right) =>
        Comparer.Equals(Normalize(left), Normalize(right));
}

internal sealed record RepositoryConfiguration(
    IReadOnlyList<string>? SkillDirectories = null,
    IReadOnlyList<PacketCommand>? Commands = null,
    IReadOnlyList<PacketCommand>? Verification = null
);

internal sealed record ProviderConfiguration(
    string BaseUrl,
    string? ApiKeyEnvironmentVariable,
    string WireApi = "completions"
);

internal sealed record ProfileConfiguration(
    string Provider,
    string Model,
    int ContextWindowTokens,
    int MaxOutputTokens,
    int CheckpointAtPercent,
    string? ReasoningEffort = null,
    bool DisableCompaction = false
);
