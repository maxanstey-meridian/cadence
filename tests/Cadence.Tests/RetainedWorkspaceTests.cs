using Cadence.Git;
using FluentAssertions;

namespace Cadence.Tests;

public sealed class RetainedWorkspaceTests
{
    [Fact]
    public async Task Clean_workspace_at_captured_candidate_is_valid_for_review_resume()
    {
        var repository = TestSupport.CreateGitRepository();
        try
        {
            var candidate = TestSupport.Head(repository);
            var act = async () =>
                await new WorkspacePreparation(new GitProcess()).ValidateReviewWorkspaceAsync(
                    candidate,
                    repository,
                    TestContext.Current.CancellationToken
                );
            await act.Should().NotThrowAsync();
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Theory]
    [InlineData("dirty")]
    [InlineData("different-head")]
    public async Task Dirty_or_different_candidate_is_rejected_for_review_resume(string change)
    {
        var repository = TestSupport.CreateGitRepository();
        try
        {
            var candidate = TestSupport.Head(repository);
            if (change == "dirty")
            {
                File.AppendAllText(Path.Combine(repository, "README.md"), "dirty\n");
            }
            else
            {
                File.WriteAllText(Path.Combine(repository, "next.txt"), "next\n");
                TestSupport.Git(repository, "add", "next.txt");
                TestSupport.Git(repository, "commit", "-m", "next");
            }
            var act = async () =>
                await new WorkspacePreparation(new GitProcess()).ValidateReviewWorkspaceAsync(
                    candidate,
                    repository,
                    TestContext.Current.CancellationToken
                );
            await act.Should().ThrowAsync<WorkspacePreparationException>();
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Fact]
    public async Task Missing_retained_executor_workspace_fails_without_cloning()
    {
        var source = TestSupport.CreateGitRepository();
        var missing = Path.Combine(
            Path.GetTempPath(),
            $"cadence-retained-missing-{Guid.NewGuid():N}"
        );
        try
        {
            var state = CadenceState.Create(
                TestSupport.Packet() with
                {
                    Repository = source,
                },
                TestSupport.Head(source),
                missing
            ) with
            {
                ResumeRequested = true,
                ExecutorTransition = new ExecutorTransition.CheckpointWritten(
                    new WriteCheckpointRequest("In progress", [], "Continue")
                ),
            };

            var act = async () =>
                await new PrepareWorkspaceStage(
                    new WorkspacePreparation(new GitProcess())
                ).ExecuteAsync(state, TestContext.Current.CancellationToken);

            await act.Should().ThrowAsync<WorkspacePreparationException>();
            Directory
                .Exists(missing)
                .Should()
                .BeFalse("resume must never recreate retained workspaces");
        }
        finally
        {
            Directory.Delete(source, recursive: true);
            if (Directory.Exists(missing))
            {
                Directory.Delete(missing, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Missing_review_workspace_is_rejected()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"cadence-missing-{Guid.NewGuid():N}");
        var act = async () =>
            await new WorkspacePreparation(new GitProcess()).ValidateReviewWorkspaceAsync(
                "candidate",
                missing,
                TestContext.Current.CancellationToken
            );
        await act.Should().ThrowAsync<WorkspacePreparationException>();
    }

    [Fact]
    public async Task Accepted_review_resume_uses_strict_candidate_validation()
    {
        var repository = TestSupport.CreateGitRepository();
        try
        {
            var candidate = TestSupport.Head(repository);
            var state = TestSupport.State(repository) with
            {
                PinnedBaseSha = candidate,
                CandidateSha = candidate,
                VerificationIndex = 1,
                VerificationResults = [PassedVerification()],
                ReviewerDecision = new ReviewDecision(ReviewDecisionValue.Accept, "Accepted", []),
            };
            File.AppendAllText(Path.Combine(repository, "README.md"), "dirty\n");

            var act = async () =>
                await new PrepareWorkspaceStage(
                    new WorkspacePreparation(new GitProcess())
                ).ExecuteAsync(state, TestContext.Current.CancellationToken);

            await act.Should().ThrowAsync<WorkspacePreparationException>();
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Fact]
    public async Task Dirty_executor_resume_preserves_work_for_planner_reauthorization()
    {
        var repository = TestSupport.CreateGitRepository();
        try
        {
            var pinned = TestSupport.Head(repository);
            var changed = Path.Combine(repository, "README.md");
            File.AppendAllText(changed, "work in progress\n");
            var state = TestSupport.State(repository) with
            {
                PinnedBaseSha = pinned,
                ResumeRequested = true,
                MutationAuthorized = false,
                ExecutorTransition = new ExecutorTransition.CheckpointWritten(
                    new WriteCheckpointRequest("In progress", [], "Continue")
                ),
            };

            await new PrepareWorkspaceStage(
                new WorkspacePreparation(new GitProcess())
            ).ExecuteAsync(state, TestContext.Current.CancellationToken);

            File.ReadAllText(changed).Should().Contain("work in progress");
            state.MutationAuthorized.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Fact]
    public async Task Uncapped_request_changes_resume_preserves_dirty_repair_and_routes_to_planner()
    {
        var repository = TestSupport.CreateGitRepository();
        try
        {
            var candidate = TestSupport.Head(repository);
            var state = TestSupport.State(repository) with
            {
                ResumeRequested = true,
                PinnedBaseSha = candidate,
                CandidateSha = candidate,
                VerificationIndex = 1,
                VerificationResults = [PassedVerification()],
            };
            state = state.RecordReviewDecision(
                new ReviewDecision(
                    ReviewDecisionValue.RequestChanges,
                    "Repair required",
                    [new ReviewFinding(ReviewFindingSeverity.High, "Defect", "README.md:1")]
                )
            );
            var changed = Path.Combine(repository, "README.md");
            File.AppendAllText(changed, "repair in progress\n");

            await new PrepareWorkspaceStage(
                new WorkspacePreparation(new GitProcess())
            ).ExecuteAsync(state, TestContext.Current.CancellationToken);

            CadenceComposition.IsReviewRepairRecovery(state).Should().BeTrue();
            state.MutationAuthorized.Should().BeFalse();
            File.ReadAllText(changed).Should().Contain("repair in progress");
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Fact]
    public async Task Zero_result_candidate_resume_returns_to_verification_not_planner()
    {
        var repository = TestSupport.CreateGitRepository();
        try
        {
            var state = VerificationResumeState(repository, 0, []);
            await new PrepareWorkspaceStage(
                new WorkspacePreparation(new GitProcess())
            ).ExecuteAsync(state, TestContext.Current.CancellationToken);

            CadenceComposition.IsVerificationRecovery(state).Should().BeTrue();
            CadenceComposition.IsReviewRecovery(state).Should().BeFalse();
            state = await RunOneVerificationStep(state);
            state.VerificationIndex.Should().Be(1);
            CadenceComposition.IsVerificationRecovery(state).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Fact]
    public async Task Partially_verified_candidate_resume_finishes_before_review()
    {
        var repository = TestSupport.CreateGitRepository();
        try
        {
            var state = VerificationResumeState(repository, 1, [PassedVerification()]);
            await new PrepareWorkspaceStage(
                new WorkspacePreparation(new GitProcess())
            ).ExecuteAsync(state, TestContext.Current.CancellationToken);

            CadenceComposition.IsVerificationRecovery(state).Should().BeTrue();
            CadenceComposition.IsReviewRecovery(state).Should().BeFalse();
            state = await RunOneVerificationStep(state);
            state.VerificationIndex.Should().Be(2);
            CadenceComposition.IsVerificationRecovery(state).Should().BeFalse();
            CadenceComposition.IsReviewRecovery(state).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    private static CadenceState VerificationResumeState(
        string repository,
        int verificationIndex,
        IReadOnlyList<VerificationResult> results
    )
    {
        var candidate = TestSupport.Head(repository);
        var packet = TestSupport.Packet() with
        {
            Repository = repository,
            Verification =
            [
                new VerificationCommand("first", "test -f README.md"),
                new VerificationCommand("second", "test -f README.md"),
            ],
        };
        return CadenceState.Create(packet, candidate, repository) with
        {
            ResumeRequested = true,
            CandidateSha = candidate,
            VerificationIndex = verificationIndex,
            VerificationResults = results,
            ExecutorTransition = new ExecutorTransition.ReportSubmitted(
                new SubmitReportRequest("Ready", "implementation")
            ),
        };
    }

    private static async Task<CadenceState> RunOneVerificationStep(CadenceState state)
    {
        var stage = new VerificationStage(new VerificationOperation(new GitProcess()));
        var pipeline = Pipeline.Start(stage, "verification-resume").Build(stage);
        var run = await new PipelineRunner().RunAsync(
            pipeline,
            state,
            cancellationToken: TestContext.Current.CancellationToken
        );
        return run.State;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Pending_reviewer_human_resume_requires_strict_candidate_workspace(
        bool reviewCap
    )
    {
        var repository = TestSupport.CreateGitRepository();
        try
        {
            var candidate = TestSupport.Head(repository);
            var decision = reviewCap
                ? new ReviewDecision(
                    ReviewDecisionValue.RequestChanges,
                    "Repair required",
                    [new ReviewFinding(ReviewFindingSeverity.High, "Defect", "README.md:1")]
                )
                : new ReviewDecision(
                    ReviewDecisionValue.NeedsHuman,
                    "Product decision required",
                    [],
                    "Choose behavior",
                    HumanDecisionDomain.Product
                );
            var state = TestSupport.State(repository) with
            {
                PinnedBaseSha = candidate,
                CandidateSha = candidate,
                VerificationIndex = 1,
                VerificationResults = [PassedVerification()],
                MaximumReviewAttempts = reviewCap ? 1 : 3,
            };
            state = state.RecordReviewDecision(decision);
            CadenceComposition.IsPendingReviewerHumanRecovery(state).Should().BeTrue();
            File.AppendAllText(Path.Combine(repository, "README.md"), "dirty\n");

            var act = async () =>
                await new PrepareWorkspaceStage(
                    new WorkspacePreparation(new GitProcess())
                ).ExecuteAsync(state, TestContext.Current.CancellationToken);

            await act.Should().ThrowAsync<WorkspacePreparationException>();
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Fact]
    public async Task Persisted_human_decision_resumes_reviewer_with_answer()
    {
        var repository = TestSupport.CreateGitRepository();
        try
        {
            var reviewer = new ScriptedChatClient(
                "reviewer",
                TestSupport.Text(
                    """
                    {"decision":"Accept","summary":"The candidate is acceptable.","findings":[],"humanQuestion":null,"humanDecisionDomain":null}
                    """
                )
            );
            var composition = CreateComposition(reviewer: reviewer);
            var state = ReviewState(
                repository,
                new ReviewDecision(
                    ReviewDecisionValue.NeedsHuman,
                    "A product decision is required.",
                    [],
                    "Which behavior should be used?",
                    HumanDecisionDomain.Product
                )
            );
            state = HumanInteraction.ApplyReviewerAnswer(
                state,
                new ReviewerHumanAnswer.HumanDecision("Use the existing behavior.")
            );

            var result = await new PipelineRunner().RunAsync(
                composition.Build(),
                state,
                new PipelineRunOptions(Observer: new NoOpPersistenceObserver()),
                cancellationToken: TestContext.Current.CancellationToken
            );

            result.Succeeded.Should().BeTrue();
            reviewer.CallCount.Should().Be(1);
            reviewer
                .Requests.SelectMany(request => request)
                .Should()
                .Contain(message => message.Text.Contains("Use the existing behavior."));
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Fact]
    public async Task Persisted_continue_repairs_resets_budget_and_enters_repair_flow()
    {
        var repository = TestSupport.CreateGitRepository();
        try
        {
            var planner = new ScriptedChatClient("planner");
            var composition = CreateComposition(planner: planner);
            var state = ReviewState(repository, RequestChanges(), maximumReviewAttempts: 1);
            state = HumanInteraction.ApplyReviewerAnswer(
                state,
                new ReviewerHumanAnswer.ContinueRepairs()
            );

            var act = async () =>
                await new PipelineRunner().RunAsync(
                    composition.Build(),
                    state,
                    new PipelineRunOptions(Observer: new NoOpPersistenceObserver()),
                    cancellationToken: TestContext.Current.CancellationToken
                );

            state.ReviewAttempt.Should().Be(0);
            await act.Should().ThrowAsync<PipelineRunException>();
            planner.CallCount.Should().Be(1, "continued repairs resume through Planner");
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Fact]
    public async Task Persisted_stop_terminates_after_recovery()
    {
        var repository = TestSupport.CreateGitRepository();
        try
        {
            var planner = new ScriptedChatClient("planner");
            var reviewer = new ScriptedChatClient("reviewer");
            var composition = CreateComposition(planner, reviewer);
            var state = ReviewState(repository, RequestChanges(), maximumReviewAttempts: 1);
            state = HumanInteraction.ApplyReviewerAnswer(state, new ReviewerHumanAnswer.Stop());

            var result = await new PipelineRunner().RunAsync(
                composition.Build(),
                state,
                new PipelineRunOptions(Observer: new NoOpPersistenceObserver()),
                cancellationToken: TestContext.Current.CancellationToken
            );

            result.Succeeded.Should().BeFalse();
            planner.CallCount.Should().Be(0);
            reviewer.CallCount.Should().Be(0);
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    private static CadenceComposition CreateComposition(
        ScriptedChatClient? planner = null,
        ScriptedChatClient? reviewer = null
    )
    {
        var executor = new ScriptedChatClient("executor");
        planner ??= new ScriptedChatClient("planner");
        reviewer ??= new ScriptedChatClient("reviewer");
        var capabilities = CadenceCapabilities.Create();
        return new CadenceComposition(
            new CadenceParticipantsFactory(
                name =>
                    name switch
                    {
                        CadenceIds.Executor => executor,
                        CadenceIds.Planner => planner,
                        CadenceIds.Reviewer => reviewer,
                        _ => throw new InvalidOperationException($"Unknown participant '{name}'."),
                    },
                _ => new CadenceAgentProfile(32_000, 4_000, 80, DisableCompaction: true),
                TestSupport.Doctrine(),
                [],
                new WorkspacePreparation(new GitProcess()),
                new GitProcess(),
                capabilities.AskPlanner,
                capabilities.SubmitReport,
                capabilities.WriteCheckpoint
            )
        );
    }

    private static CadenceState ReviewState(
        string repository,
        ReviewDecision decision,
        int maximumReviewAttempts = 3
    )
    {
        var candidate = TestSupport.Head(repository);
        var state = CadenceState.Create(
            TestSupport.Packet() with
            {
                Repository = repository,
            },
            candidate,
            repository,
            maximumReviewAttempts
        ) with
        {
            ResumeRequested = true,
            CandidateSha = candidate,
            VerificationIndex = 1,
            VerificationResults = [PassedVerification()],
        };
        return state.RecordReviewDecision(decision);
    }

    private static ReviewDecision RequestChanges() =>
        new(
            ReviewDecisionValue.RequestChanges,
            "Repair",
            [new ReviewFinding(ReviewFindingSeverity.High, "Defect", "README.md:1")]
        );

    private static VerificationResult PassedVerification() =>
        new(0, "test", "true", 0, "", "", TimeSpan.Zero, false);
}
