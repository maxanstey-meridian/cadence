using System.Text.Json;

namespace Cadence.Host;

internal sealed record HostConfiguration(
    IReadOnlyDictionary<string, ProviderConfiguration> Providers,
    IReadOnlyDictionary<string, ProfileConfiguration> Profiles,
    string ReviewerDoctrineFile,
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
    string? ReasoningEffort = null
);
