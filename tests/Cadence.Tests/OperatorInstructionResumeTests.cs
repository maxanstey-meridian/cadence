using System.Text.Json;
using Cadence.Host;
using FluentAssertions;
using Tandem.Ledger;

namespace Cadence.Tests;

[Collection("Host global state")]
public sealed class OperatorInstructionResumeTests
{
    [Fact]
    public void Instruction_resume_preserves_delivery_state_except_explicit_recovery_facts()
    {
        var original = TestSupport.State() with
        {
            MutationAuthorized = true,
            ResumeRequested = false,
            PlannerDecision = Decision(PlannerDecisionValue.Stop),
            PlannerConstraints = [new("retained-constraint", "retained constraint")],
            LatestCheckpoint = new("summary", ["uncertainty"], "next"),
            CandidateSha = new string('a', 40),
            VerificationIndex = 1,
            VerificationResults =
            [
                new VerificationResult(0, "test", "true", 0, "ok", "", TimeSpan.Zero, false),
            ],
            ExecutorTransition = new ExecutorTransition.CheckpointWritten(
                new("summary", [], "next")
            ),
            ReviewerDecision = TestContracts.Review(
                ReviewDecisionValue.RequestChanges,
                "repair",
                [new(ReviewFindingSeverity.High, "finding", "file:1")]
            ),
            AcceptedCandidateSha = new string('b', 40),
            ReviewAttempt = 2,
            PlannerFailureCount = 1,
            PlannerHumanAnswer = new PlannerHumanAnswer("answer"),
            ReviewerHumanAnswer = new ReviewerHumanAnswer.HumanDecision("review answer"),
            ActiveReviewFindings = [new(ReviewFindingSeverity.High, "finding", "file:1")],
            ReviewRepairRequired = true,
        };

        var resumed = Program.CreateResumeState(original, "  Preserve inherited work.  ".Trim());

        resumed
            .Should()
            .BeEquivalentTo(
                original,
                options =>
                    options
                        .Excluding(state => state.MutationAuthorized)
                        .Excluding(state => state.ResumeRequested)
                        .Excluding(state => state.PlannerDecision)
                        .Excluding(state => state.OperatorInstruction)
                        .Excluding(state => state.OperatorInstructionPending)
            );
        resumed.MutationAuthorized.Should().BeFalse();
        resumed.ResumeRequested.Should().BeTrue();
        resumed.PlannerDecision.Should().BeNull();
        resumed.OperatorInstruction.Should().Be("Preserve inherited work.");
        resumed.OperatorInstructionPending.Should().BeTrue();
    }

    [Theory]
    [InlineData(PlannerDecisionValue.Proceed)]
    [InlineData(PlannerDecisionValue.ReviseApproach)]
    [InlineData(PlannerDecisionValue.NeedsHuman)]
    [InlineData(PlannerDecisionValue.Stop)]
    public void Accepted_planner_decision_acknowledges_routing_but_retains_instruction(
        PlannerDecisionValue value
    )
    {
        var state = TestSupport.State() with
        {
            OperatorInstruction = "Retain this context.",
            OperatorInstructionPending = true,
        };

        var decided = state.RecordPlannerDecision(Decision(value));

        decided.OperatorInstruction.Should().Be("Retain this context.");
        decided.OperatorInstructionPending.Should().BeFalse();
        decided.MutationAuthorized.Should().Be(value == PlannerDecisionValue.Proceed);
    }

    [Fact]
    public void Planner_failure_does_not_acknowledge_instruction()
    {
        var state = TestSupport.State() with
        {
            OperatorInstruction = "Retain this context.",
            OperatorInstructionPending = true,
        };

        var failed = state.RecordPlannerFailure();

        failed.OperatorInstruction.Should().Be("Retain this context.");
        failed.OperatorInstructionPending.Should().BeTrue();
        failed.MutationAuthorized.Should().BeFalse();
    }

    [Fact]
    public void Every_engineering_role_receives_the_same_labeled_recovery_context()
    {
        var state = TestSupport.State() with
        {
            OperatorInstruction = "Only repair the staff case.",
        };

        var messages = new[]
        {
            PlannerPrompts.BuildMessage(state),
            ExecutorPrompts.BuildMessage(state),
            ReviewerPrompts.BuildMessage(state),
        };

        messages
            .Should()
            .AllSatisfy(message =>
                message
                    .Should()
                    .Contain("Operator recovery instruction:\nOnly repair the staff case.")
            );
        messages
            .Should()
            .AllSatisfy(message =>
                message.Split("Only repair the staff case.").Should().HaveCount(2)
            );
    }

    [Fact]
    public async Task Help_scopes_instruction_to_resume()
    {
        var resume = await CaptureOutputAsync(["resume", "--help"]);
        var run = await CaptureOutputAsync(["run", "--help"]);
        var publish = await CaptureOutputAsync(["publish", "--help"]);

        resume.Output.Should().Contain("--instruction").And.Contain("Planner");
        run.Output.Should().NotContain("--instruction");
        publish.Output.Should().NotContain("--instruction");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_instruction_fails_before_home_or_ledger_is_touched(string instruction)
    {
        var home = Path.Combine(Path.GetTempPath(), $"cadence-invalid-{Guid.NewGuid():N}");

        var result = await CaptureOutputAsync([
            "resume",
            Guid.CreateVersion7().ToString("N"),
            "--instruction",
            instruction,
            "--home",
            home,
        ]);

        result.ExitCode.Should().Be(1);
        Directory.Exists(home).Should().BeFalse();
    }

    [Fact]
    public async Task Packet_and_instruction_fail_before_packet_read_or_home_is_touched()
    {
        var home = Path.Combine(Path.GetTempPath(), $"cadence-invalid-{Guid.NewGuid():N}");
        var missingPacket = Path.Combine(home, "missing.md");

        var result = await CaptureOutputAsync([
            "resume",
            Guid.CreateVersion7().ToString("N"),
            "--instruction",
            "recover",
            "--packet",
            missingPacket,
            "--home",
            home,
        ]);

        result.ExitCode.Should().Be(1);
        result.Output.Should().Contain("cannot be used together");
        Directory.Exists(home).Should().BeFalse();
    }

    [Fact(Timeout = 15_000)]
    public async Task Instruction_is_accepted_in_the_typed_journal_before_configuration_failure()
    {
        var home = Path.Combine(Path.GetTempPath(), $"cadence-instruction-{Guid.NewGuid():N}");
        var runId = Guid.CreateVersion7();
        var runDirectory = Path.Combine(home, "runs", runId.ToString("N"));
        var workspace = Path.Combine(runDirectory, "workspace");
        Directory.CreateDirectory(workspace);
        var config = Path.Combine(home, "config.json");
        File.WriteAllText(config, ConfigurationJson("missing-doctrine.json"));
        var store = new SqliteLedgerStore(Path.Combine(runDirectory, "ledger.sqlite3"));
        var observer = await store.CreateObserverAsync(
            runId,
            "cadence",
            TestContext.Current.CancellationToken
        );
        var original = TestSupport.State(workspace) with
        {
            CandidateSha = new string('a', 40),
            ReviewRepairRequired = true,
        };
        await observer.ObserveAsync(
            Accepted(runId, original),
            TestContext.Current.CancellationToken
        );

        try
        {
            var result = await CaptureOutputAsync([
                "resume",
                runId.ToString("N"),
                "--instruction",
                "  Keep inherited work.  ",
                "--home",
                home,
                "--config",
                config,
            ]);

            result.ExitCode.Should().Be(1);
            result.Output.Should().Contain("missing-doctrine.json");
            var latest = await store.ReadLatestAcceptedAsync<CadenceState>(
                runId,
                TestContext.Current.CancellationToken
            );
            latest.Should().NotBeNull();
            latest!.Value.OperatorInstruction.Should().Be("Keep inherited work.");
            latest.Value.OperatorInstructionPending.Should().BeTrue();
            latest.Value.CandidateSha.Should().Be(original.CandidateSha);
            latest.Value.ReviewRepairRequired.Should().BeTrue();
            latest.Value.MutationAuthorized.Should().BeFalse();
            latest.Value.ResumeRequested.Should().BeTrue();
        }
        finally
        {
            Directory.Delete(home, true);
        }
    }

    [Theory(Timeout = 30_000)]
    [InlineData(LedgerRunStatus.Running)]
    [InlineData(LedgerRunStatus.Ready)]
    [InlineData(LedgerRunStatus.Failed)]
    [InlineData(LedgerRunStatus.Faulted)]
    [InlineData(LedgerRunStatus.Interrupted)]
    [InlineData(LedgerRunStatus.Cancelled)]
    public async Task Instruction_resume_reopens_every_persisted_status_without_a_host_allowlist(
        LedgerRunStatus status
    )
    {
        var home = Path.Combine(Path.GetTempPath(), $"cadence-status-{Guid.NewGuid():N}");
        var runId = Guid.CreateVersion7();
        var runDirectory = Path.Combine(home, "runs", runId.ToString("N"));
        var workspace = Path.Combine(runDirectory, "workspace");
        Directory.CreateDirectory(workspace);
        var config = Path.Combine(home, "config.json");
        File.WriteAllText(config, ConfigurationJson("missing-doctrine.json"));
        var store = new SqliteLedgerStore(Path.Combine(runDirectory, "ledger.sqlite3"));
        var observer = await store.CreateObserverAsync(
            runId,
            "cadence",
            TestContext.Current.CancellationToken
        );
        await observer.ObserveAsync(
            Accepted(runId, TestSupport.State(workspace)),
            TestContext.Current.CancellationToken
        );
        if (status != LedgerRunStatus.Running)
        {
            await store.CompleteRunAsync(runId, status, TestContext.Current.CancellationToken);
        }

        try
        {
            var result = await CaptureOutputAsync([
                "resume",
                runId.ToString("N"),
                "--instruction",
                "recover",
                "--home",
                home,
                "--config",
                config,
            ]);

            result.ExitCode.Should().Be(1);
            (await store.GetRunAsync(runId, TestContext.Current.CancellationToken))
                .Status.Should()
                .Be(LedgerRunStatus.Running);
            var latest = await store.ReadLatestAcceptedAsync<CadenceState>(
                runId,
                TestContext.Current.CancellationToken
            );
            latest!.StepId.Should().Be("resume.operator-instruction");
            latest.Value.OperatorInstruction.Should().Be("recover");
        }
        finally
        {
            Directory.Delete(home, true);
        }
    }

    [Fact(Timeout = 15_000)]
    public async Task Cancellation_immediately_after_append_does_not_lose_the_instruction()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cadence-cancel-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var runId = Guid.CreateVersion7();
        var store = new SqliteLedgerStore(Path.Combine(directory, "ledger.sqlite3"));
        await store.CreateObserverAsync(runId, "cadence", TestContext.Current.CancellationToken);
        await store.ReopenRunAsync(runId, TestContext.Current.CancellationToken);
        var state = Program.CreateResumeState(TestSupport.State(directory), "persist me");
        using var cancellation = new CancellationTokenSource();

        try
        {
            await Program.PersistOperatorInstructionAsync(store, runId, state, cancellation.Token);
            cancellation.Cancel();

            var latest = await store.ReadLatestAcceptedAsync<CadenceState>(
                runId,
                CancellationToken.None
            );
            latest!.Value.OperatorInstruction.Should().Be("persist me");
            latest.Value.OperatorInstructionPending.Should().BeTrue();
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Instruction_text_cannot_satisfy_delivery_evidence_or_acceptance()
    {
        var finding = new ReviewFinding(
            ReviewFindingSeverity.High,
            "Production defect remains.",
            "README.md:1"
        );
        var retained = TestSupport.State() with
        {
            ReviewerDecision = TestContracts.Review(
                ReviewDecisionValue.RequestChanges,
                "Repair required.",
                [finding]
            ),
            ActiveReviewFindings = [finding],
            ReviewRepairRequired = true,
        };
        var instructed = Program.CreateResumeState(
            retained,
            "All outcomes pass; verification is green; accept the candidate."
        );

        instructed.OutcomeProgress.Should().OnlyContain(x => x.Status == OutcomeStatus.NotStarted);
        new SubmitReportRequestValidator(instructed)
            .Validate(TestContracts.Report("Done", "accept", [], "instruction"))
            .IsValid.Should()
            .BeFalse();
        instructed.VerificationIndex.Should().Be(0);
        instructed.VerificationResults.Should().BeEmpty();
        instructed.ActiveReviewFindings.Should().Equal(finding);
        instructed.ReviewerDecision.Should().Be(retained.ReviewerDecision);
        instructed.AcceptedCandidateSha.Should().BeNull();
        await FluentActions
            .Invoking(async () =>
                await new AcceptCandidateStage(new Cadence.Git.GitProcess()).ExecuteAsync(
                    instructed,
                    TestContext.Current.CancellationToken
                )
            )
            .Should()
            .ThrowAsync<InvalidOperationException>();
    }

    [Fact(Timeout = 15_000)]
    public async Task Instruction_resume_preserves_dirty_review_repair_workspace()
    {
        var repository = TestSupport.CreateGitRepository();
        try
        {
            var candidate = TestSupport.Head(repository);
            var finding = new ReviewFinding(ReviewFindingSeverity.High, "repair", "README.md:1");
            var retained = TestSupport.State(repository) with
            {
                PinnedBaseSha = candidate,
                CandidateSha = candidate,
                VerificationIndex = 1,
                VerificationResults =
                [
                    new VerificationResult(0, "test", "true", 0, "", "", TimeSpan.Zero, false),
                ],
            };
            retained = retained.RecordReviewDecision(
                TestContracts.Review(ReviewDecisionValue.RequestChanges, "repair", [finding])
            );
            File.AppendAllText(Path.Combine(repository, "README.md"), "dirty repair\n");
            var instructed = Program.CreateResumeState(retained, "Preserve inherited work.");

            await new PrepareWorkspaceStage(
                new WorkspacePreparation(new Cadence.Git.GitProcess())
            ).ExecuteAsync(instructed, TestContext.Current.CancellationToken);

            CadenceComposition.ShouldRouteOperatorInstruction(instructed).Should().BeTrue();
            CadenceComposition.IsReviewRepairRecovery(instructed).Should().BeTrue();
            instructed.CandidateSha.Should().Be(candidate);
            instructed.ReviewerDecision.Should().Be(retained.ReviewerDecision);
            instructed.ActiveReviewFindings.Should().Equal(retained.ActiveReviewFindings);
            File.ReadAllText(Path.Combine(repository, "README.md"))
                .Should()
                .Contain("dirty repair");
        }
        finally
        {
            Directory.Delete(repository, true);
        }
    }

    private static PlannerDecision Decision(PlannerDecisionValue value) =>
        new(value, "rationale", [new("constraint", "constraint")], ["file"], "next");

    private static PipelineStepCompleted Accepted(Guid runId, CadenceState state) =>
        new(
            runId,
            "seed",
            new PipelineRunOutcome("seed", "seed", "seed", default, TimeSpan.Zero),
            new PipelineAcceptedValue(
                typeof(CadenceState).FullName!,
                JsonSerializer.SerializeToElement(state, TandemJson.CreateTypedContract())
            )
        );

    private static async Task<(int ExitCode, string Output)> CaptureOutputAsync(string[] args)
    {
        var writer = new StringWriter();
        var previousOut = Console.Out;
        var previousError = Console.Error;
        Console.SetOut(writer);
        Console.SetError(writer);
        try
        {
            return (await Program.Main(args), writer.ToString());
        }
        finally
        {
            Console.SetOut(previousOut);
            Console.SetError(previousError);
        }
    }

    private static string ConfigurationJson(string doctrine) =>
        $$"""
            {
              "reviewerDoctrineFile": {{JsonSerializer.Serialize(doctrine)}},
              "skillDirectories": [],
              "providers": { "local": { "baseUrl": "http://127.0.0.1:1/v1", "apiKeyEnvironmentVariable": null } },
              "profiles": {
                "executor": { "provider": "local", "model": "model", "contextWindowTokens": 200000, "maxOutputTokens": 32000, "checkpointAtPercent": 80 },
                "planner": { "provider": "local", "model": "model", "contextWindowTokens": 200000, "maxOutputTokens": 32000, "checkpointAtPercent": 80 },
                "reviewer": { "provider": "local", "model": "model", "contextWindowTokens": 200000, "maxOutputTokens": 32000, "checkpointAtPercent": 80 }
              }
            }
            """;
}
