using FluentAssertions;

namespace Cadence.Tests;

public sealed class ReviewerSliceTests
{
    [Fact]
    public void Reviewer_prompt_injects_exact_doctrine_identity_and_operational_audits()
    {
        var doctrine = TestSupport.Doctrine();
        var prompt = ReviewerPrompts.BuildInstructions(doctrine);
        var message = ReviewerPrompts.BuildMessage(TestSupport.State(), doctrine);

        prompt.Should().Contain(doctrine.Content);
        prompt.Should().Contain(doctrine.Source).And.Contain(doctrine.Sha256);
        prompt.Should().Contain("requirement sanity");
        prompt.Should().Contain("downstream coherence");
        prompt.Should().Contain("Green verification is necessary").And.Contain("insufficient");
        prompt.Should().Contain("every added or changed test");
        prompt.Should().Contain("every new branch and error path");
        prompt.Should().Contain("mock soup");
        prompt.Should().Contain("fake integration coverage");
        message.Should().Contain("run_verification_1");
        message.Should().Contain("following pagination");
    }

    [Fact]
    public async Task Reviewer_decision_rejects_stale_doctrine_and_non_reproducible_references()
    {
        var decision = Accepted() with
        {
            DoctrineHash = "stale",
            Outcomes =
            [
                new ReviewOutcomeAssessment(
                    "outcome-1",
                    true,
                    [
                        new ReviewEvidenceReference(
                            ReviewEvidenceKind.PacketOutcome,
                            OutcomeId: "unknown"
                        ),
                    ]
                ),
            ],
        };
        var validator = Validator();

        var result = await validator.ValidateAsync(decision, TestContext.Current.CancellationToken);

        result.Errors.Should().Contain(x => x.ErrorCode == "review.doctrine_hash.mismatch");
        result.Errors.Should().Contain(x => x.ErrorCode == "review.evidence.invalid");
    }

    [Fact]
    public async Task Accept_permits_non_blocking_findings_but_rejects_high_and_critical()
    {
        var validator = Validator();
        var medium = Finding(ReviewFindingSeverity.Medium);
        var high = Finding(ReviewFindingSeverity.High);

        (
            await validator.ValidateAsync(
                Accepted() with
                {
                    Findings = [medium],
                },
                TestContext.Current.CancellationToken
            )
        )
            .IsValid.Should()
            .BeTrue();
        (
            await validator.ValidateAsync(
                Accepted() with
                {
                    Findings = [high],
                },
                TestContext.Current.CancellationToken
            )
        )
            .Errors.Should()
            .Contain(x => x.ErrorCode == "review.findings.blocker_forbidden_for_accept");
    }

    [Fact]
    public async Task Request_changes_requires_a_critical_or_high_finding()
    {
        var validator = Validator();
        var requestChanges = Accepted() with
        {
            Decision = ReviewDecisionValue.RequestChanges,
            Outcomes =
            [
                new ReviewOutcomeAssessment(
                    "outcome-1",
                    false,
                    [TestSupport.FileEvidence("feature.cs")]
                ),
            ],
        };

        (
            await validator.ValidateAsync(
                requestChanges with
                {
                    Findings = [Finding(ReviewFindingSeverity.Low)],
                },
                TestContext.Current.CancellationToken
            )
        )
            .Errors.Should()
            .Contain(x => x.ErrorCode == "review.findings.blocker_required_for_changes");
        (
            await validator.ValidateAsync(
                requestChanges with
                {
                    Findings = [Finding(ReviewFindingSeverity.High)],
                },
                TestContext.Current.CancellationToken
            )
        )
            .IsValid.Should()
            .BeTrue();
    }

    [Fact]
    public async Task Verification_reference_must_reproduce_the_current_exact_result()
    {
        var exact = new ReviewEvidenceReference(
            ReviewEvidenceKind.VerificationCommand,
            Command: "dotnet test",
            ExitCode: 0,
            Stdout: "passed",
            Stderr: ""
        );
        var decision = Accepted() with
        {
            Outcomes = [new ReviewOutcomeAssessment("outcome-1", true, [exact])],
        };

        (await Validator().ValidateAsync(decision, TestContext.Current.CancellationToken))
            .IsValid.Should()
            .BeTrue();
        var stale = exact with { Stdout = "claimed" };
        (
            await Validator()
                .ValidateAsync(
                    decision with
                    {
                        Outcomes = [new ReviewOutcomeAssessment("outcome-1", true, [stale])],
                    },
                    TestContext.Current.CancellationToken
                )
        )
            .Errors.Should()
            .Contain(x => x.ErrorCode == "review.evidence.invalid");
    }

    [Fact]
    public async Task Red_reviewer_result_is_model_evidence_for_an_exact_packet_command()
    {
        var red = new ReviewEvidenceReference(
            ReviewEvidenceKind.VerificationCommand,
            Command: "dotnet test",
            ExitCode: 7,
            Stdout: "one test failed",
            Stderr: "process exited 7"
        );
        var decision = Accepted() with
        {
            Decision = ReviewDecisionValue.RequestChanges,
            Findings =
            [
                new ReviewFinding(
                    ReviewFindingSeverity.High,
                    "The packet verification rerun is red.",
                    [TestSupport.DoctrineEvidence(), red]
                ),
            ],
        };

        (await Validator().ValidateAsync(decision, TestContext.Current.CancellationToken))
            .IsValid.Should()
            .BeTrue();
        (
            await Validator()
                .ValidateAsync(
                    decision with
                    {
                        Findings =
                        [
                            decision.Findings[0] with
                            {
                                Evidence =
                                [
                                    TestSupport.DoctrineEvidence(),
                                    red with
                                    {
                                        Stdout = "",
                                        Stderr = "",
                                    },
                                ],
                            },
                        ],
                    },
                    TestContext.Current.CancellationToken
                )
        )
            .IsValid.Should()
            .BeTrue();
        (
            await Validator()
                .ValidateAsync(
                    decision with
                    {
                        Findings =
                        [
                            decision.Findings[0] with
                            {
                                Evidence =
                                [
                                    TestSupport.DoctrineEvidence(),
                                    red with
                                    {
                                        Command = "other",
                                    },
                                ],
                            },
                        ],
                    },
                    TestContext.Current.CancellationToken
                )
        )
            .Errors.Should()
            .Contain(error => error.ErrorCode == "review.evidence.invalid");
    }

    private static ReviewDecisionValidator Validator() =>
        new(
            TestSupport.Doctrine(),
            ["outcome-1"],
            [],
            [new VerificationResult(0, "dotnet test", 0, "passed", "", TimeSpan.Zero, false)]
        );

    private static ReviewDecision Accepted() =>
        new(
            ReviewDecisionValue.Accept,
            TestSupport.Doctrine().Sha256,
            "The candidate delivers the outcome.",
            [
                new ReviewOutcomeAssessment(
                    "outcome-1",
                    true,
                    [TestSupport.FileEvidence("feature.cs")]
                ),
            ],
            [],
            []
        );

    private static ReviewFinding Finding(ReviewFindingSeverity severity) =>
        new(
            severity,
            "The exact defect is reproducible.",
            [TestSupport.DoctrineEvidence(), TestSupport.FileEvidence("feature.cs")]
        );
}
