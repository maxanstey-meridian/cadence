using System.Text.Json;

namespace Cadence.Host;

internal sealed record HostConfiguration(
    IReadOnlyDictionary<string, ProviderConfiguration> Providers,
    IReadOnlyDictionary<string, ProfileConfiguration> Profiles,
    string ReviewerDoctrineFile,
    IReadOnlyList<string>? SkillDirectories = null,
    int GitTimeoutSeconds = 120
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
            if (configuration.SkillDirectories?.Any(string.IsNullOrWhiteSpace) == true)
            {
                throw new InvalidOperationException(
                    "skillDirectories must not contain blank paths."
                );
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

    public string ResolveReviewerDoctrinePath(string configurationPath) =>
        Path.GetFullPath(
            Path.IsPathRooted(ReviewerDoctrineFile)
                ? ReviewerDoctrineFile
                : Path.Combine(
                    Path.GetDirectoryName(Path.GetFullPath(configurationPath))!,
                    ReviewerDoctrineFile
                )
        );

    public IReadOnlyList<string> ResolveSkillDirectories(string configurationPath)
    {
        var configurationDirectory = Path.GetDirectoryName(Path.GetFullPath(configurationPath))!;
        var resolved = (SkillDirectories ?? [])
            .Select(directory =>
                Path.GetFullPath(
                    Path.IsPathRooted(directory)
                        ? directory
                        : Path.Combine(configurationDirectory, directory)
                )
            )
            .ToArray();
        if (resolved.Distinct(StringComparer.Ordinal).Count() != resolved.Length)
        {
            throw new InvalidOperationException("skillDirectories must resolve to distinct paths.");
        }
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
