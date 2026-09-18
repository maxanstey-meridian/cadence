using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Cadence.Host;
using FluentAssertions;
using Microsoft.Extensions.AI;

namespace Cadence.Tests;

public sealed class OllamaReasoningTests
{
    [Fact(Timeout = 30_000)]
    public async Task Ollama_stream_preserves_reasoning_and_uses_provider_default_effort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var cancellation = TestContext.Current.CancellationToken;
        var server = Task.Run(
            async () =>
            {
                using var connection = await listener.AcceptTcpClientAsync(cancellation);
                await using var stream = connection.GetStream();
                using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
                var length = 0;
                while (await reader.ReadLineAsync(cancellation) is { Length: > 0 } header)
                {
                    if (header.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                    {
                        length = int.Parse(header[15..].Trim());
                    }
                }
                var body = new char[length];
                (await reader.ReadBlockAsync(body.AsMemory(), cancellation)).Should().Be(length);
                var response =
                    "HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nConnection: close\r\n\r\n"
                    + "data: {\"id\":\"test\",\"object\":\"chat.completion.chunk\",\"created\":1,\"model\":\"glm-5.3-flash:cloud\",\"choices\":[{\"index\":0,\"delta\":{\"role\":\"assistant\",\"reasoning\":\"Check the product.\"}}]}\n\n"
                    + "data: {\"id\":\"test\",\"object\":\"chat.completion.chunk\",\"created\":1,\"model\":\"glm-5.3-flash:cloud\",\"choices\":[{\"index\":0,\"delta\":{\"content\":\"391\"},\"finish_reason\":\"stop\"}]}\n\n"
                    + "data: [DONE]\n\n";
                await stream.WriteAsync(Encoding.UTF8.GetBytes(response), cancellation);
                return new string(body);
            },
            cancellation
        );
        var config = new HostConfiguration(
            new Dictionary<string, ProviderConfiguration>
            {
                ["ollama"] = new($"http://127.0.0.1:{port}/v1", null, "completions"),
            },
            new Dictionary<string, ProfileConfiguration>
            {
                ["executor"] = new("ollama", "glm-5.3-flash:cloud", 4096, 256, 80),
            },
            "reviewer.md"
        );
        using var client = new ConfiguredChatClients(config).Build("executor");
        var contents = new List<AIContent>();
        await foreach (
            var update in client.GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, "What is 17 times 23?")],
                cancellationToken: cancellation
            )
        )
        {
            contents.AddRange(update.Contents);
        }
        contents
            .OfType<TextReasoningContent>()
            .Select(x => x.Text)
            .Should()
            .Equal("Check the product.");
        string.Concat(contents.OfType<TextContent>().Select(x => x.Text)).Should().Be("391");
        using var request = JsonDocument.Parse(await server);
        request.RootElement.TryGetProperty("reasoning_effort", out _).Should().BeFalse();
        request.RootElement.TryGetProperty("reasoning", out _).Should().BeFalse();
    }
}
