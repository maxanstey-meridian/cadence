using FluentAssertions;

namespace Cadence.Tests;

public sealed class PlannerSliceTests
{
    [Fact]
    public async Task Failed_instruction_context_is_required_exactly_for_failed_instruction_questions()
    {
        var context = new FailedPlannerInstructionContext(
            "Use the generated owner.",
            "Changed the handwritten adapter.",
            "dotnet test",
            "Expected generated owner was unchanged.",
            "The attempted change did not exercise the instructed seam.",
            "The generated owner controls the behavior.",
            "Change the generated source input and regenerate."
        );
        var failedInstruction = Request(PlannerQuestionType.FailedInstruction) with
        {
            FailedInstruction = context,
        };
        var ordinary = Request(PlannerQuestionType.RepositoryProcedure);

        (await Validate(failedInstruction)).IsValid.Should().BeTrue();
        (await Validate(failedInstruction with { FailedInstruction = null }))
            .Errors.Should()
            .Contain(error => error.ErrorCode == "ask_planner.failed_instruction.required");
        (await Validate(ordinary with { FailedInstruction = context }))
            .Errors.Should()
            .Contain(error => error.ErrorCode == "ask_planner.failed_instruction.forbidden");
    }

    [Fact]
    public async Task Every_planner_decision_requires_a_safe_next_action_and_only_revision_or_reorient_has_a_corrected_approach()
    {
        var revise = Decision(PlannerDecisionValue.ReviseApproach) with
        {
            CorrectedApproach = "Use the repository's existing owner.",
        };

        (await Validate(revise)).IsValid.Should().BeTrue();
        (
            await Validate(
                Decision(PlannerDecisionValue.Reorient) with
                {
                    CorrectedApproach = "Restart from durable state through the existing owner.",
                }
            )
        )
            .IsValid.Should()
            .BeTrue();
        (await Validate(revise with { CorrectedApproach = null }))
            .Errors.Should()
            .Contain(error => error.ErrorCode == "planner.corrected_approach.required");
        (await Validate(Decision(PlannerDecisionValue.Proceed) with { SafeNextAction = "" }))
            .Errors.Should()
            .Contain(error => error.ErrorCode == "planner.safe_next_action.required");
        (
            await Validate(
                Decision(PlannerDecisionValue.Stop) with
                {
                    CorrectedApproach = "Not valid for stop.",
                }
            )
        )
            .Errors.Should()
            .Contain(error => error.ErrorCode == "planner.corrected_approach.forbidden");
    }

    [Theory]
    [InlineData(PlannerDecisionValue.Proceed, false)]
    [InlineData(PlannerDecisionValue.ProceedWithConstraints, true)]
    [InlineData(PlannerDecisionValue.ReviseApproach, true)]
    [InlineData(PlannerDecisionValue.Reorient, true)]
    [InlineData(PlannerDecisionValue.NeedsHuman, true)]
    [InlineData(PlannerDecisionValue.Stop, true)]
    public void Only_accepted_decisions_replace_active_constraints(
        PlannerDecisionValue value,
        bool preservesExisting
    )
    {
        var state = TestSupport.State() with { PlannerConstraints = ["Existing obligation"] };
        var decision = Decision(value) with
        {
            Constraints =
                value == PlannerDecisionValue.ProceedWithConstraints
                    ? ["Replacement obligation"]
                    : [],
            CorrectedApproach = value
                is PlannerDecisionValue.ReviseApproach
                    or PlannerDecisionValue.Reorient
                ? "Use the correct owner."
                : null,
            HumanQuestion = value == PlannerDecisionValue.NeedsHuman ? "Which behavior?" : null,
            HumanDecisionDomain =
                value == PlannerDecisionValue.NeedsHuman
                    ? Cadence.HumanDecisionDomain.Product
                    : null,
        };

        state = state.RecordPlannerDecision(decision);

        state
            .PlannerConstraints.Should()
            .Equal(
                preservesExisting
                    ? value == PlannerDecisionValue.ProceedWithConstraints
                        ? ["Replacement obligation"]
                        : ["Existing obligation"]
                    : []
            );
        state
            .MutationAuthorized.Should()
            .Be(
                value is PlannerDecisionValue.Proceed or PlannerDecisionValue.ProceedWithConstraints
            );
    }

    [Fact]
    public async Task Planner_failure_stage_counts_failures_without_discarding_state()
    {
        var stage = new PlannerFailureStage().Definition;
        var complete = PipelineNodes.Complete(new PlannerFailureCounted());
        var state = TestSupport.State() with
        {
            PlannerConstraints = ["Keep this obligation"],
            OutcomeLedger =
            [
                new OutcomeLedgerEntry(
                    "outcome-1",
                    "Deliver the feature",
                    OutcomeStatus.Complete,
                    ["evidence"],
                    "Implemented.",
                    null
                ),
            ],
        };
        var pipeline = Pipeline
            .Start(stage, "planner-failure-count")
            .Route(stage, complete, "counted")
            .Build(complete);

        var result = await new PipelineRunner().RunAsync(
            pipeline,
            state,
            cancellationToken: TestContext.Current.CancellationToken
        );

        result.State.PlannerFailureCount.Should().Be(1);
        result.State.PlannerConstraints.Should().Equal("Keep this obligation");
        result.State.OutcomeLedger.Should().Equal(state.OutcomeLedger);
    }

    [Fact]
    public void Planner_prompt_pins_operational_safety_clauses_and_separates_live_constraints()
    {
        var prompt =
            PlannerPrompts.Instructions
            + PlannerPrompts.BuildMessage(
                TestSupport.State() with
                {
                    PlannerConstraints = ["Live obligation"],
                }
            );

        prompt.Should().Contain("untrusted pointer to verify, not proof");
        prompt.Should().Contain("Audit the complete proposed approach");
        prompt.Should().Contain("Correct XY problems");
        prompt.Should().Contain("expand, contract, split, or change owner");
        prompt.Should().Contain("Constraints cannot authorize known breakage");
        prompt.Should().Contain("another approval cycle before editing");
        prompt.Should().Contain("not evidence that prior obligations are closed");
        prompt.Should().Contain("contradictory evidence");
        prompt.Should().Contain("Do not repeat the instruction");
        prompt.Should().Contain("SafeNextAction for every response");
        prompt.Should().Contain("Reorient only when QuestionType is SessionReliability");
        prompt.Should().Contain("fresh Executor must submit its revised approach for approval");
        prompt.Should().Contain("Active accepted planner constraints");
        prompt.Should().Contain("Latest planner decision");
    }

    [Fact]
    public void Durable_context_separates_active_constraints_from_decision_history()
    {
        var text = CadenceLedgerContextFormatter.Format(
            new CadenceLedgerContext(
                null,
                null,
                null,
                ["Active obligation"],
                [
                    Decision(PlannerDecisionValue.ReviseApproach) with
                    {
                        CorrectedApproach = "Use the correct owner.",
                    },
                ],
                [],
                [],
                []
            )
        );

        text.Should().Contain("Active accepted Planner constraints:");
        text.Should().Contain("- Active obligation");
        text.Should().Contain("Recent Planner decisions:");
        text.Should().NotContain("constraints=Use the correct owner");
    }

    [Fact]
    public void Executor_prompt_routes_unreliable_context_and_checkpoint_uncertainty_mechanically()
    {
        var prompt = ExecutorPrompts.Instructions + ExecutorPrompts.CheckpointInstructions;

        prompt.Should().Contain("QuestionType SessionReliability");
        prompt.Should().Contain("discards this conversation before Planner runs");
        prompt.Should().Contain("Any non-empty uncertainties close mutation authority");
        prompt.Should().Contain("Never write \"none\" as an uncertainty");
    }

    [Fact]
    public async Task Planner_unavailable_is_a_typed_terminal_failure_after_two_counted_failures()
    {
        var unavailable = PipelineNodes.Failed(new PlannerUnavailable());
        var stage = new PlannerFailureStage().Definition;
        var state = TestSupport.State().RecordPlannerFailure();
        var pipeline = Pipeline
            .Start(stage, "planner-unavailable-proof")
            .Route(
                from: stage,
                when: current => current.PlannerFailureCount >= 2,
                to: unavailable,
                label: "planner unavailable"
            )
            .Build(unavailable);

        var result = await new PipelineRunner().RunAsync(
            pipeline,
            state,
            cancellationToken: TestContext.Current.CancellationToken
        );

        result.Succeeded.Should().BeFalse();
        result.State.PlannerFailureCount.Should().Be(2);
    }

    private static AskPlannerRequest Request(PlannerQuestionType type) =>
        new(type, "current slice", "What is safe?", "Use the existing seam.", ["src/a.cs"]);

    private static PlannerDecision Decision(PlannerDecisionValue value) =>
        new(value, "Evidence supports this decision.", [], ["src/a.cs"], "Take one safe step.");

    private static Task<FluentValidation.Results.ValidationResult> Validate(
        AskPlannerRequest request
    ) => new AskPlannerRequestValidator().ValidateAsync(request);

    private static Task<FluentValidation.Results.ValidationResult> Validate(
        PlannerDecision decision
    ) => new PlannerDecisionValidator().ValidateAsync(decision);

    private sealed class PlannerFailureCounted : IPipelineCompletion<CadenceState>
    {
        public string Id => "planner-failure-counted";

        public string Summarize(CadenceState state) => "Planner failure counted.";
    }
}
