using FluentAssertions;

namespace Cadence.Tests;

public sealed class AcceptanceContractTests
{
    [Fact]
    public void Packet_validation_enforces_criterion_identity_references_and_outcome_coverage()
    {
        var packet = TestSupport.Packet() with
        {
            Outcomes = [new("one", "One"), new("two", "Two")],
            Acceptance = [new(" criterion ", "one", "proof"), new("criterion", "missing", " ")],
        };

        var result = new PacketValidator().Validate(packet);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.ErrorMessage.Contains("unique"));
        result.Errors.Should().Contain(error => error.ErrorMessage.Contains("requirement"));
        result.Errors.Should().Contain(error => error.ErrorMessage.Contains("unknown outcome"));
        result.Errors.Should().Contain(error => error.ErrorMessage.Contains("Outcome 'two'"));
    }

    [Fact]
    public void Executor_report_requires_complete_exact_acceptance_claims()
    {
        var state = CadenceState.Create(
            TestSupport.Packet() with
            {
                Acceptance = [new("criterion", "outcome-1", "proof")],
            },
            "base",
            "/workspace"
        ) with
        {
            OutcomeLedger =
            [
                new(
                    "outcome-1",
                    "Deliver the feature",
                    OutcomeStatus.Complete,
                    ["done"],
                    "done",
                    null
                ),
            ],
        };
        var request = new SubmitReportRequest(
            "A meaningful completed implementation report",
            "Add feature implementation",
            [],
            new(RegressionTestDisposition.Added, ["A focused regression scenario"]),
            [new("criterion", "src/Feature.cs:12 proves the scenario")]
        );

        new SubmitReportRequestValidator(state).Validate(request).IsValid.Should().BeTrue();
        new SubmitReportRequestValidator(state)
            .Validate(request with { AcceptanceClaims = [] })
            .Errors.Should()
            .Contain(error => error.ErrorCode == "submit_report.acceptance_claims.missing");
    }

    [Fact]
    public void Satisfied_acceptance_requires_exact_identity_and_precise_implementation_evidence()
    {
        var doctrine = TestSupport.Doctrine();
        var criterion = new ReviewAcceptanceAssessment(
            "criterion",
            true,
            [new(ReviewEvidenceKind.AcceptanceCriterion, AcceptanceId: "criterion")]
        );
        var decision = new ReviewDecision(
            ReviewDecisionValue.Accept,
            doctrine.Sha256,
            "A meaningful review summary",
            [new("outcome-1", true, [TestSupport.FileEvidence()])],
            [],
            [],
            AcceptanceAssessments: [criterion]
        );
        var validator = new ReviewDecisionValidator(doctrine, ["outcome-1"], [], [], ["criterion"]);

        validator
            .Validate(decision)
            .Errors.Should()
            .Contain(error =>
                error.ErrorCode == "review.acceptance_assessments.implementation_evidence_required"
            );
        validator
            .Validate(
                decision with
                {
                    AcceptanceAssessments =
                    [
                        criterion with
                        {
                            Evidence = [.. criterion.Evidence, TestSupport.FileEvidence()],
                        },
                    ],
                }
            )
            .IsValid.Should()
            .BeTrue();
    }

    [Theory]
    [InlineData(null, "outcome-1")]
    [InlineData("criterion", null)]
    [InlineData(" ", "outcome-1")]
    [InlineData("criterion", " ")]
    public void Packet_validation_reports_blank_acceptance_fields_without_throwing(
        string? id,
        string? outcome
    )
    {
        var packet = TestSupport.Packet() with { Acceptance = [new(id!, outcome!, "proof")] };

        var act = () => new PacketValidator().Validate(packet);

        act.Should().NotThrow();
        act().IsValid.Should().BeFalse();
    }

    [Fact]
    public void Packet_validation_rejects_omitted_and_empty_acceptance()
    {
        new PacketValidator()
            .Validate(TestSupport.Packet())
            .Errors.Should()
            .Contain(error => error.ErrorMessage.Contains("at least one acceptance"));
        new PacketValidator()
            .Validate(TestSupport.Packet() with { Acceptance = [] })
            .IsValid.Should()
            .BeFalse();
    }

    [Fact]
    public void Executor_report_rejects_duplicate_unknown_and_blank_acceptance_claims()
    {
        var state = StateWithAcceptance();
        var request = CompleteReport([new("criterion", "specific evidence")]);
        var validator = new SubmitReportRequestValidator(state);

        validator
            .Validate(
                request with
                {
                    AcceptanceClaims = [.. request.AcceptanceClaims, request.AcceptanceClaims[0]],
                }
            )
            .Errors.Should()
            .Contain(error => error.ErrorCode == "submit_report.acceptance_claims.duplicate");
        validator
            .Validate(request with { AcceptanceClaims = [new("unknown", "specific evidence")] })
            .Errors.Should()
            .Contain(error => error.ErrorCode == "submit_report.acceptance_claims.unknown");
        validator
            .Validate(request with { AcceptanceClaims = [new("", "specific evidence")] })
            .Errors.Should()
            .Contain(error => error.ErrorCode == "acceptance_claim.id.required");
        validator
            .Validate(request with { AcceptanceClaims = [new("criterion", " ")] })
            .Errors.Should()
            .Contain(error => error.ErrorCode == "acceptance_claim.evidence.meaningful");
    }

    [Fact]
    public void Malformed_null_acceptance_claim_is_rejected_without_validator_exceptions()
    {
        var validator = new SubmitReportRequestValidator(StateWithAcceptance());
        var request = CompleteReport([null!]);

        var act = () => validator.Validate(request);

        act.Should().NotThrow();
        act().IsValid.Should().BeFalse();
        act()
            .Errors.Should()
            .Contain(error => error.ErrorCode == "submit_report.acceptance_claims.null_item");
    }

    [Fact]
    public void Reviewer_rejects_missing_duplicate_unknown_unsatisfied_and_wrong_typed_acceptance()
    {
        var validator = AcceptanceReviewValidator();
        var valid = AcceptedAssessment([
            new(ReviewEvidenceKind.AcceptanceCriterion, AcceptanceId: "criterion"),
            TestSupport.FileEvidence(),
        ]);

        validator
            .Validate(Review([]))
            .Errors.Should()
            .Contain(error => error.ErrorCode.EndsWith(".missing"));
        validator
            .Validate(Review([valid, valid]))
            .Errors.Should()
            .Contain(error => error.ErrorCode.EndsWith(".duplicate"));
        validator
            .Validate(Review([valid with { AcceptanceId = "unknown" }]))
            .Errors.Should()
            .Contain(error => error.ErrorCode.EndsWith(".unknown"));
        validator
            .Validate(Review([valid with { AcceptanceId = "" }]))
            .Errors.Should()
            .Contain(error => error.ErrorCode == "review.acceptance_assessment.id.meaningful");
        validator
            .Validate(Review([valid with { Satisfied = false }]))
            .Errors.Should()
            .Contain(error => error.ErrorCode.EndsWith("unsatisfied_for_accept"));
        validator
            .Validate(
                Review([
                    valid with
                    {
                        Evidence =
                        [
                            new(ReviewEvidenceKind.AcceptanceCriterion, AcceptanceId: "wrong"),
                            TestSupport.FileEvidence(),
                        ],
                    },
                ])
            )
            .Errors.Should()
            .Contain(error => error.ErrorCode.EndsWith("reference_required"));
        validator
            .Validate(Review([valid with { Evidence = [TestSupport.FileEvidence()] }]))
            .Errors.Should()
            .Contain(error => error.ErrorCode.EndsWith("reference_required"));
    }

    [Fact]
    public void Malformed_null_acceptance_assessments_are_rejected_without_validator_exceptions()
    {
        var validator = AcceptanceReviewValidator();
        var nullItem = Review([null!]);
        var nullEvidence = Review([new ReviewAcceptanceAssessment("criterion", true, null!)]);
        var nullEvidenceItem = Review([new ReviewAcceptanceAssessment("criterion", true, [null!])]);

        var itemAct = () => validator.Validate(nullItem);
        var evidenceAct = () => validator.Validate(nullEvidence);
        var evidenceItemAct = () => validator.Validate(nullEvidenceItem);

        itemAct.Should().NotThrow();
        itemAct().IsValid.Should().BeFalse();
        evidenceAct.Should().NotThrow();
        evidenceAct().IsValid.Should().BeFalse();
        evidenceItemAct.Should().NotThrow();
        evidenceItemAct().IsValid.Should().BeFalse();
    }

    [Fact]
    public void Aggregate_verification_alone_cannot_satisfy_acceptance_but_symbol_can()
    {
        var verification = new ReviewEvidenceReference(
            ReviewEvidenceKind.VerificationCommand,
            Command: "dotnet test",
            ExitCode: 0,
            Stdout: "green",
            Stderr: ""
        );
        var identity = new ReviewEvidenceReference(
            ReviewEvidenceKind.AcceptanceCriterion,
            AcceptanceId: "criterion"
        );
        var validator = AcceptanceReviewValidator([
            new(0, "dotnet test", 0, "green", "", TimeSpan.Zero, false),
        ]);

        validator
            .Validate(Review([AcceptedAssessment([identity, verification])]))
            .Errors.Should()
            .Contain(error => error.ErrorCode.EndsWith("implementation_evidence_required"));
        validator
            .Validate(
                Review([
                    AcceptedAssessment([
                        identity,
                        new(ReviewEvidenceKind.Symbol, Symbol: "ScenarioTest"),
                    ]),
                ])
            )
            .IsValid.Should()
            .BeTrue();
    }

    [Fact]
    public void Packet_resume_identity_includes_ordered_acceptance()
    {
        var method = typeof(Cadence.Host.Program).GetMethod(
            "PacketsMatch",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
        )!;
        var left = TestSupport.Packet() with { Acceptance = [new("one", "outcome-1", "proof")] };
        var right = left with { Acceptance = [new("two", "outcome-1", "proof")] };

        ((bool)method.Invoke(null, [left, left])!).Should().BeTrue();
        ((bool)method.Invoke(null, [left, right])!).Should().BeFalse();
    }

    [Fact]
    public void Historical_state_json_without_acceptance_fields_defaults_to_empty_without_fabrication()
    {
        var options = Tandem.TandemJson.CreateTypedContract();
        var node = System
            .Text.Json.Nodes.JsonNode.Parse(
                System.Text.Json.JsonSerializer.Serialize(TestSupport.State(), options)
            )!
            .AsObject();
        node["packet"]!.AsObject().Remove("acceptance");
        var review = new ReviewDecision(
            ReviewDecisionValue.Accept,
            TestSupport.Doctrine().Sha256,
            "Historical review",
            [],
            [],
            []
        );
        node["reviewerDecision"] = System.Text.Json.Nodes.JsonNode.Parse(
            System.Text.Json.JsonSerializer.Serialize(review, options)
        );
        node["reviewerDecision"]!.AsObject().Remove("acceptanceAssessments");

        var restored = System.Text.Json.JsonSerializer.Deserialize<CadenceState>(
            node.ToJsonString(),
            options
        )!;

        restored.Packet.Acceptance.Should().BeEmpty();
        restored.ReviewerDecision!.AcceptanceAssessments.Should().BeEmpty();
    }

    private static CadenceState StateWithAcceptance() =>
        CadenceState.Create(
            TestSupport.Packet() with
            {
                Acceptance = [new("criterion", "outcome-1", "proof")],
            },
            "base",
            "/workspace"
        ) with
        {
            OutcomeLedger =
            [
                new(
                    "outcome-1",
                    "Deliver the feature",
                    OutcomeStatus.Complete,
                    ["done"],
                    "done",
                    null
                ),
            ],
        };

    private static SubmitReportRequest CompleteReport(IReadOnlyList<AcceptanceClaim> claims) =>
        new(
            "A meaningful completed implementation report",
            "Add feature implementation",
            [],
            new(RegressionTestDisposition.Added, ["A focused regression scenario"]),
            claims
        );

    private static ReviewDecisionValidator AcceptanceReviewValidator(
        IReadOnlyList<VerificationResult>? results = null
    ) => new(TestSupport.Doctrine(), ["outcome-1"], [], results ?? [], ["criterion"]);

    private static ReviewAcceptanceAssessment AcceptedAssessment(
        IReadOnlyList<ReviewEvidenceReference> evidence
    ) => new("criterion", true, evidence);

    private static ReviewDecision Review(IReadOnlyList<ReviewAcceptanceAssessment> assessments) =>
        new(
            ReviewDecisionValue.Accept,
            TestSupport.Doctrine().Sha256,
            "A meaningful review summary",
            [new("outcome-1", true, [TestSupport.FileEvidence()])],
            [],
            [],
            AcceptanceAssessments: assessments
        );
}
