using FluentAssertions;

namespace Cadence.Tests;

public sealed class ReportValidationTests
{
    [Fact]
    public async Task Report_rejects_an_incomplete_authoritative_ledger()
    {
        var validator = new SubmitReportRequestValidator(TestSupport.State());

        var result = await validator.ValidateAsync(Report(), TestContext.Current.CancellationToken);

        result
            .Errors.Should()
            .Contain(error => error.ErrorCode == "submit_report.outcomes.incomplete");
    }

    [Fact]
    public async Task Report_is_rejected_while_a_continuity_checkpoint_is_required()
    {
        var validator = new SubmitReportRequestValidator(
            CompleteState(),
            continuityCheckpointRequired: true
        );

        var result = await validator.ValidateAsync(Report(), TestContext.Current.CancellationToken);

        result.Errors.Should().Contain(error => error.PropertyName == "continuityCheckpoint");
    }

    [Fact]
    public async Task Report_consumes_a_complete_ledger_and_requires_planner_constraint_evidence()
    {
        var state = CompleteState() with { PlannerConstraints = ["Preserve compatibility"] };
        var validator = new SubmitReportRequestValidator(state);
        var invalid = Report();
        var valid = invalid with
        {
            AddressedConstraints =
            [
                new ConstraintClaim("Preserve compatibility", "tests/a.cs proves compatibility"),
            ],
        };

        (await validator.ValidateAsync(invalid, TestContext.Current.CancellationToken))
            .Errors.Should()
            .Contain(error => error.ErrorCode == "submit_report.addressed_constraints.missing");
        (await validator.ValidateAsync(valid, TestContext.Current.CancellationToken))
            .IsValid.Should()
            .BeTrue();
    }

    [Fact]
    public async Task Report_requires_every_packet_and_planner_constraint_exactly_once()
    {
        var state = CompleteState() with
        {
            Packet = TestSupport.Packet() with { Constraints = ["Packet obligation"] },
            PlannerConstraints = ["Planner obligation", "Packet obligation"],
        };
        var validator = new SubmitReportRequestValidator(state);
        var valid = Report() with
        {
            AddressedConstraints =
            [
                new ConstraintClaim("Packet obligation", "Packet evidence"),
                new ConstraintClaim("Planner obligation", "Planner evidence"),
            ],
        };

        (await validator.ValidateAsync(valid, TestContext.Current.CancellationToken))
            .IsValid.Should()
            .BeTrue();
        (
            await validator.ValidateAsync(
                valid with
                {
                    AddressedConstraints = [valid.AddressedConstraints[0]],
                },
                TestContext.Current.CancellationToken
            )
        )
            .Errors.Should()
            .Contain(error => error.ErrorCode == "submit_report.addressed_constraints.missing");
        (
            await validator.ValidateAsync(
                valid with
                {
                    AddressedConstraints =
                    [
                        valid.AddressedConstraints[0],
                        valid.AddressedConstraints[0],
                        new ConstraintClaim("Unknown", "evidence"),
                    ],
                },
                TestContext.Current.CancellationToken
            )
        )
            .Errors.Select(error => error.ErrorCode)
            .Should()
            .Contain([
                "submit_report.addressed_constraints.duplicate",
                "submit_report.addressed_constraints.unknown",
            ]);
    }

    [Fact]
    public async Task Direct_and_no_op_repair_reports_are_rejected_until_material_update()
    {
        var complete = CompleteState();
        var requestChanges = new ReviewDecision(
            ReviewDecisionValue.RequestChanges,
            TestSupport.Doctrine().Sha256,
            "Repair required.",
            [],
            [],
            []
        );
        var reviewed = complete.RecordReviewDecision(requestChanges);
        var noOp = reviewed.RecordOutcomeUpdates(
            new UpdateOutcomesRequest([
                new OutcomeUpdate(
                    "outcome-1",
                    OutcomeStatus.Complete,
                    ["src/a.cs: implementation"],
                    "The requested behavior is implemented.",
                    null
                ),
            ])
        );
        var repaired = reviewed.RecordOutcomeUpdates(
            new UpdateOutcomesRequest([
                new OutcomeUpdate(
                    "outcome-1",
                    OutcomeStatus.Complete,
                    ["src/a.cs: repaired implementation"],
                    "The review defect is repaired.",
                    null
                ),
            ])
        );

        (
            await new SubmitReportRequestValidator(reviewed).ValidateAsync(
                Report(),
                TestContext.Current.CancellationToken
            )
        )
            .Errors.Should()
            .Contain(error => error.ErrorCode == "submit_report.review_repair.required");
        (
            await new SubmitReportRequestValidator(noOp).ValidateAsync(
                Report(),
                TestContext.Current.CancellationToken
            )
        )
            .Errors.Should()
            .Contain(error => error.ErrorCode == "submit_report.review_repair.required");
        (
            await new SubmitReportRequestValidator(repaired).ValidateAsync(
                Report(),
                TestContext.Current.CancellationToken
            )
        )
            .IsValid.Should()
            .BeTrue();
    }

    private static CadenceState CompleteState() =>
        TestSupport
            .State()
            .RecordOutcomeUpdates(
                new UpdateOutcomesRequest([
                    new OutcomeUpdate(
                        "outcome-1",
                        OutcomeStatus.Complete,
                        ["src/a.cs: implementation"],
                        "The requested behavior is implemented.",
                        null
                    ),
                ])
            );

    private static SubmitReportRequest Report() =>
        new(
            "Implemented",
            [],
            new RegressionTestClaim(
                RegressionTestDisposition.ExistingCoverage,
                ["tests/a.cs covers the regression."]
            )
        );
}
