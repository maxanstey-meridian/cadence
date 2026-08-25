using FluentAssertions;

namespace Cadence.Tests;

public sealed class PromptContractTests
{
    [Fact]
    public void Role_messages_classify_current_facts_obligations_and_prior_claims()
    {
        var state = State();

        var executor = ExecutorPrompts.BuildMessage(state);
        var planner = PlannerPrompts.BuildMessage(
            state with
            {
                ExecutorTransition = new ExecutorTransition.PlannerRequested(
                    new(
                        "Current slice claim",
                        "Which direction?",
                        "Use the established seam.",
                        ["src/Seam.cs: the seam exists."]
                    )
                ),
            }
        );
        var reviewer = ReviewerPrompts.BuildMessage(state);

        var contract = DeliveryContractRenderer.Render(state);
        new[] { executor, planner, reviewer }
            .Should()
            .AllSatisfy(message =>
            {
                message.Should().Contain("Implementation context:\nInspect the concrete seam.");
                message.Split(contract, StringSplitOptions.None).Should().HaveCount(2);
                message.Should().Contain("[outcome:outcome-1]");
                message.Should().Contain("[acceptance:criterion-1] for [outcome:outcome-1]");
                message.Should().Contain("[packet-constraint:public-contract]");
                message.Should().Contain("[planner-constraint:preserve-seam]");
                message.Should().Contain("Use the bracketed references above exactly");
            });

        executor.Should().Contain("Current mechanical mutation authority: True");
        executor.Should().Contain("Prior Executor progress notes (unverified continuity)");
        executor.Should().Contain("Current repair findings recorded by the workflow");
        executor.Should().NotContain("reviewRepairClaims");
        executor.Should().Contain("Latest accepted Planner decision");
        executor.Should().Contain("Current mechanical verification results for the candidate");
        executor.Should().Contain("Current mechanical candidate SHA: candidate-sha");
        executor.Should().Contain("Continuity checkpoint (durable recovery claims");

        planner.Should().Contain("Latest continuity checkpoint (unverified)");
        planner.Should().Contain("Executor request (unverified proposal)");
        planner.Should().Contain("claims, not established Planner facts");
        planner.Should().Contain("Current recorded verification results");
        planner.Should().Contain("Latest accepted Planner decision");
        planner.Should().Contain("authoritative only for the requested Human-owned decision");

        reviewer.Should().Contain("Current mechanical pinned base: base-sha");
        reviewer.Should().Contain("Current mechanical candidate SHA: candidate-sha");
        reviewer.Should().Contain("Current mechanical verification results bound to the candidate");
        reviewer.Should().Contain("Current repair findings recorded by the workflow");
        reviewer.Should().Contain("Executor handoff notes (unverified)");
        reviewer.Should().Contain("Required review outcome:");
        reviewer.Should().Contain("exact candidate repository state completely satisfies");
        reviewer.Should().NotContain("Return one value shaped like");
        reviewer.Should().NotContain("\"decision\": \"Accept\"");
    }

    [Fact]
    public void Role_instructions_declare_outcomes_without_prescribing_epistemic_workflows()
    {
        var doctrine = TestSupport.Doctrine();
        var reviewer = ReviewerPrompts.BuildInstructions(doctrine);
        var all = string.Join(
            "\n",
            ExecutorPrompts.Instructions,
            PlannerPrompts.Instructions,
            reviewer
        );

        ExecutorPrompts
            .Instructions.Should()
            .Contain("coding agent responsible for producing the complete repository")
            .And.Contain("change described by the packet");
        ExecutorPrompts.Instructions.Should().Contain("affected code and consumers not explicitly");
        ExecutorPrompts.Instructions.Should().Contain("Implementation is complete only when");
        ExecutorPrompts.Instructions.Should().Contain("Mutation authorized: true means Planner");
        ExecutorPrompts
            .Instructions.Should()
            .Contain("current mechanical facts bound to the exact candidate");
        ExecutorPrompts.Instructions.Should().Contain("A passing result does");
        ExecutorPrompts.Instructions.Should().Contain("not authorize Proceed");
        ExecutorPrompts
            .Instructions.Should()
            .Contain("prior reports, checkpoints, and ledger entries only as continuity notes");
        ExecutorPrompts.Instructions.Should().Contain("does not establish completion");
        ExecutorPrompts.Instructions.Should().Contain("Green tests that assert obsolete behavior");

        PlannerPrompts
            .Instructions.Should()
            .Contain("engineering agent responsible for deciding whether the Executor's");
        PlannerPrompts.Instructions.Should().Contain("affected consumers, repository invariants");
        PlannerPrompts
            .Instructions.Should()
            .Contain("establish material repository")
            .And.Contain("facts independently before relying on them");
        PlannerPrompts.Instructions.Should().Contain("without warrant in the packet");
        PlannerPrompts.Instructions.Should().Contain("does not prescribe a local task sequence");
        PlannerPrompts.Instructions.Should().Contain("concise stable local ID");
        PlannerPrompts.Instructions.Should().Contain("without the `planner-constraint:` prefix");
        PlannerPrompts.Instructions.Should().NotContain("file_access_write");

        reviewer.Should().Contain("independent code-review agent responsible for deciding whether");
        reviewer.Should().Contain("including relevant unchanged code");
        reviewer.Should().Contain("Look for concrete").And.Contain("counterexamples");
        reviewer.Should().Contain("repository is the subject of the review");
        reviewer.Should().Contain("do not establish that the candidate is");
        reviewer.Should().Contain("A diff of selected changed files is not enough");
        reviewer.Should().NotContain("evidence sources");
        reviewer.Should().NotContain("candidate evidence sufficient for");
        reviewer.Should().NotContain("complete candidate scope named or implied");
        var firstDoctrine = reviewer.IndexOf("[correctness]", StringComparison.Ordinal);
        var secondDoctrine = reviewer.IndexOf("[real-integration]", StringComparison.Ordinal);
        firstDoctrine.Should().BeGreaterThan(-1).And.BeLessThan(secondDoctrine);
        reviewer.Split("[correctness]", StringSplitOptions.None).Should().HaveCount(2);
        reviewer.Split("[real-integration]", StringSplitOptions.None).Should().HaveCount(2);

        all.Should().NotContain("only post-capture pipeline verification is authoritative");
        all.Should().NotContain("concrete next continuation");
        all.Should().NotContain("Then account for every");
    }

    [Fact]
    public void Assembled_harness_and_outputs_preserve_role_specific_evidence_semantics()
    {
        CadenceHarnessInstructions.Value.Should().Contain("autonomous engineering agent");
        CadenceHarnessInstructions.Value.Should().Contain("packet defines the required delivery");
        CadenceHarnessInstructions.Value.Should().Contain("repository is the implementation");
        CadenceHarnessInstructions
            .Value.Should()
            .Contain("continuity material, not repository truth");
        CadenceHarnessInstructions.Value.Should().NotContain("evidence sources");

        var plannerOutput = new PlannerDecisionOutput();
        plannerOutput.Instructions.Should().Contain("source and material fact established");
        plannerOutput.Instructions.Should().Contain("not prescribe a local task sequence");
        plannerOutput
            .Examples(State())
            .Single()
            .Output.Decision.Should()
            .Be(PlannerDecisionValue.ReviseApproach);

        var reviewOutput = new ReviewDecisionOutput();
        reviewOutput.Instructions.Should().Contain("complete delivery contract");
        var reviewExample = reviewOutput.Examples(State()).Single().Output;
        reviewExample.Decision.Should().Be(ReviewDecisionValue.RequestChanges);
        reviewExample.Assessments.Should().Contain(x => !x.Satisfied);
        reviewExample
            .Findings.Should()
            .ContainSingle(x => x.Severity == ReviewFindingSeverity.High);
    }

    private static CadenceState State()
    {
        var packet = new Packet(
            "Prompt contract",
            "/source",
            "main",
            [new("outcome-1", "Deliver the prompt contract")],
            [new("check", "task check")],
            [new PacketConstraint("public-contract", "Keep the public contract stable.")],
            "Inspect the concrete seam.",
            Acceptance: [new("criterion-1", "outcome-1", "Prove the prompt contract")]
        );
        return CadenceState.Create(packet, "base-sha", "/workspace") with
        {
            MutationAuthorized = true,
            OutcomeProgress =
            [
                new("outcome-1", OutcomeStatus.InProgress, "Executor claim", "Finish"),
            ],
            PlannerDecision = new(
                PlannerDecisionValue.Proceed,
                "Proceed with the established seam.",
                [new PlannerConstraint("preserve-seam", "Preserve the seam.")],
                ["src/Seam.cs: the seam exists."],
                "Update src/Seam.cs through the established boundary.",
                null,
                null,
                null
            ),
            PlannerConstraints = [new PlannerConstraint("preserve-seam", "Preserve the seam.")],
            LatestCheckpoint = new("Checkpoint claim", [], "Continue"),
            CandidateSha = "candidate-sha",
            VerificationIndex = 1,
            VerificationResults =
            [
                new(0, "check", "task check", 0, "passed", "", TimeSpan.Zero, false),
            ],
            ExecutorTransition = new ExecutorTransition.ReportSubmitted(
                TestContracts.Report("Executor report claim", "Prompt contract")
            ),
            ActiveReviewFindings =
            [
                new(ReviewFindingSeverity.High, "Repair the prompt contract.", "src/Seam.cs:1"),
            ],
        };
    }
}
