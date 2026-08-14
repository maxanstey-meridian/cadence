using FluentAssertions;

namespace Cadence.Tests;

public sealed class CadenceStateTests
{
    [Fact]
    public void State_initializes_continuity_from_the_supplied_time_provider()
    {
        var now = DateTimeOffset.Parse("2026-08-11T14:00:00Z");

        var state = CadenceState.Create(
            TestSupport.Packet(),
            "base",
            "/workspace",
            new FakeTimeProvider(now)
        );

        state.LastContinuityAt.Should().Be(now);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void State_rejects_a_non_positive_review_limit(int maximumReviewAttempts)
    {
        var act = () =>
            CadenceState.Create(
                TestSupport.Packet(),
                "base",
                "/workspace",
                maximumReviewAttempts: maximumReviewAttempts
            );

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void State_rejects_blank_verification_commands(string command)
    {
        var packet = TestSupport.Packet() with { Verification = [command] };

        var act = () => CadenceState.Create(packet, "base", "/workspace");

        act.Should().Throw<ArgumentException>().WithMessage("*must not be blank*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void State_rejects_blank_repository_commands(string command)
    {
        var packet = TestSupport.Packet() with { Commands = [command] };

        var act = () => CadenceState.Create(packet, "base", "/workspace");

        act.Should().Throw<ArgumentException>().WithMessage("*commands must not be blank*");
    }

    [Fact]
    public void Recovery_restores_accepted_facts_but_closes_mutation_and_requests_planner_review()
    {
        var decision = new PlannerDecision(
            PlannerDecisionValue.ProceedWithConstraints,
            "Approved before interruption.",
            ["Preserve the contract."],
            ["README.md"],
            "Continue the implementation."
        );
        var persisted = TestSupport.State() with
        {
            PlannerDecision = decision,
            PlannerConstraints = ["Preserve the contract."],
            PlannerFailureCount = 1,
            OutcomeLedger =
            [
                new OutcomeLedgerEntry(
                    "outcome-1",
                    "Deliver the feature",
                    OutcomeStatus.InProgress,
                    ["README.md:1"],
                    "Partially implemented.",
                    "Finish the implementation."
                ),
            ],
            LatestCheckpoint = new WriteCheckpointRequest(
                "Work is partially implemented.",
                [],
                "Finish the implementation."
            ),
        };

        var state = persisted.Resume(TestSupport.Packet());

        state.MutationAuthorized.Should().BeFalse();
        state.PlannerConstraints.Should().Equal("Preserve the contract.");
        state.PlannerFailureCount.Should().Be(1);
        state.OutcomeLedger.Single().Status.Should().Be(OutcomeStatus.InProgress);
        state.LatestCheckpoint!.NextAction.Should().Be("Finish the implementation.");
        state.ExecutorTransition.Should().BeOfType<ExecutorTransition.PlannerRequested>();
        ((ExecutorTransition.PlannerRequested)state.ExecutorTransition!)
            .Request.Should()
            .Match<AskPlannerRequest>(request =>
                request.QuestionType == PlannerQuestionType.SessionReliability
                && request.Evidence.Any(value =>
                    value.Contains("Checkpoint uncertainties", StringComparison.Ordinal)
                )
            );
    }

    [Fact]
    public void Recovery_rejects_runs_that_reached_candidate_verification()
    {
        var persisted = TestSupport.State() with
        {
            VerificationResults =
            [
                new VerificationResult(0, "task check", 0, "", "", TimeSpan.Zero, false),
            ],
        };

        var act = () => persisted.Resume(TestSupport.Packet());

        act.Should().Throw<InvalidOperationException>().WithMessage("*executor-phase*");
    }

    [Fact]
    public void Recovery_accepts_compatible_replacement_packet_content()
    {
        var packet = TestSupport.Packet() with { Commands = ["task generate"] };

        var state = TestSupport.State().Resume(packet);

        state.Packet.Should().BeSameAs(packet);
        state.OutcomeLedger.Single().OutcomeId.Should().Be("outcome-1");
        state.OutcomeLedger.Single().Status.Should().Be(OutcomeStatus.NotStarted);
    }

    [Fact]
    public void Recovery_treats_the_supplied_packet_as_authoritative()
    {
        var packet = TestSupport.Packet() with
        {
            Repository = "/different/repository",
            Base = "different-base",
            Outcomes = [new PacketOutcome("replacement", "Deliver replacement behavior")],
        };

        var state = TestSupport.State().Resume(packet);

        state.Packet.Should().BeSameAs(packet);
        state.OutcomeLedger.Should().ContainSingle().Which.OutcomeId.Should().Be("replacement");
    }

    [Theory]
    [InlineData("", "description")]
    [InlineData("id", "   ")]
    public void State_rejects_blank_outcome_identity(string id, string description)
    {
        var packet = TestSupport.Packet() with { Outcomes = [new PacketOutcome(id, description)] };

        var act = () => CadenceState.Create(packet, "base", "/workspace");

        act.Should().Throw<ArgumentException>().WithMessage("*must not be blank*");
    }

    [Theory]
    [InlineData(ReviewDecisionValue.Accept)]
    [InlineData(ReviewDecisionValue.NeedsHuman)]
    public void Non_repair_review_decisions_do_not_consume_repair_budget(
        ReviewDecisionValue decisionValue
    )
    {
        var state = TestSupport.State().RecordReviewDecision(Decision(decisionValue));

        state.ReviewAttempt.Should().Be(0);
        state.ReviewRepairRequired.Should().BeFalse();
    }

    [Fact]
    public void Request_changes_requires_a_material_outcome_update_before_resubmission()
    {
        var update = new OutcomeUpdate(
            "outcome-1",
            OutcomeStatus.InProgress,
            ["src/a.cs:1"],
            "Initial implementation.",
            "Repair the defect."
        );
        var reviewed = TestSupport
            .State()
            .RecordOutcomeUpdates(new UpdateOutcomesRequest([update]))
            .RecordReviewDecision(Decision(ReviewDecisionValue.RequestChanges));

        var unchanged = reviewed.RecordOutcomeUpdates(new UpdateOutcomesRequest([update]));
        var repaired = reviewed.RecordOutcomeUpdates(
            new UpdateOutcomesRequest([
                update with
                {
                    ImplementationState = "The review defect is repaired.",
                },
            ])
        );

        reviewed.ReviewAttempt.Should().Be(1);
        reviewed.ReviewRepairRequired.Should().BeTrue();
        unchanged.ReviewRepairRequired.Should().BeTrue();
        repaired.ReviewRepairRequired.Should().BeFalse();
    }

    [Fact]
    public void Planner_authority_is_scoped_to_the_current_approach_revision()
    {
        var now = DateTimeOffset.Parse("2026-08-11T10:00:00Z");
        var state = TestSupport.State(now: now);

        state = state.RecordPlannerRequest(
            new AskPlannerRequest(
                PlannerQuestionType.ArchitectureOrEngineeringDirection,
                "existing seam",
                "How?",
                "Implement through the existing seam.",
                ["src/a.cs"]
            ),
            now.AddMinutes(1)
        );
        state.MutationAuthorized.Should().BeFalse();
        state.ApproachRevision.Should().Be(1);

        state = state.RecordPlannerDecision(
            new PlannerDecision(
                PlannerDecisionValue.ProceedWithConstraints,
                "The inspected seam supports the approach.",
                ["Preserve the public contract."],
                ["src/a.cs"],
                "Implement through the existing seam."
            )
        );
        state.MutationAuthorized.Should().BeTrue();
        state.ApprovedApproachRevision.Should().Be(1);

        state = state.RecordPlannerRequest(
            new AskPlannerRequest(
                PlannerQuestionType.ImplementationSurfaceReview,
                "alternate seam",
                "Changed plan?",
                "Use another seam.",
                ["src/b.cs"]
            ),
            now.AddMinutes(2)
        );
        state.MutationAuthorized.Should().BeFalse();
        state.ApproachRevision.Should().Be(2);
    }

    [Fact]
    public void A_new_planner_decision_replaces_old_constraints()
    {
        var state = TestSupport
            .State()
            .RecordPlannerDecision(
                new PlannerDecision(
                    PlannerDecisionValue.ProceedWithConstraints,
                    "Proceed carefully.",
                    ["Old constraint"],
                    ["src/a.cs"],
                    "Preserve the public contract."
                )
            );

        state = state.RecordPlannerDecision(
            new PlannerDecision(
                PlannerDecisionValue.Proceed,
                "No implementation obligations remain.",
                [],
                ["src/a.cs"],
                "Continue with the approved approach."
            )
        );

        state.PlannerConstraints.Should().BeEmpty();
    }

    [Fact]
    public void Reorient_authorizes_the_current_revision_and_replaces_accepted_constraints()
    {
        var state = TestSupport
            .State()
            .RecordPlannerDecision(
                new PlannerDecision(
                    PlannerDecisionValue.ProceedWithConstraints,
                    "Proceed carefully.",
                    ["Preserve the public contract."],
                    ["src/a.cs"],
                    "Implement through the existing seam."
                )
            )
            .RecordPlannerRequest(
                new AskPlannerRequest(
                    PlannerQuestionType.SessionReliability,
                    "Current session is unreliable.",
                    "How should a fresh Executor continue?",
                    "Restart from durable facts.",
                    ["src/a.cs"]
                ),
                DateTimeOffset.Parse("2026-08-11T15:00:00Z")
            );

        var reoriented = state.RecordPlannerDecision(
            new PlannerDecision(
                PlannerDecisionValue.Reorient,
                "The corrected approach is safe.",
                ["Use the existing seam."],
                ["src/a.cs"],
                "Continue from the corrected approach.",
                "Use the existing seam and rerun focused verification."
            )
        );

        reoriented.MutationAuthorized.Should().BeTrue();
        reoriented.ApprovedApproachRevision.Should().Be(reoriented.ApproachRevision);
        reoriented.PlannerConstraints.Should().Equal("Use the existing seam.");
    }

    [Fact]
    public void Reorient_does_not_authorize_without_a_session_reliability_request()
    {
        var state = TestSupport.State();

        var reoriented = state.RecordPlannerDecision(
            new PlannerDecision(
                PlannerDecisionValue.Reorient,
                "The corrected approach is safe.",
                [],
                ["src/a.cs"],
                "Continue from the corrected approach.",
                "Use the existing seam."
            )
        );

        reoriented.MutationAuthorized.Should().BeFalse();
    }

    [Fact]
    public void Every_checkpoint_revokes_mutation_authority()
    {
        var now = DateTimeOffset.Parse("2026-08-11T14:00:00Z");
        var authorized = TestSupport
            .State(now: now)
            .RecordPlannerDecision(
                new PlannerDecision(
                    PlannerDecisionValue.ProceedWithConstraints,
                    "The approach is safe.",
                    ["Preserve the accepted contract."],
                    ["src/a.cs"],
                    "Continue through the approved seam."
                )
            );

        var continuity = authorized.RecordCheckpoint(
            new WriteCheckpointRequest("Continuity", [], "Continue implementation."),
            now.AddMinutes(1)
        );

        continuity.MutationAuthorized.Should().BeFalse();
        continuity.PlannerConstraints.Should().Equal("Preserve the accepted contract.");
        continuity.ApproachRevision.Should().Be(authorized.ApproachRevision);

        var uncertain = authorized.RecordCheckpoint(
            new WriteCheckpointRequest(
                "The owner is unclear.",
                ["The correct integration owner is unresolved."],
                "Ask Planner to resolve the owner."
            ),
            now.AddMinutes(2)
        );

        uncertain.MutationAuthorized.Should().BeFalse();
        uncertain.PlannerConstraints.Should().Equal("Preserve the accepted contract.");
        uncertain.ApproachRevision.Should().Be(authorized.ApproachRevision);
    }

    [Fact]
    public void Creating_a_run_requires_verification()
    {
        var packet = TestSupport.Packet() with { Verification = [] };

        var act = () => CadenceState.Create(packet, "sha", "/workspace");

        act.Should().Throw<ArgumentException>().WithMessage("*verification command*");
    }

    [Fact]
    public void A_new_report_invalidates_candidate_verification_and_review()
    {
        var report = new SubmitReportRequest(
            "Implemented",
            [],
            new RegressionTestClaim(
                RegressionTestDisposition.ExistingCoverage,
                ["Existing verification covers the change."]
            )
        );
        var state = TestSupport.State() with
        {
            CandidateSha = "candidate",
            VerificationIndex = 1,
            VerificationResults =
            [
                new VerificationResult(0, "test", 0, "ok", "", TimeSpan.Zero, false),
            ],
            ReviewerDecision = new ReviewDecision(
                ReviewDecisionValue.Accept,
                TestSupport.Doctrine().Sha256,
                "Accepted",
                [
                    new ReviewOutcomeAssessment(
                        "outcome-1",
                        true,
                        [TestSupport.FileEvidence("src/a.cs")]
                    ),
                ],
                [],
                []
            ),
            ReviewerCandidateSha = "candidate",
        };

        state = state
            .RecordOutcomeUpdates(
                new UpdateOutcomesRequest([
                    new OutcomeUpdate(
                        "outcome-1",
                        OutcomeStatus.Complete,
                        ["src/a.cs changed"],
                        "Implemented.",
                        null
                    ),
                ])
            )
            .RecordImplementationReport(report);

        state.CandidateSha.Should().BeNull();
        state.VerificationResults.Should().BeEmpty();
        state.ReviewerDecision.Should().BeNull();
        state
            .OutcomeLedger.Should()
            .OnlyContain(outcome => outcome.Status == OutcomeStatus.Complete);
    }

    private static ReviewDecision Decision(ReviewDecisionValue value) =>
        new(
            value,
            TestSupport.Doctrine().Sha256,
            "A sufficiently detailed review decision.",
            [],
            [],
            [],
            value == ReviewDecisionValue.NeedsHuman ? "Which product behavior is required?" : null,
            value == ReviewDecisionValue.NeedsHuman ? HumanDecisionDomain.Product : null
        );
}
