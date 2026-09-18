using System.Text.Json;
using Cadence.Host;
using FluentAssertions;

namespace Cadence.Tests;

public sealed class HarnessRecoveryRegressionTests
{
    [Fact]
    public void Later_approvals_keep_constraints_and_assessed_direction_through_recovery()
    {
        var request = new AskPlannerRequest(
            "specific slice",
            "Authorize?",
            "specific approach",
            ["specific fact"]
        );
        var decision = new PlannerDecision(
            PlannerDecisionValue.Proceed,
            "Grounded approval",
            [new("preserve", "Preserve behavior")],
            ["source fact"],
            "Implement"
        );
        var state = TestSupport
            .State()
            .RecordPlannerRequest(request)
            .RecordPlannerDecision(decision);
        state = state.RecordOutcomeUpdates(
            new([
                new(
                    "outcome-1",
                    OutcomeStatus.InProgress,
                    "Some implementation exists",
                    "Finish it"
                ),
            ])
        );
        state = state.RecordCheckpoint(new("Checkpoint", [], "Continue"), DateTimeOffset.UtcNow);
        state = Program.CreateResumeState(
            JsonSerializer.Deserialize<CadenceState>(JsonSerializer.Serialize(state))!
        );
        state = state.RecordPlannerDecision(decision with { Constraints = [] });
        state.CurrentPlannerRequest.Should().BeEquivalentTo(request);
        state.PlannerConstraints.Should().Equal(decision.Constraints);
        DeliveryObligations
            .From(state)
            .Should()
            .Contain(x => x.Reference == "planner-constraint:preserve");
        ExecutorPrompts
            .BuildMessage(state)
            .Should()
            .Contain(request.CurrentSlice)
            .And.Contain(request.ProposedApproach);
        PlannerPrompts
            .BuildMessage(state)
            .Should()
            .Contain(request.CurrentSlice)
            .And.Contain(request.ProposedApproach);
        state = state.RecordPlannerDecision(
            decision with
            {
                Constraints =
                [
                    new("preserve", "Explicitly revised behavior"),
                    new("second", "Another requirement"),
                ],
            }
        );
        state.PlannerConstraints.Should().HaveCount(2);
        state
            .PlannerConstraints.Single(x => x.Id == "preserve")
            .Requirement.Should()
            .Be("Explicitly revised behavior");
    }
}
