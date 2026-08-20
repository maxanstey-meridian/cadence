using Cadence.Git;
using FluentAssertions;

namespace Cadence.Tests;

public sealed class SimplifiedLifecycleTests
{
    [Fact]
    public async Task Simple_report_captures_candidate_without_copied_packet_facts()
    {
        await InRepository(async repository =>
        {
            File.WriteAllText(Path.Combine(repository, "feature.txt"), "done\n");
            var state = State(repository)
                .RecordImplementationReport(new SubmitReportRequest("Ready", "feature"));

            var result = await new CaptureCandidateStage(new GitProcess()).ExecuteAsync(
                state,
                TestContext.Current.CancellationToken
            );

            var captured = result.Should().BeOfType<Outcome<CadenceState>.Success>().Subject.State;
            captured.CandidateSha.Should().Be(TestSupport.Head(repository));
            captured.VerificationResults.Should().BeEmpty();
        });
    }

    [Fact]
    public async Task Initial_no_change_creates_allow_empty_candidate()
    {
        await InRepository(async repository =>
        {
            var original = TestSupport.Head(repository);
            var state = State(repository)
                .RecordImplementationReport(
                    new SubmitReportRequest("Already satisfied", "verify existing behavior")
                );

            var result = await new CaptureCandidateStage(new GitProcess()).ExecuteAsync(
                state,
                TestContext.Current.CancellationToken
            );

            var captured = result.Should().BeOfType<Outcome<CadenceState>.Success>().Subject.State;
            captured.CandidateSha.Should().NotBeNull().And.NotBe(original);
            TestSupport.Git(
                repository,
                "diff",
                "--quiet",
                $"{original}^{{tree}}",
                $"{captured.CandidateSha}^{{tree}}"
            );
        });
    }

    [Fact]
    public async Task Initial_no_change_runs_verification_and_becomes_publishable()
    {
        await InRepository(async repository =>
        {
            var packet = TestSupport.Packet() with
            {
                Repository = repository,
                Verification = [new VerificationCommand("existing behavior", "test -f README.md")],
            };
            var state = CadenceState
                .Create(packet, TestSupport.Head(repository), repository)
                .RecordImplementationReport(
                    new SubmitReportRequest("Already satisfied", "verify existing behavior")
                );
            var captured = await new CaptureCandidateStage(new GitProcess()).ExecuteAsync(
                state,
                TestContext.Current.CancellationToken
            );
            state = captured.Should().BeOfType<Outcome<CadenceState>.Success>().Subject.State;

            var verificationStage = new VerificationStage(
                new VerificationOperation(new GitProcess())
            );
            var verificationPipeline = Pipeline
                .Start(verificationStage, "initial-no-change")
                .Build(verificationStage);
            var verificationRun = await new PipelineRunner().RunAsync(
                verificationPipeline,
                state,
                cancellationToken: TestContext.Current.CancellationToken
            );
            state = verificationRun.State;
            state
                .VerificationResults.Should()
                .ContainSingle(result => result.ExitCode == 0 && !result.TimedOut);
            state = state.RecordReviewDecision(
                new ReviewDecision(ReviewDecisionValue.Accept, "Accepted", [])
            );

            var accepted = await new AcceptCandidateStage(new GitProcess()).ExecuteAsync(
                state,
                TestContext.Current.CancellationToken
            );
            accepted
                .Should()
                .BeOfType<Outcome<CadenceState>.Success>()
                .Subject.State.AcceptedCandidateSha.Should()
                .Be(state.CandidateSha);
        });
    }

    [Fact]
    public async Task Unchanged_repair_is_rejected_but_changed_tree_is_captured()
    {
        await InRepository(async repository =>
        {
            var finding = new ReviewFinding(
                ReviewFindingSeverity.High,
                "Behavior is wrong",
                "README.md:1"
            );
            var rejected = State(repository)
                .RecordReviewDecision(
                    new ReviewDecision(ReviewDecisionValue.RequestChanges, "Repair", [finding])
                )
                .RecordImplementationReport(new SubmitReportRequest("Repaired", "repair"));

            var unchanged = await new CaptureCandidateStage(new GitProcess()).ExecuteAsync(
                rejected,
                TestContext.Current.CancellationToken
            );
            var unchangedState = unchanged
                .Should()
                .BeOfType<Outcome<CadenceState>.Success>()
                .Subject.State;
            unchangedState.CandidateSha.Should().BeNull();
            unchangedState
                .ExecutorTransition.Should()
                .BeOfType<ExecutorTransition.CandidateUnchanged>();

            File.AppendAllText(Path.Combine(repository, "README.md"), "repair\n");
            var changed = await new CaptureCandidateStage(new GitProcess()).ExecuteAsync(
                rejected,
                TestContext.Current.CancellationToken
            );
            changed
                .Should()
                .BeOfType<Outcome<CadenceState>.Success>()
                .Subject.State.CandidateSha.Should()
                .NotBeNull();
        });
    }

    [Fact]
    public void Reviewer_contract_enforces_material_findings_and_human_boundary()
    {
        var validator = new ReviewDecisionValidator();
        validator
            .Validate(new ReviewDecision(ReviewDecisionValue.RequestChanges, "Change", []))
            .IsValid.Should()
            .BeFalse();
        validator
            .Validate(
                new ReviewDecision(
                    ReviewDecisionValue.Accept,
                    "Accept",
                    [new(ReviewFindingSeverity.High, "Defect", "file:1")]
                )
            )
            .IsValid.Should()
            .BeFalse();
        validator
            .Validate(
                new ReviewDecision(
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
        var state = State("/workspace");
        state.MutationAuthorized.Should().BeFalse();
        state = state.RecordPlannerDecision(
            new PlannerDecision(
                PlannerDecisionValue.Proceed,
                "Sound",
                ["Keep API"],
                ["source"],
                "Implement"
            )
        );
        state.MutationAuthorized.Should().BeTrue();
        state.PlannerConstraints.Should().Contain("Keep API");
        state = state.RecordPlannerRequest(new AskPlannerRequest("repair", "How?", "Approach", []));
        state.MutationAuthorized.Should().BeFalse();
    }

    private static CadenceState State(string repository)
    {
        var head = Directory.Exists(repository) ? TestSupport.Head(repository) : "base";
        return TestSupport.State(repository) with { PinnedBaseSha = head };
    }

    private static async Task InRepository(Func<string, Task> action)
    {
        var repository = TestSupport.CreateGitRepository();
        try
        {
            await action(repository);
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }
}
