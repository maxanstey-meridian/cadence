using System.Runtime.CompilerServices;
using Cadence.Git;
using FluentAssertions;
using Microsoft.Extensions.AI;

namespace Cadence.Tests;

public sealed class RoleBudgetTests
{
    [Theory]
    [InlineData("executor", 1111)]
    [InlineData("planner", 2222)]
    [InlineData("reviewer", 3333)]
    public async Task Each_assembled_role_applies_its_output_budget(string role, int expected)
    {
        var repository = TestSupport.CreateGitRepository();
        try
        {
            var terminal = role switch
            {
                "executor" => TestSupport.ToolCall(
                    "ask",
                    "ask_planner",
                    new Dictionary<string, object?>
                    {
                        ["currentSlice"] = "Read the repository",
                        ["question"] = "Authorize?",
                        ["proposedApproach"] = "Use the existing implementation",
                        ["evidence"] = new[] { "README.md: current repository" },
                    }
                ),
                "planner" => TestSupport.Text(
                    """{"decision":"Stop","rationale":"Stop this budget probe","constraints":[],"evidenceUsed":["README.md: current repository"],"safeNextAction":"End probe","correctedApproach":null,"humanQuestion":null,"humanDecisionDomain":null}"""
                ),
                _ => TestSupport.Text(
                    """{"decision":"NeedsHuman","summary":"Product choice required","assessments":[],"findings":[],"humanQuestion":"Which product behavior?","humanDecisionDomain":"Product"}"""
                ),
            };
            using var client = new BudgetClient(
                new ScriptedChatClient(
                    role,
                    TestSupport.ToolCall(
                        "read",
                        "file_access_read",
                        new Dictionary<string, object?> { ["path"] = "README.md" }
                    ),
                    terminal
                )
            );
            var git = new GitProcess();
            var dirty = new DirtyWorkCheckpointPolicy(git, TimeProvider.System);
            var caps = CadenceCapabilities.Create(TimeProvider.System, dirty);
            var participants = new CadenceParticipantsFactory(
                _ => client,
                name =>
                    new(
                        200000,
                        name == "executor" ? 1111
                            : name == "planner" ? 2222
                            : 3333,
                        80
                    ),
                TestSupport.Doctrine(),
                [],
                new(git),
                git,
                dirty,
                caps.AskPlanner,
                caps.UpdateOutcomes,
                caps.SubmitReport,
                caps.WriteCheckpoint,
                caps.ResetContext
            ).Create();
            var agent = role switch
            {
                "executor" => participants.Executor,
                "planner" => participants.Planner,
                _ => participants.Reviewer,
            };
            var result = await new PipelineRunner().RunAsync(
                Pipeline.Start(agent, "budget").Build(agent),
                TestSupport.State(repository),
                cancellationToken: TestContext.Current.CancellationToken
            );
            client
                .Limits.Should()
                .NotBeEmpty()
                .And.AllSatisfy(limit => limit.Should().Be(expected));
        }
        finally
        {
            Directory.Delete(repository, true);
        }
    }

    private sealed class BudgetClient(IChatClient inner) : DelegatingChatClient(inner)
    {
        internal List<int?> Limits { get; } = [];

        public override Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            Limits.Add(options?.MaxOutputTokens);
            return base.GetResponseAsync(messages, options, cancellationToken);
        }

        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            Limits.Add(options?.MaxOutputTokens);
            await foreach (
                var update in base.GetStreamingResponseAsync(messages, options, cancellationToken)
            )
            {
                yield return update;
            }
        }
    }
}
