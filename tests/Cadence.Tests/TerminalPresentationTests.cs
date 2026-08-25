using System.Text.Json;
using System.Threading.Channels;
using FluentAssertions;
using Spectre.Console.Testing;
using Tandem.Terminal;

namespace Cadence.Tests;

public sealed class TerminalPresentationTests
{
    private static readonly Guid _runId = Guid.CreateVersion7();
    private static readonly string _workingDirectory = Path.Combine(
        Path.GetTempPath(),
        "cadence-terminal"
    );

    [Fact]
    public void Compact_tool_allow_list_contains_exactly_the_selected_ordinal_names()
    {
        Cadence
            .Host.Program.TruncatedToolNames.Should()
            .BeEquivalentTo(
                "ask_planner",
                "update_outcomes",
                "submit_report",
                "write_checkpoint",
                "reset_context",
                "file_access_write",
                "file_access_replace"
            );
        Cadence.Host.Program.TruncatedToolNames.Should().NotContain("ASK_PLANNER");
    }

    [Fact(Timeout = 5_000)]
    public async Task Selected_tools_have_compact_starts_and_other_tool_categories_keep_arguments()
    {
        var console = new TestConsole().Width(300);
        await using var display = CreateDisplay(console, new(false, false));
        await display.StartAsync(TestContext.Current.CancellationToken);
        using var arguments = JsonDocument.Parse(
            "{\"fileName\":\"large.txt\",\"content\":\"complete invocation\"}"
        );

        foreach (var name in Cadence.Host.Program.TruncatedToolNames)
        {
            await StartTool(display, name, arguments.RootElement);
        }

        string[] excluded =
        [
            "file_access_read",
            "file_access_delete",
            "read_ledger",
            "gitnexus_context",
            "git_status",
            "run_shell",
            "packet_command",
            "verification_command",
        ];
        foreach (var name in excluded)
        {
            await StartTool(display, name, arguments.RootElement);
        }

        await display.SucceededAsync("complete");
        await display.WaitForCleanupAsync(TestContext.Current.CancellationToken);

        foreach (var name in Cadence.Host.Program.TruncatedToolNames)
        {
            console.Output.Should().Contain($"tool {name} in {_workingDirectory} started");
        }
        foreach (var name in excluded)
        {
            console
                .Output.Should()
                .Contain(
                    $"tool {name} content=\"complete invocation\" fileName=\"large.txt\" in {_workingDirectory} started"
                );
        }
    }

    [Theory(Timeout = 5_000)]
    [InlineData("submit_report")]
    [InlineData("ask_planner")]
    public async Task Rejected_capabilities_keep_compact_start_failure_and_structured_error(
        string name
    )
    {
        var console = new TestConsole().Width(300).Height(30);
        var input = new ControlledKeyInput();
        await using var display = CreateDisplay(console, new(true, true), input);
        await display.StartAsync(TestContext.Current.CancellationToken);
        await input.WaitUntilReadAsync(TestContext.Current.CancellationToken);
        using var arguments = JsonDocument.Parse("{\"summary\":\"invalid request\"}");
        const string error =
            "{\"isError\":true,\"error\":\"validation failed\",\"problems\":[\"required\"]}";

        await StartTool(display, name, arguments.RootElement, "failed-call");
        await display.Observer.ObserveAsync(
            new PipelineAgentUpdated(
                _runId,
                "executor",
                new AgentUpdate.ToolCompleted("failed-call", null, error)
            ),
            TestContext.Current.CancellationToken
        );
        await display.SucceededAsync("complete");
        input.Quit();
        await display.WaitForCleanupAsync(TestContext.Current.CancellationToken);

        console
            .Output.Should()
            .Contain($"{name} in {_workingDirectory}")
            .And.Contain("✗")
            .And.Contain("\"isError\": true")
            .And.Contain("\"error\": \"validation failed\"")
            .And.Contain("\"problems\":")
            .And.Contain("\"required\"")
            .And.NotContain("summary=\"invalid request\"");
    }

    [Theory(Timeout = 5_000)]
    [InlineData("ask_planner", "Authoritative planner question", "Planner asked:")]
    [InlineData("update_outcomes", "Authoritative outcome evidence", "Outcome progress updated.")]
    [InlineData("submit_report", "Authoritative report detail", "Report submitted:")]
    [InlineData("write_checkpoint", "Authoritative checkpoint detail", "Checkpoint written:")]
    [InlineData("reset_context", "Authoritative reset reason", "Context reset requested:")]
    public async Task Empty_summary_renders_each_semantic_payload_once_with_a_safe_tool_label(
        string toolName,
        string detail,
        string duplicateSummary
    )
    {
        var console = new TestConsole().Width(220).Height(30);
        var input = new ControlledKeyInput();
        await using var display = CreateDisplay(console, new(true, true), input);
        await display.StartAsync(TestContext.Current.CancellationToken);
        await input.WaitUntilReadAsync(TestContext.Current.CancellationToken);
        using var arguments = JsonDocument.Parse(
            JsonSerializer.Serialize(new { detail, evidence = new[] { "complete evidence" } })
        );

        await StartTool(display, toolName, arguments.RootElement, "accepted-call");
        await display.Observer.ObserveAsync(
            new PipelineCapabilityAccepted(
                _runId,
                "executor",
                "invocation",
                "capability:internal-kind",
                toolName,
                "accepted-call",
                string.Empty,
                "Cadence.InternalCapabilityRequest",
                arguments.RootElement
            ),
            TestContext.Current.CancellationToken
        );
        await display.SucceededAsync("complete");
        input.Quit();
        await display.WaitForCleanupAsync(TestContext.Current.CancellationToken);

        console.Output.Should().Contain(toolName);
        console.Output.Should().Contain("complete evidence");
        console.Output.Should().NotContain(duplicateSummary);
        console.Output.Should().NotContain("capability:internal-kind");
        console.Output.Should().NotContain("Cadence.InternalCapabilityRequest");
        Count(console.Output, detail).Should().Be(1);
    }

    private static TerminalPipelineDisplay CreateDisplay(
        TestConsole console,
        TerminalCapabilities capabilities,
        ITerminalKeyInput? input = null
    ) =>
        new(
            new PipelineInspection(
                "cadence",
                null,
                "executor",
                ["executor"],
                [],
                [],
                ["executor"],
                [],
                "",
                ""
            ),
            _runId,
            new TerminalDisplayOptions
            {
                Console = console,
                Capabilities = capabilities,
                KeyInput = input,
                RefreshInterval = TimeSpan.FromMilliseconds(1),
                TruncatedToolNames = Cadence.Host.Program.TruncatedToolNames,
            }
        );

    private static ValueTask StartTool(
        TerminalPipelineDisplay display,
        string name,
        JsonElement arguments,
        string? callId = null
    ) =>
        display.Observer.ObserveAsync(
            new PipelineAgentUpdated(
                _runId,
                "executor",
                new AgentUpdate.ToolStarted(callId ?? $"{name}-call", name, arguments)
                {
                    WorkingDirectory = _workingDirectory,
                }
            ),
            TestContext.Current.CancellationToken
        );

    private static int Count(string value, string fragment) =>
        value.Split(fragment, StringSplitOptions.None).Length - 1;

    private sealed class ControlledKeyInput : ITerminalKeyInput
    {
        private readonly Channel<ConsoleKeyInfo?> _keys =
            Channel.CreateUnbounded<ConsoleKeyInfo?>();
        private readonly TaskCompletionSource _reading = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );

        public ValueTask<ConsoleKeyInfo?> ReadAsync(CancellationToken cancellationToken)
        {
            _reading.TrySetResult();
            return _keys.Reader.ReadAsync(cancellationToken);
        }

        public Task WaitUntilReadAsync(CancellationToken cancellationToken) =>
            _reading.Task.WaitAsync(cancellationToken);

        public void Quit()
        {
            _keys.Writer.TryWrite(new('q', ConsoleKey.Q, false, false, false));
            _keys.Writer.TryWrite(null);
        }
    }
}
