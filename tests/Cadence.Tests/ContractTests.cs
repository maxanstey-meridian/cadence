using FluentAssertions;

namespace Cadence.Tests;

public sealed class ContractTests
{
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
    public void Reviewer_and_ledger_context_render_regression_test_evidence()
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
        var ledger = new CadenceLedgerContext(null, report, null, [], [], [], [], []);

        ReviewerPrompts
            .BuildMessage(state, TestSupport.Doctrine())
            .Should()
            .Contain("Regression tests: Added: tests/a.cs: focused regression");
        CadenceLedgerContextFormatter
            .Format(ledger)
            .Should()
            .Contain("Regression tests: Added; evidence=tests/a.cs: focused regression");
    }
}
