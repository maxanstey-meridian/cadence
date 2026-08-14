using FluentAssertions;
using Tandem.Advanced;

namespace Cadence.Tests;

public sealed class ExecutorPolicyTests
{
    [Fact]
    public async Task Authorized_prose_continuation_allows_every_lifecycle_tool()
    {
        var state = TestSupport.State() with { ApprovedApproachRevision = 0, ApproachRevision = 0 };
        var observation = new AgentTurnObservation<CadenceState>(
            new AgentMessageContext<CadenceState>(Guid.NewGuid(), state, null),
            "I will continue.",
            [],
            false,
            0
        );

        var directive = await ExecutorPolicies
            .CreateTurnPolicy()
            .Continue(observation, TestContext.Current.CancellationToken);

        directive.Should().NotBeNull();
        directive!.RequiredToolName.Should().BeNull();
        directive.Prompt.Should().ContainAll("ask_planner", "write_checkpoint", "submit_report");
        directive.Prompt.Should().Contain("implementation autonomously");
        directive
            .Prompt.Should()
            .Contain("never for ordinary implementation or deterministic gate repair");
    }
}
