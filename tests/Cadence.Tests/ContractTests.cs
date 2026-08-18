using FluentAssertions;

namespace Cadence.Tests;

public sealed class ContractTests
{
    [Fact]
    public void Executor_stops_investigating_at_the_next_lifecycle_action()
    {
        ExecutorPrompts.Instructions.Should().Contain("call ask_planner immediately");
        ExecutorPrompts.Instructions.Should().Contain("announce that you are ready and then");
        ExecutorPrompts.Instructions.Should().Contain("next authorized edit");
        ExecutorPrompts.Instructions.Should().Contain("begin mutation");
        ExecutorPrompts
            .Instructions.Should()
            .Contain("You own implementation and ordinary engineering judgment");
        ExecutorPrompts.Instructions.Should().Contain("not a reason by itself");
        ExecutorPrompts.Instructions.Should().Contain("to call ask_planner");
        ExecutorPrompts.Instructions.Should().Contain("remove a confirmed unused");
        ExecutorPrompts.Instructions.Should().Contain("Do not use ask_planner for reassurance");
        ExecutorPrompts.Instructions.Should().Contain("Ambiguity must be consequential");
        ExecutorPrompts.Instructions.Should().Contain("One failed attempt");
        ExecutorPrompts.Instructions.Should().Contain("is not by itself a Planner boundary");
        new AskPlannerCapability()
            .Instructions.Should()
            .Contain("Do not use for ordinary implementation decisions");
    }

    [Fact]
    public async Task Decision_discriminants_must_be_defined_enum_values()
    {
        var planner = new PlannerDecision(
            (PlannerDecisionValue)999,
            "Rationale",
            [],
            ["src/a.cs"],
            "Inspect the implementation."
        );
        var reviewer = new ReviewDecision(
            (ReviewDecisionValue)999,
            TestSupport.Doctrine().Sha256,
            "Summary",
            [
                new ReviewOutcomeAssessment(
                    "outcome-1",
                    true,
                    [TestSupport.FileEvidence("src/a.cs")]
                ),
            ],
            [],
            []
        );

        var plannerResult = await new PlannerDecisionValidator().ValidateAsync(
            planner,
            TestContext.Current.CancellationToken
        );
        var reviewerResult = await new ReviewDecisionValidator(
            TestSupport.Doctrine(),
            ["outcome-1"]
        ).ValidateAsync(reviewer, TestContext.Current.CancellationToken);

        plannerResult
            .Errors.Should()
            .Contain(error => error.ErrorCode == "planner.decision.invalid");
        reviewerResult
            .Errors.Should()
            .Contain(error => error.ErrorCode == "review.decision.invalid");
    }

    [Fact]
    public async Task Planner_constraints_must_not_be_null()
    {
        var decision = new PlannerDecision(
            PlannerDecisionValue.Proceed,
            "Rationale",
            null!,
            ["src/a.cs"],
            "Inspect the implementation."
        );

        var result = await new PlannerDecisionValidator().ValidateAsync(
            decision,
            TestContext.Current.CancellationToken
        );

        result.Errors.Should().Contain(error => error.ErrorCode == "planner.constraints.required");
    }

    [Fact]
    public async Task Reviewer_accept_requires_every_planner_constraint_to_be_satisfied()
    {
        var decision = new ReviewDecision(
            ReviewDecisionValue.Accept,
            TestSupport.Doctrine().Sha256,
            "Accepted despite an unmet constraint.",
            [
                new ReviewOutcomeAssessment(
                    "outcome-1",
                    true,
                    [TestSupport.FileEvidence("src/a.cs")]
                ),
            ],
            [],
            [
                new ReviewConstraintAssessment(
                    "Preserve compatibility",
                    false,
                    [
                        new ReviewEvidenceReference(
                            ReviewEvidenceKind.Constraint,
                            Constraint: "Preserve compatibility"
                        ),
                    ]
                ),
            ]
        );

        var result = await new ReviewDecisionValidator(
            TestSupport.Doctrine(),
            ["outcome-1"],
            ["Preserve compatibility"]
        ).ValidateAsync(decision, TestContext.Current.CancellationToken);

        result
            .Errors.Should()
            .Contain(error =>
                error.ErrorCode == "review.constraint_assessments.unsatisfied_for_accept"
            );
    }

    [Fact]
    public async Task Reviewer_requires_packet_and_planner_constraints_exactly_once()
    {
        var state = TestSupport.State() with
        {
            Packet = TestSupport.Packet() with { Constraints = ["Packet obligation"] },
            PlannerConstraints = ["Planner obligation", "Packet obligation"],
        };
        var decision = new ReviewDecision(
            ReviewDecisionValue.Accept,
            TestSupport.Doctrine().Sha256,
            "All obligations are satisfied.",
            [
                new ReviewOutcomeAssessment(
                    "outcome-1",
                    true,
                    [TestSupport.FileEvidence("src/a.cs")]
                ),
            ],
            [],
            state
                .Constraints.Select(constraint => new ReviewConstraintAssessment(
                    constraint,
                    true,
                    [
                        new ReviewEvidenceReference(
                            ReviewEvidenceKind.Constraint,
                            Constraint: constraint
                        ),
                        TestSupport.FileEvidence(),
                    ]
                ))
                .ToArray()
        );
        var validator = new ReviewDecisionValidator(
            TestSupport.Doctrine(),
            ["outcome-1"],
            state.Constraints
        );

        (await validator.ValidateAsync(decision, TestContext.Current.CancellationToken))
            .IsValid.Should()
            .BeTrue();
        (
            await validator.ValidateAsync(
                decision with
                {
                    ConstraintAssessments = [decision.ConstraintAssessments[0]],
                },
                TestContext.Current.CancellationToken
            )
        )
            .Errors.Should()
            .Contain(error => error.ErrorCode == "review.constraint_assessments.missing");
        (
            await validator.ValidateAsync(
                decision with
                {
                    ConstraintAssessments =
                    [
                        decision.ConstraintAssessments[0],
                        decision.ConstraintAssessments[0],
                        new ReviewConstraintAssessment(
                            "Unknown",
                            true,
                            [
                                new ReviewEvidenceReference(
                                    ReviewEvidenceKind.Constraint,
                                    Constraint: "Unknown"
                                ),
                            ]
                        ),
                    ],
                },
                TestContext.Current.CancellationToken
            )
        )
            .Errors.Select(error => error.ErrorCode)
            .Should()
            .Contain([
                "review.constraint_assessments.duplicate",
                "review.constraint_assessments.unknown",
            ]);
    }

    [Fact]
    public void Duplicate_packet_outcome_ids_are_rejected()
    {
        var packet = TestSupport.Packet() with
        {
            Outcomes =
            [
                new PacketOutcome("duplicate", "First"),
                new PacketOutcome("duplicate", "Second"),
            ],
        };

        var act = () => CadenceState.Create(packet, "base", "/workspace");

        act.Should().Throw<ArgumentException>().WithMessage("*outcome IDs must be unique*");
    }

    [Fact]
    public async Task Reviewer_requires_implementation_evidence_for_delivered_outcomes_and_constraints()
    {
        var state = CadenceState.Create(
            TestSupport.Packet() with
            {
                Constraints = ["Preserve the public contract."],
            },
            "base-sha",
            "/workspace"
        );
        var constraint = state.Constraints[0];
        var outcomeId = state.Packet.Outcomes[0].Id;
        var decision = new ReviewDecision(
            ReviewDecisionValue.Accept,
            TestSupport.Doctrine().Sha256,
            "The packet outcome and constraint are satisfied.",
            [
                new ReviewOutcomeAssessment(
                    outcomeId,
                    true,
                    [
                        new ReviewEvidenceReference(
                            ReviewEvidenceKind.PacketOutcome,
                            OutcomeId: outcomeId
                        ),
                    ]
                ),
            ],
            [],
            [
                new ReviewConstraintAssessment(
                    constraint,
                    true,
                    [
                        new ReviewEvidenceReference(
                            ReviewEvidenceKind.Constraint,
                            Constraint: constraint
                        ),
                    ]
                ),
            ]
        );
        var output = new ReviewDecisionOutput(TestSupport.Doctrine());

        var result = await output
            .ValidatorFor(state)
            .ValidateAsync(decision, TestContext.Current.CancellationToken);

        result
            .Errors.Select(error => error.ErrorCode)
            .Should()
            .Contain([
                "review.outcomes.implementation_evidence_required",
                "review.constraint_assessments.implementation_evidence_required",
            ]);

        var evidenced = decision with
        {
            Outcomes =
            [
                decision.Outcomes[0] with
                {
                    Evidence = [.. decision.Outcomes[0].Evidence, TestSupport.FileEvidence()],
                },
            ],
            ConstraintAssessments =
            [
                decision.ConstraintAssessments[0] with
                {
                    Evidence =
                    [
                        .. decision.ConstraintAssessments[0].Evidence,
                        TestSupport.FileEvidence(),
                    ],
                },
            ],
        };
        (
            await output
                .ValidatorFor(state)
                .ValidateAsync(evidenced, TestContext.Current.CancellationToken)
        )
            .IsValid.Should()
            .BeTrue();
    }

    [Fact]
    public async Task Reviewer_accept_requires_every_outcome_to_be_delivered()
    {
        var decision = new ReviewDecision(
            ReviewDecisionValue.Accept,
            TestSupport.Doctrine().Sha256,
            "Accepted despite a missing outcome.",
            [
                new ReviewOutcomeAssessment(
                    "outcome-1",
                    false,
                    [
                        new ReviewEvidenceReference(
                            ReviewEvidenceKind.PacketOutcome,
                            OutcomeId: "outcome-1"
                        ),
                    ]
                ),
            ],
            [],
            []
        );

        var result = await new ReviewDecisionValidator(
            TestSupport.Doctrine(),
            ["outcome-1"]
        ).ValidateAsync(decision, TestContext.Current.CancellationToken);

        result
            .Errors.Should()
            .Contain(error => error.ErrorCode == "review.outcomes.undelivered_for_accept");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Human_decision_domain_is_required_only_for_needs_human(bool planner)
    {
        if (planner)
        {
            var missing = new PlannerDecision(
                PlannerDecisionValue.NeedsHuman,
                "Product intent is required.",
                [],
                ["PLAN.md"],
                "Ask the Human which behavior is intended.",
                null,
                "Which behavior is intended?"
            );
            var supplied = missing with { HumanDecisionDomain = HumanDecisionDomain.Product };
            var forbidden = supplied with { Decision = PlannerDecisionValue.Stop };

            (
                await new PlannerDecisionValidator().ValidateAsync(
                    missing,
                    TestContext.Current.CancellationToken
                )
            )
                .Errors.Should()
                .Contain(error => error.ErrorCode == "planner.human_decision_domain.required");
            (
                await new PlannerDecisionValidator().ValidateAsync(
                    supplied,
                    TestContext.Current.CancellationToken
                )
            )
                .IsValid.Should()
                .BeTrue();
            (
                await new PlannerDecisionValidator().ValidateAsync(
                    forbidden,
                    TestContext.Current.CancellationToken
                )
            )
                .Errors.Should()
                .Contain(error => error.ErrorCode == "planner.human_decision_domain.forbidden");
            return;
        }

        var reviewMissing = new ReviewDecision(
            ReviewDecisionValue.NeedsHuman,
            TestSupport.Doctrine().Sha256,
            "A permissions policy decision is required.",
            [
                new ReviewOutcomeAssessment(
                    "outcome-1",
                    false,
                    [
                        new ReviewEvidenceReference(
                            ReviewEvidenceKind.PacketOutcome,
                            OutcomeId: "outcome-1"
                        ),
                    ]
                ),
            ],
            [],
            [],
            "Who may perform this action?"
        );
        var reviewSupplied = reviewMissing with
        {
            HumanDecisionDomain = HumanDecisionDomain.Permissions,
        };
        var reviewForbidden = reviewSupplied with { Decision = ReviewDecisionValue.RequestChanges };

        (
            await new ReviewDecisionValidator(TestSupport.Doctrine(), ["outcome-1"]).ValidateAsync(
                reviewMissing,
                TestContext.Current.CancellationToken
            )
        )
            .Errors.Should()
            .Contain(error => error.ErrorCode == "review.human_decision_domain.required");
        (
            await new ReviewDecisionValidator(TestSupport.Doctrine(), ["outcome-1"]).ValidateAsync(
                reviewSupplied,
                TestContext.Current.CancellationToken
            )
        )
            .IsValid.Should()
            .BeTrue();
        (
            await new ReviewDecisionValidator(TestSupport.Doctrine(), ["outcome-1"]).ValidateAsync(
                reviewForbidden,
                TestContext.Current.CancellationToken
            )
        )
            .Errors.Should()
            .Contain(error => error.ErrorCode == "review.human_decision_domain.forbidden");
    }

    [Fact]
    public void Agent_messages_include_packet_context_and_constraints()
    {
        var state = TestSupport.State() with
        {
            Packet = TestSupport.Packet() with
            {
                ImplementationContext = "Use the established adapter seam.",
                Constraints = ["Do not change the public contract."],
            },
        };

        var messages = new[]
        {
            ExecutorPrompts.BuildMessage(state),
            PlannerPrompts.BuildMessage(state),
            ReviewerPrompts.BuildMessage(state, TestSupport.Doctrine()),
        };

        messages
            .Should()
            .OnlyContain(message =>
                message.Contains("Use the established adapter seam.", StringComparison.Ordinal)
                && message.Contains("Do not change the public contract.", StringComparison.Ordinal)
            );
    }

    [Fact]
    public void Planner_message_identifies_direct_checkpoint_review()
    {
        var state = TestSupport
            .State()
            .RecordCheckpoint(
                new WriteCheckpointRequest(
                    "Progress",
                    ["Open question"],
                    "Continue implementation"
                ),
                DateTimeOffset.Parse("2026-08-14T12:00:00Z")
            );

        PlannerPrompts
            .BuildMessage(state)
            .Should()
            .Contain("Checkpoint review requested")
            .And.NotContain("(no request provided)");
    }

    [Fact]
    public void Executor_message_explains_current_revocable_mutation_authority()
    {
        var message = ExecutorPrompts.BuildMessage(TestSupport.State());

        message.Should().Contain("Mutation-authority lifecycle:");
        message.Should().Contain("current authority for this invocation");
        message.Should().Contain("revocable lease");
        message.Should().Contain("be open in one invocation");
        message.Should().Contain("closed in the next without contradiction");
        message.Should().Contain("mutation tools are intentionally absent");
        message.Should().Contain("checkpoint-only invocation is the explicit exception");
    }

    [Fact]
    public void Executor_message_renders_safe_next_action_as_one_immediate_action_not_a_scope()
    {
        var state = TestSupport.State() with
        {
            ApproachRevision = 1,
            ApprovedApproachRevision = 1,
            PlannerDecision = new PlannerDecision(
                PlannerDecisionValue.Proceed,
                "The inspected seam supports this step.",
                [],
                ["src/adapter.cs"],
                "Update the inspected adapter method.",
                null,
                null,
                null
            ),
        };

        var message = ExecutorPrompts.BuildMessage(state);

        message
            .Should()
            .Contain("Safe next action (one immediate action, not a scope or working set):");
        message.Should().Contain("Update the inspected adapter method.");
        message.Should().NotContain("Current working slice");
        message.Should().NotContain("Only this slice is active");
        message.Should().NotContain("authoritative active slice");
    }

    [Fact]
    public void Executor_instructions_scope_global_understanding_to_invariants_and_current_work()
    {
        var prompt = ExecutorPrompts.Instructions;

        prompt.Should().Contain("span many Executor sessions");
        prompt.Should().Contain("Delivery contract: the complete packet and all outcomes");
        prompt.Should().Contain("invariants and the direct");
        prompt.Should().Contain("Current working scope: InProgress outcomes");
        prompt.Should().Contain("accepted Planner constraints");
        prompt.Should().Contain("Reviewer findings or failed verification command");
        prompt.Should().Contain("NotStarted outcomes are future roadmap");
        prompt.Should().Contain("do not inventory,");
        prompt.Should().Contain("Use three levels of scope");
        prompt.Should().Contain("SafeNextAction: one immediate action within that scope");
        prompt.Should().Contain("it does not define or limit the scope");
        prompt.Should().Contain("repository work promptly instead of rereading the packet");
        prompt.Should().NotContain("current task");
        prompt.Should().NotContain("current assignment");
        prompt.Should().NotContain("only this action is active");
    }

    [Fact]
    public void Planner_instructions_keep_future_backlog_out_of_current_constraints()
    {
        var prompt = PlannerPrompts.Instructions;

        prompt.Should().Contain("span many Executor sessions");
        prompt.Should().Contain("not the whole delivery");
        prompt.Should().Contain("cross-cutting invariants, never a task list for future");
        prompt.Should().Contain("NotStarted outcomes or a restatement of the remaining backlog");
        prompt.Should().Contain("reconcile every packet outcome before acting");
        prompt.Should().Contain("SafeNextAction is one immediate action");
        prompt.Should().Contain("Proceed authorizes the");
        prompt.Should().Contain("without additional Planner constraints");
        prompt.Should().Contain("For NeedsHuman, it is to await");
        prompt.Should().Contain("For Stop, it is to stop without");
        prompt.Should().NotContain("authoritative active slice");
        prompt.Should().NotContain("Select one bounded working slice");
        prompt.Should().NotContain("current task");
        prompt.Should().NotContain("current assignment");
        prompt.Should().NotContain("only this action is active");
    }

    [Fact]
    public void Planner_output_instruction_keeps_safe_next_action_below_authorized_scope()
    {
        new PlannerDecisionOutput()
            .Instructions.Should()
            .Contain("one immediate action, not the authorized scope");
    }

    [Fact]
    public void Checkpoint_instructions_preserve_progress_without_enumerating_later_phases()
    {
        var prompt = ExecutorPrompts.CheckpointInstructions;

        prompt.Should().Contain("successor-oriented summary");
        prompt.Should().Contain("one precise next");
        prompt.Should().NotContain("current working slice");
        prompt.Should().NotContain("future phase");
        prompt.Should().NotContain("backlog");
    }

    [Fact]
    public void Reviewer_renders_regression_test_evidence()
    {
        var report = new SubmitReportRequest(
            "Implemented",
            "Implement feature",
            [],
            new RegressionTestClaim(
                RegressionTestDisposition.Added,
                ["tests/a.cs: focused regression"]
            )
        );
        var state = TestSupport.State().RecordImplementationReport(report);
        ReviewerPrompts
            .BuildMessage(state, TestSupport.Doctrine())
            .Should()
            .Contain("Regression tests: Added: tests/a.cs: focused regression");
    }
}
