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
    public void Executor_message_elevates_the_bounded_planner_slice_above_the_delivery_roadmap()
    {
        var state = TestSupport.State() with
        {
            ApproachRevision = 1,
            ApprovedApproachRevision = 1,
            PlannerDecision = new PlannerDecision(
                PlannerDecisionValue.Proceed,
                "The inspected seam supports this slice.",
                [],
                ["src/adapter.cs"],
                "Implement only the focused adapter test slice.",
                null,
                null,
                null
            ),
        };

        var message = ExecutorPrompts.BuildMessage(state);

        message.Should().Contain("Current working slice:");
        message.Should().Contain("Implement only the focused adapter test slice.");
        message.Should().Contain("Only this slice is active");
        message.Should().Contain("Delivery roadmap (authoritative outcome ledger):");
        message
            .IndexOf("Current working slice:", StringComparison.Ordinal)
            .Should()
            .BeLessThan(
                message.IndexOf(
                    "Delivery roadmap (authoritative outcome ledger):",
                    StringComparison.Ordinal
                )
            );
    }

    [Fact]
    public void Planner_and_checkpoint_prompts_schedule_only_one_bounded_slice()
    {
        var plannerMessage = PlannerPrompts.BuildMessage(TestSupport.State());

        plannerMessage.Should().Contain("Select one bounded working slice");
        plannerMessage.Should().Contain("SafeNextAction is the authoritative active slice");
        PlannerPrompts
            .Instructions.Should()
            .Contain("Large packets are expected to span many periodic Executor sessions");
        PlannerPrompts.Instructions.Should().Contain("not a task list for later phases");
        PlannerPrompts.Instructions.Should().Contain("not summarize the remaining delivery");
        ExecutorPrompts
            .CheckpointInstructions.Should()
            .Contain("one precise immediate next action");
        ExecutorPrompts
            .CheckpointInstructions.Should()
            .Contain("reconcile, inventory, or enumerate future phases");
        new PlannerDecisionOutput()
            .Instructions.Should()
            .Contain("one bounded SafeNextAction for the next Executor session");
    }

    [Fact]
    public void Executor_repair_routes_override_the_prior_planner_slice()
    {
        var planner = new PlannerDecision(
            PlannerDecisionValue.Proceed,
            "The original slice was safe.",
            [],
            ["src/adapter.cs"],
            "Continue the original adapter slice.",
            null,
            null,
            null
        );
        var verification = TestSupport.State() with
        {
            ApproachRevision = 1,
            ApprovedApproachRevision = 1,
            PlannerDecision = planner,
            VerificationResults =
            [
                new VerificationResult(0, "task check", 1, "", "failed", TimeSpan.Zero, false),
            ],
        };
        var review = verification with
        {
            VerificationResults = [],
            ReviewRepairRequired = true,
            ReviewerDecision = new ReviewDecision(
                ReviewDecisionValue.RequestChanges,
                TestSupport.Doctrine().Sha256,
                "Repair required.",
                [],
                [new ReviewFinding(ReviewFindingSeverity.High, "Restore the invariant.", [])],
                []
            ),
        };

        ExecutorPrompts
            .BuildMessage(verification)
            .Should()
            .Contain("Diagnose and repair the latest failed verification command: task check");
        ExecutorPrompts
            .BuildMessage(review)
            .Should()
            .Contain("starting with: Restore the invariant.");
    }

    [Fact]
    public void Reviewer_renders_regression_test_evidence()
    {
        var report = new SubmitReportRequest(
            "Implemented",
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
