using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace Cadence.Tests;

internal static class TestSupport
{
    private static readonly Lazy<ReviewerDoctrine> DoctrineValue = new(CreateDoctrine);

    internal static ReviewerDoctrine Doctrine() => DoctrineValue.Value;

    internal static ReviewEvidenceReference FileEvidence(string path = "README.md", int line = 1) =>
        new(ReviewEvidenceKind.FileLine, Path: path, Line: line);

    internal static ReviewEvidenceReference DoctrineEvidence() =>
        new(ReviewEvidenceKind.DoctrineClause, DoctrineClause: "Correctness over taste.");

    internal static Packet Packet() =>
        new(
            "Implement feature",
            "/source",
            "main",
            [new PacketOutcome("outcome-1", "Deliver the feature")],
            ["dotnet test"],
            [],
            "Inspect the implementation."
        );

    internal static CadenceState State(string? workspace = null, DateTimeOffset? now = null) =>
        CadenceState.Create(
            Packet(),
            "base-sha",
            workspace ?? "/workspace",
            now is null ? TimeProvider.System : new FakeTimeProvider(now.Value)
        );

    internal static string CreateGitRepository()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cadence-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        Run(path, "init");
        Run(path, "config", "user.name", "Cadence Tests");
        Run(path, "config", "user.email", "cadence-tests@localhost");
        File.WriteAllText(Path.Combine(path, "README.md"), "initial\n");
        Run(path, "add", "README.md");
        Run(path, "commit", "-m", "initial");
        return path;
    }

    private static ReviewerDoctrine CreateDoctrine()
    {
        var path = Path.Combine(Path.GetTempPath(), "cadence-tests-reviewer-doctrine.md");
        File.WriteAllText(
            path,
            "Correctness over taste.\nPreserve behavior and test real integration.\n"
        );
        return ReviewerDoctrine.Load(path);
    }

    internal static string Head(string repository) => Run(repository, "rev-parse", "HEAD").Trim();

    internal static void Git(string repository, params string[] arguments) =>
        Run(repository, arguments);

    internal static ChatResponse ToolCall(
        string callId,
        string toolName,
        IDictionary<string, object?> arguments
    ) =>
        new(
            new ChatMessage(
                ChatRole.Assistant,
                [new FunctionCallContent(callId, toolName, arguments)]
            )
        )
        {
            FinishReason = ChatFinishReason.ToolCalls,
            ModelId = "cadence-test",
        };

    internal static ChatResponse Text(string text) =>
        new(new ChatMessage(ChatRole.Assistant, text))
        {
            FinishReason = ChatFinishReason.Stop,
            ModelId = "cadence-test",
        };

    private static string Run(string workingDirectory, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }
        using var process =
            Process.Start(start) ?? throw new InvalidOperationException("git failed to start");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(stderr);
        }
        return stdout;
    }
}

internal sealed class ScriptedChatClient : IChatClient
{
    private readonly string _name;
    private readonly ConcurrentQueue<Func<IReadOnlyList<ChatMessage>, ChatResponse>> _responses;

    internal ScriptedChatClient(string name, params ChatResponse[] responses)
        : this(
            name,
            responses.Select(response => new Func<IReadOnlyList<ChatMessage>, ChatResponse>(_ =>
                response
            ))
        ) { }

    private ScriptedChatClient(
        string name,
        IEnumerable<Func<IReadOnlyList<ChatMessage>, ChatResponse>> responses
    )
    {
        _name = name;
        _responses = new(responses);
    }

    internal static ScriptedChatClient Dynamic(
        string name,
        params Func<IReadOnlyList<ChatMessage>, ChatResponse>[] responses
    ) => new(name, responses);

    public int CallCount { get; private set; }
    public List<IReadOnlyList<ChatMessage>> Requests { get; } = [];
    public List<IReadOnlyList<string>> AdvertisedTools { get; } = [];
    public Action<int>? BeforeCall { get; init; }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    ) => Task.FromResult(Dequeue(messages, options));

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        foreach (var update in Dequeue(messages, options).ToChatResponseUpdates())
        {
            yield return update;
        }
        await Task.CompletedTask;
    }

    private ChatResponse Dequeue(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        CallCount++;
        BeforeCall?.Invoke(CallCount);
        var request = messages.ToArray();
        Requests.Add(request);
        AdvertisedTools.Add(options?.Tools?.Select(tool => tool.Name).ToArray() ?? []);
        return _responses.TryDequeue(out var response)
            ? response(request)
            : throw new InvalidOperationException(
                $"ScriptedChatClient '{_name}' exhausted at call {CallCount}. Last request: "
                    + string.Join(" | ", Requests[^1].Select(message => message.Text))
            );
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}

internal sealed class NoOpPersistenceObserver : IPipelinePersistenceObserver
{
    public ValueTask ObserveAsync(
        PipelineObservation observation,
        CancellationToken cancellationToken
    ) => ValueTask.CompletedTask;
}

internal sealed class RecordingPersistenceObserver : IPipelinePersistenceObserver
{
    public List<PipelineObservation> Observations { get; } = [];

    public ValueTask ObserveAsync(
        PipelineObservation observation,
        CancellationToken cancellationToken
    )
    {
        Observations.Add(observation);
        return ValueTask.CompletedTask;
    }
}
