using FluentAssertions;
using Tandem.Advanced;

namespace Cadence.Tests;

public sealed class GroundingPolicyTests
{
    [Fact]
    public void Planner_cannot_approve_without_repository_inspection()
    {
        var decision = new PlannerDecision(
            PlannerDecisionValue.Proceed,
            "The approach is sound.",
            [],
            ["src/a.cs"],
            "Implement through the inspected seam."
        );
        var observation = new OutputAcceptanceObservation<CadenceState, PlannerDecision>(
            new AgentMessageContext<CadenceState>(Guid.NewGuid(), TestSupport.State(), null),
            "decision",
            decision,
            new HashSet<ToolObservation>(),
            0
        );

        var problems = PlannerPolicies.RepositoryGrounded()(observation);

        problems.Should().ContainSingle();
        problems[0].Message.Should().Contain("repository inspection");
    }

    [Fact]
    public void Planner_cannot_stop_without_repository_inspection()
    {
        var decision = new PlannerDecision(
            PlannerDecisionValue.Stop,
            "No safe action remains.",
            [],
            ["src/a.cs"],
            "Record why no safe action remains."
        );
        var observation = new OutputAcceptanceObservation<CadenceState, PlannerDecision>(
            new AgentMessageContext<CadenceState>(Guid.NewGuid(), TestSupport.State(), null),
            "decision",
            decision,
            new HashSet<ToolObservation>(),
            0
        );

        PlannerPolicies.RepositoryGrounded()(observation).Should().ContainSingle();
    }

    [Fact]
    public void Planner_needs_human_does_not_require_repository_inspection()
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
        var observation = new OutputAcceptanceObservation<CadenceState, PlannerDecision>(
            new AgentMessageContext<CadenceState>(Guid.NewGuid(), TestSupport.State(), null),
            "decision",
            decision,
            new HashSet<ToolObservation>(),
            0
        );

        PlannerPolicies.RepositoryGrounded()(observation).Should().BeEmpty();
    }

    [Fact]
    public void Planner_approval_is_accepted_after_repository_inspection()
    {
        var decision = new PlannerDecision(
            PlannerDecisionValue.Proceed,
            "The approach is sound.",
            [],
            ["src/a.cs"],
            "Implement through the inspected seam."
        );
        var observation = new OutputAcceptanceObservation<CadenceState, PlannerDecision>(
            new AgentMessageContext<CadenceState>(Guid.NewGuid(), TestSupport.State(), null),
            "decision",
            decision,
            new HashSet<ToolObservation>
            {
                new("read", ToolEffect.Read, ToolEvidence.RepositoryInspection),
            },
            0
        );

        PlannerPolicies.RepositoryGrounded()(observation).Should().BeEmpty();
    }

    [Fact]
    public void Reorient_fails_closed_unless_it_matches_session_reliability()
    {
        const PlannerDecisionValue decisionValue = PlannerDecisionValue.Reorient;
        var state = TestSupport.State() with
        {
            ExecutorTransition = new ExecutorTransition.PlannerRequested(
                new AskPlannerRequest(
                    PlannerQuestionType.RepositoryProcedure,
                    "current slice",
                    "What is safe?",
                    "Use the durable state.",
                    ["src/a.cs"]
                )
            ),
        };
        var decision = new PlannerDecision(
            decisionValue,
            "The repository evidence supports this decision.",
            [],
            ["src/a.cs"],
            "Restart from durable state.",
            decisionValue == PlannerDecisionValue.Reorient
                ? "Discard the unreliable context and use the durable state."
                : null
        );
        var observation = new OutputAcceptanceObservation<CadenceState, PlannerDecision>(
            new AgentMessageContext<CadenceState>(Guid.NewGuid(), state, null),
            "decision",
            decision,
            new HashSet<ToolObservation>
            {
                new("read", ToolEffect.Read, ToolEvidence.RepositoryInspection),
            },
            0
        );

        PlannerPolicies
            .RepositoryGrounded()(observation)
            .Should()
            .ContainSingle(problem => problem.Message.Contains("Reorient is valid only"));
    }

    [Fact]
    public void Reviewer_cannot_accept_without_repository_inspection()
    {
        var decision = new ReviewDecision(
            ReviewDecisionValue.Accept,
            TestSupport.Doctrine().Sha256,
            "Delivered",
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
        var observation = new OutputAcceptanceObservation<CadenceState, ReviewDecision>(
            new AgentMessageContext<CadenceState>(Guid.NewGuid(), TestSupport.State(), null),
            "decision",
            decision,
            new HashSet<ToolObservation>(),
            0
        );

        ReviewerPolicies.RepositoryGrounded()(observation).Should().ContainSingle();
    }

    [Fact]
    public void Reviewer_can_decide_after_inspecting_the_candidate_change_manifest()
    {
        var decision = new ReviewDecision(
            ReviewDecisionValue.Accept,
            TestSupport.Doctrine().Sha256,
            "Delivered",
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
        var observation = new OutputAcceptanceObservation<CadenceState, ReviewDecision>(
            new AgentMessageContext<CadenceState>(Guid.NewGuid(), TestSupport.State(), null),
            "decision",
            decision,
            new HashSet<ToolObservation>
            {
                new("git_changed_files", ToolEffect.Read, ToolEvidence.RepositoryInspection),
                new("git_diff", ToolEffect.Read, ToolEvidence.RepositoryInspection),
                new(
                    "run_verification_1",
                    ToolEffect.ProcessExecution,
                    ToolEvidence.RepositoryInspection
                ),
            },
            0
        );

        ReviewerPolicies.RepositoryGrounded()(observation).Should().BeEmpty();
    }

    [Fact]
    public void Reviewer_request_changes_accepts_exact_red_rerun_evidence()
    {
        var red = new ReviewEvidenceReference(
            ReviewEvidenceKind.VerificationCommand,
            Command: "dotnet test",
            ExitCode: 1,
            Stdout: "failed test",
            Stderr: "test process failed"
        );
        var decision = new ReviewDecision(
            ReviewDecisionValue.RequestChanges,
            TestSupport.Doctrine().Sha256,
            "The verification rerun failed.",
            [],
            [
                new ReviewFinding(
                    ReviewFindingSeverity.High,
                    "The exact packet verification command fails.",
                    [TestSupport.DoctrineEvidence(), red]
                ),
            ],
            []
        );
        var observation = new OutputAcceptanceObservation<CadenceState, ReviewDecision>(
            new AgentMessageContext<CadenceState>(Guid.NewGuid(), TestSupport.State(), null),
            "decision",
            decision,
            new HashSet<ToolObservation>
            {
                new("git_changed_files", ToolEffect.Read, ToolEvidence.RepositoryInspection),
                new("git_diff", ToolEffect.Read, ToolEvidence.RepositoryInspection),
            },
            0
        );

        ReviewerPolicies.RepositoryGrounded()(observation).Should().BeEmpty();
    }

    [Theory]
    [InlineData(ReviewDecisionValue.Accept)]
    [InlineData(ReviewDecisionValue.RequestChanges)]
    public void Reviewer_non_human_decisions_always_require_both_git_observations(
        ReviewDecisionValue decisionValue
    )
    {
        var decision = new ReviewDecision(
            decisionValue,
            TestSupport.Doctrine().Sha256,
            "A grounded decision.",
            [],
            decisionValue == ReviewDecisionValue.RequestChanges
                ?
                [
                    new ReviewFinding(
                        ReviewFindingSeverity.High,
                        "A repository defect.",
                        [TestSupport.DoctrineEvidence(), TestSupport.FileEvidence()]
                    ),
                ]
                : [],
            []
        );
        var observation = new OutputAcceptanceObservation<CadenceState, ReviewDecision>(
            new AgentMessageContext<CadenceState>(Guid.NewGuid(), TestSupport.State(), null),
            "decision",
            decision,
            new HashSet<ToolObservation>
            {
                new(
                    "run_verification_1",
                    ToolEffect.ProcessExecution,
                    ToolEvidence.RepositoryInspection
                ),
            },
            0
        );

        ReviewerPolicies.RepositoryGrounded()(observation).Should().ContainSingle();
    }
}
