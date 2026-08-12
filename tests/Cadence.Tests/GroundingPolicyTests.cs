using System.Text.Json;
using FluentAssertions;
using Tandem.Advanced;

namespace Cadence.Tests;

public sealed class GroundingPolicyTests
{
    private const string BaseSha = "1111111111111111111111111111111111111111";
    private const string CandidateSha = "2222222222222222222222222222222222222222";

    [Theory]
    [InlineData(PlannerDecisionValue.Proceed)]
    [InlineData(PlannerDecisionValue.Stop)]
    public void Planner_non_human_decisions_require_repository_inspection(
        PlannerDecisionValue value
    )
    {
        var decision = new PlannerDecision(
            value,
            "The repository evidence supports this decision.",
            [],
            ["src/a.cs"],
            "Implement through the inspected seam."
        );

        PlannerPolicies
            .RepositoryGrounded()(PlannerObservation(decision, new HashSet<ToolObservation>()))
            .Should()
            .ContainSingle();
        PlannerPolicies
            .RepositoryGrounded()(
                PlannerObservation(
                    decision,
                    new HashSet<ToolObservation>
                    {
                        new("read", ToolEffect.Read, ToolEvidence.RepositoryInspection),
                    }
                )
            )
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void Planner_needs_human_remains_exempt()
    {
        var decision = new PlannerDecision(
            PlannerDecisionValue.NeedsHuman,
            "Product intent is required.",
            [],
            ["packet outcome"],
            "Ask the Human which behavior is intended.",
            null,
            "Which behavior is intended?",
            HumanDecisionDomain.Product
        );

        PlannerPolicies
            .RepositoryGrounded()(PlannerObservation(decision, new HashSet<ToolObservation>()))
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void Reviewer_requires_exact_completed_git_range_in_manifest_then_repository_diff_order()
    {
        ReviewerProblems(
                Accepted(),
                [
                    Git(
                        "git_changed_files",
                        BaseSha,
                        "wrong",
                        status: ToolInvocationStatus.Completed
                    ),
                    Git("git_diff", BaseSha, CandidateSha, path: null),
                    Verification(1, 0),
                ]
            )
            .Should()
            .ContainSingle();
        ReviewerProblems(
                Accepted(),
                [
                    Git("git_diff", BaseSha, CandidateSha, path: null),
                    Git("git_changed_files", BaseSha, CandidateSha),
                    Verification(1, 0),
                ]
            )
            .Should()
            .ContainSingle();
        ReviewerProblems(
                Accepted(),
                [
                    Git("git_changed_files", BaseSha, CandidateSha),
                    Git("git_diff", BaseSha, CandidateSha, path: "src/a.cs"),
                    Verification(1, 0),
                ]
            )
            .Should()
            .ContainSingle();
        ReviewerProblems(Accepted(), Grounded(Verification(1, 0))).Should().BeEmpty();
    }

    [Theory]
    [InlineData(ToolInvocationStatus.Failed)]
    [InlineData(ToolInvocationStatus.Blocked)]
    [InlineData(ToolInvocationStatus.Faulted)]
    public void Reviewer_git_grounding_requires_completed_read_status(ToolInvocationStatus status)
    {
        ReviewerProblems(
                Accepted(),
                [
                    Git("git_changed_files", BaseSha, CandidateSha, status: status),
                    Git("git_diff", BaseSha, CandidateSha, path: null),
                    Verification(1, 0),
                ]
            )
            .Should()
            .ContainSingle();
    }

    [Fact]
    public void Accept_requires_latest_attempts_after_git_grounding_in_packet_order()
    {
        ReviewerProblems(
                Accepted(),
                Grounded(Verification(2, 0), Verification(1, 0)),
                ["first", "second"]
            )
            .Should()
            .ContainSingle();
        ReviewerProblems(
                Accepted(),
                [Verification(1, 0), .. Grounded(Verification(1, 0), Verification(2, 0))],
                ["first", "second"]
            )
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void Latest_verification_attempt_determines_acceptance()
    {
        ReviewerProblems(
                Accepted(),
                Grounded(Verification(1, 1, ToolInvocationStatus.Failed), Verification(1, 0))
            )
            .Should()
            .BeEmpty();
        ReviewerProblems(
                Accepted(),
                Grounded(Verification(1, 0), Verification(1, 1, ToolInvocationStatus.Failed))
            )
            .Should()
            .ContainSingle();
        ReviewerProblems(Accepted(), Grounded(Verification(1, 0, timedOut: true)))
            .Should()
            .ContainSingle();
        ReviewerProblems(Accepted(), Grounded(Verification(1, 0, truncated: true)))
            .Should()
            .ContainSingle()
            .Which.Message.Should()
            .Contain("complete");
    }

    [Fact]
    public void Latest_git_invocations_are_authoritative_and_ordered()
    {
        ReviewerProblems(
                Accepted(),
                [.. Grounded(), Git("git_changed_files", BaseSha, "wrong"), Verification(1, 0)]
            )
            .Should()
            .ContainSingle();
        ReviewerProblems(
                Accepted(),
                [
                    Git("git_changed_files", BaseSha, CandidateSha),
                    Git("git_diff", BaseSha, CandidateSha, path: "src/a.cs"),
                    Verification(1, 0),
                ]
            )
            .Should()
            .ContainSingle();
        ReviewerProblems(
                Accepted(),
                [
                    Git("git_diff", BaseSha, CandidateSha),
                    Git("git_changed_files", BaseSha, CandidateSha),
                    Verification(1, 0),
                ]
            )
            .Should()
            .ContainSingle();
    }

    [Fact]
    public void Path_inspection_after_repository_diff_preserves_git_grounding()
    {
        ReviewerProblems(
                Accepted(),
                [
                    Git("git_changed_files", BaseSha, CandidateSha),
                    Git("git_diff", BaseSha, CandidateSha),
                    Git("git_diff", BaseSha, CandidateSha, path: "src/a.cs"),
                    Verification(1, 0),
                ]
            )
            .Should()
            .BeEmpty();
    }

    [Fact]
    public void Every_verification_evidence_reference_is_authenticated()
    {
        var fabricated = VerificationEvidence("dotnet test", 0, "fabricated", "");
        var accepted = Accepted() with
        {
            Outcomes = [new ReviewOutcomeAssessment("outcome-1", true, [fabricated])],
        };
        ReviewerProblems(accepted, Grounded(Verification(1, 0))).Should().ContainSingle();

        var medium = accepted with
        {
            Outcomes = Accepted().Outcomes,
            Findings = [Finding(ReviewFindingSeverity.Medium, fabricated)],
        };
        ReviewerProblems(medium, Grounded(Verification(1, 0))).Should().ContainSingle();

        var low = medium with { Findings = [Finding(ReviewFindingSeverity.Low, fabricated)] };
        ReviewerProblems(low, Grounded(Verification(1, 0))).Should().ContainSingle();

        var unrelatedHigh = RequestChanges("dotnet test", 7, "failed", "stderr") with
        {
            Findings =
            [
                Finding(
                    ReviewFindingSeverity.High,
                    VerificationEvidence("dotnet test", 7, "failed", "stderr")
                ),
                Finding(ReviewFindingSeverity.High, fabricated),
            ],
        };
        ReviewerProblems(
                unrelatedHigh,
                Grounded(Verification(1, 7, ToolInvocationStatus.Failed, "failed", "stderr"))
            )
            .Should()
            .ContainSingle();
    }

    [Fact]
    public void Runtime_evidence_must_match_the_latest_verification_attempt()
    {
        var stale = VerificationEvidence("dotnet test", 0, "stale", "");
        var decision = Accepted() with
        {
            Outcomes = [new ReviewOutcomeAssessment("outcome-1", true, [stale])],
        };

        ReviewerProblems(
                decision,
                Grounded(Verification(1, 0, stdout: "stale"), Verification(1, 0, stdout: "current"))
            )
            .Should()
            .ContainSingle();
    }

    [Fact]
    public void Request_changes_requires_first_runtime_failure_and_exact_red_evidence()
    {
        var exact = RequestChanges("dotnet test", 7, "failed", "stderr");
        ReviewerProblems(
                exact,
                Grounded(Verification(1, 7, ToolInvocationStatus.Failed, "failed", "stderr"))
            )
            .Should()
            .BeEmpty();
        ReviewerProblems(
                RequestChanges("dotnet test", 7, "claimed", "stderr"),
                Grounded(Verification(1, 7, ToolInvocationStatus.Failed, "failed", "stderr"))
            )
            .Should()
            .ContainSingle();
        ReviewerProblems(
                exact,
                Grounded(Verification(1, 7, ToolInvocationStatus.Blocked, "failed", "stderr"))
            )
            .Should()
            .ContainSingle();
    }

    [Fact]
    public void Needs_human_remains_exempt_from_runtime_grounding()
    {
        var decision = Accepted() with
        {
            Decision = ReviewDecisionValue.NeedsHuman,
            HumanQuestion = "Which product behavior is intended?",
            HumanDecisionDomain = HumanDecisionDomain.Product,
        };

        ReviewerProblems(decision, []).Should().BeEmpty();
    }

    private static OutputAcceptanceObservation<CadenceState, PlannerDecision> PlannerObservation(
        PlannerDecision decision,
        IReadOnlySet<ToolObservation> tools
    ) =>
        new(
            new AgentMessageContext<CadenceState>(Guid.NewGuid(), State(), null),
            "decision",
            decision,
            tools,
            [],
            0
        );

    private static IReadOnlyList<StructuredOutputProblem> ReviewerProblems(
        ReviewDecision decision,
        IReadOnlyList<ToolInvocationObservation> invocations,
        IReadOnlyList<string>? commands = null
    )
    {
        var state = State(commands);
        return ReviewerPolicies.RepositoryGrounded()(
            new OutputAcceptanceObservation<CadenceState, ReviewDecision>(
                new AgentMessageContext<CadenceState>(Guid.NewGuid(), state, null),
                "decision",
                decision,
                new HashSet<ToolObservation>(),
                invocations,
                0
            )
        );
    }

    private static CadenceState State(IReadOnlyList<string>? commands = null) =>
        TestSupport.State() with
        {
            PinnedBaseSha = BaseSha,
            CandidateSha = CandidateSha,
            Packet = TestSupport.Packet() with { Verification = commands ?? ["dotnet test"] },
        };

    private static ToolInvocationObservation Git(
        string name,
        string baseSha,
        string candidateSha,
        string? path = null,
        ToolInvocationStatus status = ToolInvocationStatus.Completed
    ) =>
        new(
            name,
            ToolEffect.Read,
            JsonSerializer.SerializeToElement(
                name == "git_diff"
                    ? new
                    {
                        baseSha,
                        candidateSha,
                        path,
                        startLine = 1,
                    }
                    : (object)
                        new
                        {
                            baseSha,
                            candidateSha,
                            startLine = 1,
                        }
            ),
            status,
            null
        );

    private static ToolInvocationObservation Verification(
        int number,
        int exitCode,
        ToolInvocationStatus status = ToolInvocationStatus.Completed,
        string stdout = "passed",
        string stderr = "",
        bool timedOut = false,
        bool truncated = false
    ) =>
        new(
            $"run_verification_{number}",
            ToolEffect.ProcessExecution,
            JsonSerializer.SerializeToElement(new { }),
            status,
            new ToolResultEvidence.Process(
                exitCode,
                stdout,
                stderr,
                TimeSpan.Zero,
                timedOut,
                truncated
            )
        );

    private static ToolInvocationObservation[] Grounded(
        params ToolInvocationObservation[] verification
    ) =>
        [
            Git("git_changed_files", BaseSha, CandidateSha),
            Git("git_diff", BaseSha, CandidateSha, path: null),
            .. verification,
        ];

    private static ReviewDecision Accepted() =>
        new(
            ReviewDecisionValue.Accept,
            TestSupport.Doctrine().Sha256,
            "Delivered",
            [new ReviewOutcomeAssessment("outcome-1", true, [TestSupport.FileEvidence()])],
            [],
            []
        );

    private static ReviewDecision RequestChanges(
        string command,
        int exitCode,
        string stdout,
        string stderr
    ) =>
        Accepted() with
        {
            Decision = ReviewDecisionValue.RequestChanges,
            Findings =
            [
                new ReviewFinding(
                    ReviewFindingSeverity.High,
                    "The packet verification rerun fails.",
                    [
                        TestSupport.DoctrineEvidence(),
                        new ReviewEvidenceReference(
                            ReviewEvidenceKind.VerificationCommand,
                            Command: command,
                            ExitCode: exitCode,
                            Stdout: stdout,
                            Stderr: stderr
                        ),
                    ]
                ),
            ],
        };

    private static ReviewFinding Finding(
        ReviewFindingSeverity severity,
        ReviewEvidenceReference evidence
    ) => new(severity, "Verification evidence claim.", [TestSupport.DoctrineEvidence(), evidence]);

    private static ReviewEvidenceReference VerificationEvidence(
        string command,
        int exitCode,
        string stdout,
        string stderr
    ) =>
        new(
            ReviewEvidenceKind.VerificationCommand,
            Command: command,
            ExitCode: exitCode,
            Stdout: stdout,
            Stderr: stderr
        );
}
