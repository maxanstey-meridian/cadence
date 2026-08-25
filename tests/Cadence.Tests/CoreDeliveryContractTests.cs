using System.Text.Json;
using Cadence.Git;
using FluentAssertions;
using Tandem.Advanced;

namespace Cadence.Tests;

public sealed class CoreDeliveryContractTests
{
    [Fact]
    public void Delivery_capabilities_use_no_duplicate_summaries()
    {
        var dirty = new DirtyWorkCheckpointPolicy(new GitProcess(), TimeProvider.System);

        new AskPlannerCapability()
            .Summarize(new("Current work", "Question?", "Proposed approach", ["Evidence"]))
            .Should()
            .BeEmpty();
        new UpdateOutcomesCapability()
            .Summarize(new([new("outcome-1", OutcomeStatus.InProgress, "Evidence", "Next")]))
            .Should()
            .BeEmpty();
        new SubmitReportCapability(dirty)
            .Summarize(new("Summary", "Commit message", [], "Regression evidence"))
            .Should()
            .BeEmpty();
        new WriteCheckpointCapability()
            .Summarize(new("Detailed checkpoint", [], "Continue."))
            .Should()
            .BeEmpty();
        new ResetContextCapability()
            .Summarize(new("Detailed checkpoint", [], "Continue.", "Context is unreliable"))
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void Update_outcomes_preserves_untouched_progress_and_reopens_completed_work()
    {
        var packet = TestSupport.Packet() with
        {
            Outcomes = [new("one", "One"), new("two", "Two")],
        };
        var state = CadenceState.Create(packet, "base", "/workspace") with
        {
            OutcomeProgress =
            [
                new("one", OutcomeStatus.Complete, "Implemented one.", null),
                new("two", OutcomeStatus.Complete, "Implemented two.", null),
            ],
            CandidateSha = "candidate",
            VerificationIndex = 1,
            VerificationResults =
            [
                new VerificationResult(0, "test", "true", 0, "", "", TimeSpan.Zero, false),
            ],
            ReviewerDecision = TestContracts.Review(ReviewDecisionValue.Accept, "Accepted", []),
            ReviewRepairRequired = true,
        };

        var updated = state.RecordOutcomeUpdates(
            new([new("one", OutcomeStatus.InProgress, "Repairing one.", "Finish repair.")])
        );

        updated
            .OutcomeProgress.Single(x => x.OutcomeId == "two")
            .Status.Should()
            .Be(OutcomeStatus.Complete);
        updated
            .OutcomeProgress.Single(x => x.OutcomeId == "one")
            .Status.Should()
            .Be(OutcomeStatus.InProgress);
        updated.CandidateSha.Should().BeNull();
        updated.VerificationResults.Should().BeEmpty();
        updated.ReviewerDecision.Should().BeNull();
        updated.ReviewRepairRequired.Should().BeFalse();
    }

    [Fact]
    public void Submit_report_rejects_incomplete_and_non_exact_obligation_claims()
    {
        var packet = TestSupport.Packet() with
        {
            Acceptance = [new("accept", "outcome-1", "Accepted")],
            Constraints = [new("constraint", "Constraint requirement")],
        };
        var state = CadenceState.Create(packet, "base", "/workspace");
        var request = TestContracts.Report(
            "Done",
            "feature",
            [
                new("acceptance:accept", "Evidence"),
                new("acceptance:accept", "Duplicate"),
                new("unknown", "Evidence"),
            ],
            "Regression test added."
        );

        var result = new SubmitReportRequestValidator(state).Validate(request);

        result.IsValid.Should().BeFalse();
        result.Errors.Select(x => x.ErrorMessage).Should().Contain(x => x.Contains("complete"));
        result.Errors.Select(x => x.ErrorMessage).Should().Contain(x => x.Contains("Duplicate"));
        result.Errors.Select(x => x.ErrorMessage).Should().Contain(x => x.Contains("Unknown"));
        result.Errors.Select(x => x.ErrorMessage).Should().Contain(x => x.Contains("Missing"));
    }

    [Fact]
    public void Reviewer_accept_rejects_missing_duplicate_unknown_and_unsatisfied_assessments()
    {
        var state = CompleteVerifiedState();
        var expected = DeliveryObligations.From(state).Select(x => x.Reference).ToArray();
        IReadOnlyList<ReviewAssessment>[] invalid =
        [
            expected.Skip(1).Select(x => new ReviewAssessment(x, true, "Evidence")).ToArray(),
            expected
                .Select(x => new ReviewAssessment(x, true, "Evidence"))
                .Append(new(expected[0], true, "Duplicate"))
                .ToArray(),
            expected
                .Select(x => new ReviewAssessment(x, true, "Evidence"))
                .Append(new("unknown", true, "Evidence"))
                .ToArray(),
            expected.Select(x => new ReviewAssessment(x, x != expected[0], "Evidence")).ToArray(),
        ];

        invalid
            .Should()
            .OnlyContain(x => ReviewProblems(state, ReviewDecisionValue.Accept, x).Count > 0);
        ReviewProblems(
                state,
                ReviewDecisionValue.Accept,
                expected.Select(x => new ReviewAssessment(x, true, "Evidence")).ToArray()
            )
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void Reviewer_request_changes_requires_complete_evidenced_but_not_satisfied_coverage()
    {
        var state = CompleteVerifiedState();
        var expected = DeliveryObligations.From(state).Select(x => x.Reference).ToArray();

        ReviewProblems(
                state,
                ReviewDecisionValue.RequestChanges,
                expected
                    .Select((x, index) => new ReviewAssessment(x, index != 0, "Evidence"))
                    .ToArray()
            )
            .Should()
            .BeEmpty();
        ReviewProblems(
                state,
                ReviewDecisionValue.RequestChanges,
                expected.Skip(1).Select(x => new ReviewAssessment(x, true, "Evidence")).ToArray()
            )
            .Should()
            .NotBeEmpty();
    }

    [Fact]
    public void Delivery_evidence_rejects_whitespace_only_text()
    {
        new OutcomeProgressValidator()
            .Validate(new OutcomeProgress("outcome-1", OutcomeStatus.InProgress, " ", " "))
            .IsValid.Should()
            .BeFalse();
        new OutcomeProgressValidator()
            .Validate(new OutcomeProgress("outcome-1", OutcomeStatus.NotStarted, " ", "Next."))
            .IsValid.Should()
            .BeFalse();
        new ObligationClaimValidator()
            .Validate(new ObligationClaim("acceptance:accept", " "))
            .IsValid.Should()
            .BeFalse();
        new SubmitReportRequestValidator()
            .Validate(TestContracts.Report("Done", "report", [], " "))
            .IsValid.Should()
            .BeFalse();
        new WriteCheckpointRequestValidator()
            .Validate(new WriteCheckpointRequest(" ", [" "], " "))
            .IsValid.Should()
            .BeFalse();
        new ResetContextRequestValidator()
            .Validate(new ResetContextRequest(" ", [" "], " ", " "))
            .IsValid.Should()
            .BeFalse();
    }

    [Fact]
    public void Material_repair_progress_is_required_before_resubmission()
    {
        var state = TestSupport.State() with
        {
            OutcomeProgress = [new("outcome-1", OutcomeStatus.Complete, "Done.", null)],
            ReviewRepairRequired = true,
        };
        var report = TestContracts.Report("Done", "repair", [], "Tests pass.");

        new SubmitReportRequestValidator(state).Validate(report).IsValid.Should().BeFalse();
        var unchanged = state.RecordOutcomeUpdates(new([state.OutcomeProgress[0]]));
        unchanged.ReviewRepairRequired.Should().BeTrue();
        var changed = state.RecordOutcomeUpdates(
            new([state.OutcomeProgress[0] with { Evidence = "Repair completed." }])
        );
        new SubmitReportRequestValidator(changed).Validate(report).IsValid.Should().BeTrue();
    }

    private static CadenceState CompleteVerifiedState()
    {
        var state = TestSupport.State();
        return state with
        {
            VerificationIndex = state.Packet.Verification.Count,
            VerificationResults = state
                .Packet.Verification.Select(
                    (command, index) =>
                        new VerificationResult(
                            index,
                            command.Label,
                            command.Command,
                            0,
                            "ok",
                            "",
                            TimeSpan.Zero,
                            false
                        )
                )
                .ToArray(),
        };
    }

    private static IReadOnlyList<StructuredOutputProblem> ReviewProblems(
        CadenceState state,
        ReviewDecisionValue decision,
        IReadOnlyList<ReviewAssessment> assessments
    ) =>
        ReviewerPolicies.ContractComplete()(
            new OutputAcceptanceObservation<CadenceState, ReviewDecision>(
                new(Guid.NewGuid(), state, null),
                "review",
                TestContracts.Review(decision, "Review", assessments, []),
                new HashSet<ToolObservation>
                {
                    new("read_file", ToolEffect.Read, ToolEvidence.RepositoryInspection),
                },
                [],
                1
            )
        );

    [Fact]
    public void Obligation_catalog_is_namespaced_deterministic_and_derived()
    {
        var packet = TestSupport.Packet() with
        {
            Outcomes = [new("shared", "Outcome")],
            Acceptance = [new("shared", "shared", "Acceptance")],
            Constraints = [new("shared", "Packet constraint")],
        };
        var state = CadenceState.Create(packet, "base", "/workspace") with
        {
            PlannerConstraints = [new("shared", "Planner constraint")],
            LatestCheckpoint = new("Not an obligation", [], "Continue"),
            ActiveReviewFindings = [new(ReviewFindingSeverity.High, "Not an obligation", "file:1")],
        };

        DeliveryObligations
            .From(state)
            .Should()
            .Equal(
                new DeliveryObligation(
                    "outcome:shared",
                    DeliveryObligationKind.Outcome,
                    "shared",
                    "Outcome"
                ),
                new DeliveryObligation(
                    "acceptance:shared",
                    DeliveryObligationKind.AcceptanceCriterion,
                    "shared",
                    "Acceptance",
                    "shared"
                ),
                new DeliveryObligation(
                    "packet-constraint:shared",
                    DeliveryObligationKind.PacketConstraint,
                    "shared",
                    "Packet constraint"
                ),
                new DeliveryObligation(
                    "planner-constraint:shared",
                    DeliveryObligationKind.PlannerConstraint,
                    "shared",
                    "Planner constraint"
                )
            );
        JsonSerializer.Serialize(state).Should().NotContain("deliveryObligations");
    }

    [Fact]
    public void Planner_constraints_are_typed_validated_and_retained_by_non_authorizing_decisions()
    {
        var validator = new PlannerDecisionValidator();
        var proceed = new PlannerDecision(
            PlannerDecisionValue.Proceed,
            "Grounded rationale",
            [new("stable-id", "Preserve behavior")],
            ["src/file.cs: fact"],
            "Continue delivery"
        );
        validator.Validate(proceed).IsValid.Should().BeTrue();
        validator
            .Validate(
                proceed with
                {
                    Constraints = [new("stable-id", "One"), new("stable-id", "Two")],
                }
            )
            .IsValid.Should()
            .BeFalse();
        validator
            .Validate(proceed with { Constraints = [new(" ", "Requirement")] })
            .IsValid.Should()
            .BeFalse();
        validator
            .Validate(proceed with { Constraints = [new("stable-id", "todo")] })
            .IsValid.Should()
            .BeFalse();

        var state = TestSupport.State().RecordPlannerDecision(proceed);
        var revise = proceed with
        {
            Decision = PlannerDecisionValue.ReviseApproach,
            Constraints = [],
            CorrectedApproach = "Use the corrected approach",
        };
        state.RecordPlannerDecision(revise).PlannerConstraints.Should().Equal(proceed.Constraints);
        validator
            .Validate(revise with { Constraints = [new("forbidden", "Forbidden")] })
            .IsValid.Should()
            .BeFalse();
    }

    [Fact]
    public void State_initializes_continuity_from_the_injected_clock()
    {
        var expected = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
        var state = CadenceState.Create(
            TestSupport.Packet(),
            "base",
            "/workspace",
            timeProvider: new FixedTimeProvider(expected)
        );
        state.LastContinuityAt.Should().Be(expected);
    }

    [Fact]
    public void Reviewer_contract_enforces_material_findings_and_human_boundary()
    {
        var validator = new ReviewDecisionValidator();
        validator
            .Validate(TestContracts.Review(ReviewDecisionValue.RequestChanges, "Change", []))
            .IsValid.Should()
            .BeFalse();
        validator
            .Validate(
                TestContracts.Review(
                    ReviewDecisionValue.Accept,
                    "Accept",
                    [new(ReviewFindingSeverity.High, "Defect", "file:1")]
                )
            )
            .IsValid.Should()
            .BeFalse();
        validator
            .Validate(
                TestContracts.Review(
                    ReviewDecisionValue.NeedsHuman,
                    "Decision needed",
                    [],
                    "Choose policy",
                    HumanDecisionDomain.Product
                )
            )
            .IsValid.Should()
            .BeTrue();
    }

    [Fact]
    public void Planner_authorization_is_explicit_and_closes_on_new_request()
    {
        var state = TestSupport.State();
        state.MutationAuthorized.Should().BeFalse();
        state = state.RecordPlannerDecision(
            new PlannerDecision(
                PlannerDecisionValue.Proceed,
                "Sound",
                [new("keep-api", "Keep API")],
                ["source"],
                "Implement"
            )
        );
        state.MutationAuthorized.Should().BeTrue();
        state.PlannerConstraints.Should().Contain(new PlannerConstraint("keep-api", "Keep API"));
        state = state.RecordPlannerRequest(new AskPlannerRequest("repair", "How?", "Approach", []));
        state.MutationAuthorized.Should().BeFalse();
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
