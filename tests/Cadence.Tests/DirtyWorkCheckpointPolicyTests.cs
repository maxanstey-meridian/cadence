using System.Text.Json;
using Cadence.Git;
using FluentAssertions;
using Tandem.Advanced;

namespace Cadence.Tests;

public sealed class DirtyWorkCheckpointPolicyTests
{
    [Fact]
    public async Task Dirty_workspace_after_five_minutes_blocks_the_next_mutation()
    {
        var started = DateTimeOffset.Parse("2026-08-11T10:00:00Z");
        var repository = TestSupport.CreateGitRepository();
        try
        {
            File.AppendAllText(Path.Combine(repository, "README.md"), "changed\n");
            var time = new FakeTimeProvider(started.AddMinutes(5));
            var policy = new DirtyWorkCheckpointPolicy(new GitProcess(), time);
            var state = TestSupport.State(repository, started);

            var result = await policy.InterceptAsync(
                new AgentMessageContext<CadenceState>(Guid.NewGuid(), state, null),
                new ToolInvocation(
                    "write",
                    ToolEffect.WorkspaceMutation,
                    JsonSerializer.SerializeToElement(new { })
                ),
                CancellationToken.None
            );

            result.Should().BeOfType<ToolInterceptionResult.Blocked>();
            ((ToolInterceptionResult.Blocked)result!).Message.Should().Contain("write_checkpoint");
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Fact]
    public async Task Clean_workspace_does_not_require_a_continuity_checkpoint()
    {
        var started = DateTimeOffset.Parse("2026-08-11T10:00:00Z");
        var repository = TestSupport.CreateGitRepository();
        try
        {
            var policy = new DirtyWorkCheckpointPolicy(
                new GitProcess(),
                new FakeTimeProvider(started.AddHours(1))
            );

            var result = await policy.InterceptAsync(
                new AgentMessageContext<CadenceState>(
                    Guid.NewGuid(),
                    TestSupport.State(repository, started),
                    null
                ),
                new ToolInvocation(
                    "write",
                    ToolEffect.WorkspaceMutation,
                    JsonSerializer.SerializeToElement(new { })
                ),
                CancellationToken.None
            );

            result.Should().BeNull();
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }
}
