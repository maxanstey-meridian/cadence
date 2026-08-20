using System.ClientModel;
using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using OpenAI;
using Tandem.OpenAICompatible;

namespace Cadence.Host;

internal sealed class ConfiguredChatClients(HostConfiguration configuration)
{
    private readonly ConcurrentDictionary<string, Lazy<IChatClient>> _clients = new(
        StringComparer.Ordinal
    );

    public IChatClient Build(string profileName) =>
        _clients
            .GetOrAdd(
                profileName,
                name => new Lazy<IChatClient>(
                    () => Create(name),
                    LazyThreadSafetyMode.ExecutionAndPublication
                )
            )
            .Value;

    public CadenceAgentProfile ResolveProfile(string profileName)
    {
        var profile = GetProfile(profileName);
        return new CadenceAgentProfile(
            profile.ContextWindowTokens,
            profile.MaxOutputTokens,
            profile.CheckpointAtPercent,
            profile.DisableCompaction
        );
    }

    private IChatClient Create(string profileName)
    {
        var profile = GetProfile(profileName);
        if (!configuration.Providers.TryGetValue(profile.Provider, out var provider))
        {
            throw new InvalidOperationException(
                $"Profile '{profileName}' references unknown provider '{profile.Provider}'."
            );
        }
        if (!Uri.TryCreate(provider.BaseUrl, UriKind.Absolute, out var endpoint))
        {
            throw new InvalidOperationException(
                $"Provider '{profile.Provider}' has an invalid baseUrl."
            );
        }

        var apiKey = provider.ApiKeyEnvironmentVariable is null
            ? string.Empty
            : Environment.GetEnvironmentVariable(provider.ApiKeyEnvironmentVariable)
                ?? string.Empty;
        if (provider.ApiKeyEnvironmentVariable is not null && apiKey.Length == 0)
        {
            throw new InvalidOperationException(
                $"Environment variable '{provider.ApiKeyEnvironmentVariable}' is required by provider '{profile.Provider}'."
            );
        }

        var client = new OpenAIClient(
            new ApiKeyCredential(apiKey.Length == 0 ? "cadence-local-proxy-placeholder" : apiKey),
            new OpenAIClientOptions
            {
                Endpoint = endpoint,
                NetworkTimeout = TimeSpan.FromSeconds(600),
            }
        );
#pragma warning disable OPENAI001
        IChatClient chatClient = provider.WireApi switch
        {
            "completions" => client.GetChatClient(profile.Model).AsIChatClient(),
            "responses" => client.GetResponsesClient().AsIChatClient(profile.Model),
            _ => throw new InvalidOperationException(
                $"Provider '{profile.Provider}' wireApi must be 'completions' or 'responses'."
            ),
        };
#pragma warning restore OPENAI001
        if (
            provider.WireApi == "completions"
            && (
                endpoint.Host.Equals("openrouter.ai", StringComparison.OrdinalIgnoreCase)
                || endpoint.Host.EndsWith(".openrouter.ai", StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            chatClient = new OpenRouterReasoningChatClient(chatClient);
        }
        if (profile.ReasoningEffort is not null)
        {
            var effort = profile.ReasoningEffort switch
            {
                "low" => ReasoningEffort.Low,
                "medium" => ReasoningEffort.Medium,
                "high" => ReasoningEffort.High,
                _ => throw new InvalidOperationException(
                    $"Profile '{profileName}' reasoningEffort must be 'low', 'medium', or 'high'."
                ),
            };
            chatClient = chatClient
                .AsBuilder()
                .ConfigureOptions(options =>
                    options.Reasoning = new ReasoningOptions
                    {
                        Effort = effort,
                        Output = ReasoningOutput.Summary,
                    }
                )
                .Build();
        }

        return new StreamRetryChatClient(chatClient);
    }

    private ProfileConfiguration GetProfile(string name) =>
        configuration.Profiles.TryGetValue(name, out var profile)
            ? profile
            : throw new InvalidOperationException($"Profile '{name}' is not configured.");
}
