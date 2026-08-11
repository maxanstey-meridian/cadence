using FluentAssertions;

namespace Cadence.Tests;

public sealed class HumanInteractionTests
{
    [Fact]
    public void Revise_approach_is_not_presented_as_a_human_question()
    {
        var state = TestSupport
            .State()
            .RecordPlannerDecision(
                new PlannerDecision(
                    PlannerDecisionValue.ReviseApproach,
                    "The proposed owner is wrong.",
                    [],
                    ["src/owner.cs"],
                    "Submit the corrected approach for approval.",
                    "Move the behavior to the existing owner."
                )
            );

        var act = () => HumanInteraction.BuildPlannerQuestion(state);

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*No pending planner question*");
    }

    [Fact]
    public void Reviewer_human_answer_must_match_the_pending_request_kind()
    {
        var state = RepairCapState();

        var act = () =>
            HumanInteraction.ApplyReviewerAnswer(
                state,
                new ReviewerHumanAnswer.HumanDecision("Continue")
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*does not match*");
    }

    [Fact]
    public async Task Typed_repair_cap_interaction_suspends_and_resumes_with_a_typed_resolution()
    {
        var interaction = PipelineNodes.WaitFor<
            CadenceState,
            ReviewerHumanRequest,
            ReviewerHumanAnswer
        >(
            "repair-cap-human",
            HumanInteraction.BuildReviewerQuestion,
            HumanInteraction.ApplyReviewerAnswer
        );
        var complete = PipelineNodes.Complete(new HumanInteractionComplete());
        var pipeline = Pipeline
            .Start(interaction, "typed-human-resume")
            .Route(interaction, complete, "answered")
            .Build(complete);
        var state = RepairCapState();
        PipelineInteractionContext<ReviewerHumanRequest, ReviewerHumanAnswer>? observed = null;
        var handlers = new PipelineInteractionHandlers().Handle(
            interaction,
            (context, _) =>
            {
                observed = context;
                return ValueTask.FromResult<ReviewerHumanAnswer>(
                    new ReviewerHumanAnswer.ContinueRepairs()
                );
            }
        );

        var result = await new PipelineRunner().RunAsync(
            pipeline,
            state,
            new PipelineRunOptions(Interactions: handlers),
            TestContext.Current.CancellationToken
        );

        result.Succeeded.Should().BeTrue();
        observed!.Request.Should().BeOfType<ReviewerHumanRequest.RepairCap>();
        observed.RequestId.Should().NotBeNullOrWhiteSpace();
        result.State.ReviewerHumanResolution.Should().Be(ReviewerHumanResolution.ContinueRepairs);
        result.State.ReviewAttempt.Should().Be(0);
    }

    [Fact]
    public void Human_answer_at_repair_cap_reopens_a_bounded_repair_window()
    {
        var state = RepairCapState();

        state = HumanInteraction.ApplyReviewerAnswer(
            state,
            new ReviewerHumanAnswer.ContinueRepairs()
        );

        state.ReviewAttempt.Should().Be(0);
        state.ReviewerHumanAnswer.Should().BeOfType<ReviewerHumanAnswer.ContinueRepairs>();
        state.ReviewerHumanResolution.Should().Be(ReviewerHumanResolution.ContinueRepairs);
    }

    private static CadenceState RepairCapState() =>
        TestSupport.State() with
        {
            ReviewAttempt = 3,
            MaximumReviewAttempts = 3,
            ReviewerDecision = new ReviewDecision(
                ReviewDecisionValue.RequestChanges,
                TestSupport.Doctrine().Sha256,
                "A repair remains.",
                [
                    new ReviewOutcomeAssessment(
                        "outcome-1",
                        false,
                        [TestSupport.FileEvidence("src/a.cs")]
                    ),
                ],
                [
                    new ReviewFinding(
                        ReviewFindingSeverity.High,
                        "Fix the implementation.",
                        [TestSupport.DoctrineEvidence(), TestSupport.FileEvidence("src/a.cs")]
                    ),
                ],
                []
            ),
        };

    private sealed class HumanInteractionComplete : IPipelineCompletion<CadenceState>
    {
        public string Id => "human-interaction-complete";

        public string Summarize(CadenceState state) => "Typed Human interaction resumed.";
    }
}
