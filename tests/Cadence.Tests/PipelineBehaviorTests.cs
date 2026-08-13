using Cadence.Git;
using Cadence.Host;
using FluentAssertions;
using Microsoft.Extensions.AI;

namespace Cadence.Tests;

public sealed class PipelineBehaviorTests
{
    [Fact]
    public async Task Valid_no_change_packet_reaches_an_accepted_verified_candidate()
    {
        var repository = TestSupport.CreateGitRepository();
        var workspace = Path.Combine(Path.GetTempPath(), $"cadence-no-change-{Guid.NewGuid():N}");
        try
        {
            var baseSha = TestSupport.Head(repository);
            var executor = new ScriptedChatClient(
                "executor",
                Read("executor-read"),
                AskPlanner("ask"),
                UpdateOutcomes("complete"),
                SubmitReport("report")
            );
            var planner = new ScriptedChatClient(
                "planner",
                Read("planner-read"),
                TestSupport.Text(PlannerDecisionJson(PlannerDecisionValue.Proceed))
            );
            var reviewer = NoChangeReviewer();
            var factory = BuildParticipants(profile =>
                profile switch
                {
                    "executor" => executor,
                    "planner" => planner,
                    "reviewer" => reviewer,
                    _ => throw new InvalidOperationException(profile),
                }
            );
            var packet = TestSupport.Packet() with
            {
                Repository = repository,
                Verification = ["test -f README.md"],
            };

            var result = await new PipelineRunner().RunAsync(
                new CadenceComposition(factory).Build(),
                CadenceState.Create(packet, baseSha, workspace),
                new PipelineRunOptions(Observer: new NoOpPersistenceObserver()),
                TestContext.Current.CancellationToken
            );

            result.Succeeded.Should().BeTrue();
            result.State.CandidateSha.Should().NotBe(baseSha);
            result.State.VerifiedCandidateSha.Should().Be(result.State.CandidateSha);
            TestSupport.Git(workspace, "diff", "--quiet", baseSha, result.State.CandidateSha!);
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Planner_approval_is_rejected_until_the_production_agent_inspects_the_repository()
    {
        var repository = TestSupport.CreateGitRepository();
        try
        {
            var skillDirectory = Path.Combine(repository, "skills", "meridian");
            Directory.CreateDirectory(skillDirectory);
            File.WriteAllText(
                Path.Combine(skillDirectory, "SKILL.md"),
                "---\nname: meridian\ndescription: Review doctrine.\n---\n\n# Meridian\n"
            );
            var planner = new ScriptedChatClient(
                "planner",
                TestSupport.Text(PlannerDecisionJson(PlannerDecisionValue.Proceed)),
                Read("planner-read"),
                TestSupport.Text(PlannerDecisionJson(PlannerDecisionValue.Proceed))
            );
            var participant = BuildParticipants(
                    _ => planner,
                    skills: [AgentSkill.FromDirectory(skillDirectory)]
                )
                .Create()
                .Planner;
            var state = TestSupport.State(repository) with
            {
                Packet = TestSupport.Packet() with { Repository = repository },
                ExecutorTransition = new ExecutorTransition.PlannerRequested(
                    new AskPlannerRequest(
                        PlannerQuestionType.ImplementationSurfaceReview,
                        "requested file",
                        "May I implement?",
                        "Implement the packet directly.",
                        ["README.md"]
                    )
                ),
            };

            var result = await new PipelineRunner().RunAsync(
                Pipeline.Start(participant, "planner-grounding-proof").Build(participant),
                state,
                cancellationToken: TestContext.Current.CancellationToken
            );

            result.State.PlannerDecision!.Decision.Should().Be(PlannerDecisionValue.Proceed);
            planner.CallCount.Should().Be(3);
            planner
                .AdvertisedTools.SelectMany(tools => tools)
                .Should()
                .Contain(["load_skill", "read_skill_resource"]);
            var advertised = planner.AdvertisedTools.SelectMany(tools => tools).ToHashSet();
            advertised
                .Should()
                .Contain([
                    "git_status",
                    "git_diff",
                    "git_log",
                    "git_show",
                    "git_blame",
                    "git_changed_files",
                    "git_compare",
                ]);
            advertised
                .Should()
                .NotContain([
                    "run_verification_1",
                    "run_shell",
                    "file_access_write",
                    "file_access_delete",
                    "file_access_replace",
                    "file_access_replace_lines",
                ]);
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Fact]
    public async Task Planner_typed_decision_reaches_record_store_without_reserialization()
    {
        var repository = TestSupport.CreateGitRepository();
        var recordDirectory = Path.Combine(
            Path.GetTempPath(),
            $"cadence-typed-records-{Guid.NewGuid():N}"
        );
        var recordPath = Path.Combine(recordDirectory, "records.json");
        try
        {
            var planner = new ScriptedChatClient(
                "planner",
                Read("planner-read"),
                TestSupport.Text(PlannerDecisionJson(PlannerDecisionValue.Proceed))
            );
            var store = new RunRecordStore(recordPath);
            var participant = BuildParticipants(_ => planner).Create().Planner;
            var state = TestSupport.State(repository) with
            {
                Packet = TestSupport.Packet() with { Repository = repository },
                ExecutorTransition = new ExecutorTransition.PlannerRequested(
                    new AskPlannerRequest(
                        PlannerQuestionType.ImplementationSurfaceReview,
                        "requested file",
                        "May I implement?",
                        "Implement the packet directly.",
                        ["README.md"]
                    )
                ),
            };

            var result = await new PipelineRunner().RunAsync(
                Pipeline.Start(participant, "planner-typed-record").Build(participant),
                state,
                new PipelineRunOptions(Observer: store),
                TestContext.Current.CancellationToken
            );

            result.State.PlannerDecision!.Decision.Should().Be(PlannerDecisionValue.Proceed);
            var context = await store.ReadContextAsync(
                CadenceLedgerRole.Reviewer,
                TestContext.Current.CancellationToken
            );
            var recorded = context.PlannerDecisions.Should().ContainSingle().Which;
            recorded.Decision.Should().Be(PlannerDecisionValue.Proceed);
            recorded.SafeNextAction.Should().Be("Implement through the inspected seam.");
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
            if (Directory.Exists(recordDirectory))
            {
                Directory.Delete(recordDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Reviewer_acceptance_is_rejected_until_the_production_agent_inspects_the_repository()
    {
        var repository = TestSupport.CreateGitRepository();
        try
        {
            var baseSha = TestSupport.Head(repository);
            File.AppendAllText(Path.Combine(repository, "README.md"), "candidate\n");
            TestSupport.Git(repository, "add", "README.md");
            TestSupport.Git(repository, "commit", "-m", "candidate");
            var candidateSha = TestSupport.Head(repository);
            var reviewer = new ScriptedChatClient(
                "reviewer",
                TestSupport.Text(AcceptJson()),
                GitChangedFiles("reviewer-changed-files", baseSha, candidateSha),
                GitDiff("reviewer-diff", baseSha, candidateSha),
                RunVerification("reviewer-verification", 1),
                TestSupport.Text(AcceptJson())
            );
            var participant = BuildParticipants(_ => reviewer).Create().Reviewer;
            var state = TestSupport.State(repository) with
            {
                Packet = TestSupport.Packet() with
                {
                    Repository = repository,
                    Commands = ["dotnet --info"],
                    Verification = ["dotnet --version"],
                },
                PinnedBaseSha = baseSha,
                CandidateSha = candidateSha,
                VerifiedCandidateSha = candidateSha,
                VerificationIndex = 1,
                VerificationResults =
                [
                    new VerificationResult(
                        0,
                        "dotnet --version",
                        0,
                        "passed",
                        "",
                        TimeSpan.Zero,
                        false
                    ),
                ],
            };

            var result = await new PipelineRunner().RunAsync(
                Pipeline.Start(participant, "reviewer-grounding-proof").Build(participant),
                state,
                cancellationToken: TestContext.Current.CancellationToken
            );

            reviewer.CallCount.Should().Be(5);
            result.State.ReviewerDecision!.Decision.Should().Be(ReviewDecisionValue.Accept);
            reviewer
                .AdvertisedTools.SelectMany(tools => tools)
                .Should()
                .Contain("run_verification_1");
            reviewer
                .AdvertisedTools.SelectMany(tools => tools)
                .Should()
                .NotContain("run_command_1");
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Fact]
    public async Task Preapproval_write_is_rejected_without_changing_the_workspace()
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"cadence-guard-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        try
        {
            var executor = new ScriptedChatClient(
                "executor",
                Write("blocked-write", "blocked.txt", "must not exist"),
                AskPlanner("valid-ask")
            );
            var participant = BuildParticipants(_ => executor).Create().Executor;
            var pipeline = Pipeline.Start(participant, "guard-proof").Build(participant);
            var state = TestSupport.State(workspace) with
            {
                Packet = TestSupport.Packet() with
                {
                    Commands = ["dotnet --version"],
                    Verification = ["dotnet --info"],
                },
            };

            var result = await new PipelineRunner().RunAsync(
                pipeline,
                state,
                cancellationToken: TestContext.Current.CancellationToken
            );

            result
                .State.ExecutorTransition.Should()
                .BeOfType<ExecutorTransition.PlannerRequested>();
            File.Exists(Path.Combine(workspace, "blocked.txt")).Should().BeFalse();
            executor.CallCount.Should().Be(2);
            executor
                .AdvertisedTools.SelectMany(tools => tools)
                .Should()
                .NotContain("run_command_1")
                .And.NotContain("run_verification_1");
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task Planner_authorization_exposes_exact_packet_commands_to_executor()
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"cadence-command-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        try
        {
            var executor = new ScriptedChatClient(
                "executor",
                TestSupport.ToolCall("command", "run_command_1", new Dictionary<string, object?>()),
                AskPlanner("finish")
            );
            var participant = BuildParticipants(_ => executor).Create().Executor;
            var state = TestSupport.State(workspace) with
            {
                Packet = TestSupport.Packet() with
                {
                    Commands = ["dotnet --version"],
                    Verification = ["dotnet --info"],
                },
            };
            state = state.RecordPlannerDecision(
                new PlannerDecision(
                    PlannerDecisionValue.Proceed,
                    "The command is required by the approved approach.",
                    [],
                    ["README.md"],
                    "Run the declared repository command."
                )
            );

            var result = await new PipelineRunner().RunAsync(
                Pipeline.Start(participant, "command-proof").Build(participant),
                state,
                cancellationToken: TestContext.Current.CancellationToken
            );

            result
                .State.ExecutorTransition.Should()
                .BeOfType<ExecutorTransition.PlannerRequested>();
            executor
                .AdvertisedTools.First()
                .Should()
                .Contain("run_command_1")
                .And.Contain("run_verification_1");
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task Request_changes_recaptures_and_reverifies_before_fresh_acceptance()
    {
        var repository = TestSupport.CreateGitRepository();
        var workspace = Path.Combine(Path.GetTempPath(), $"cadence-repair-{Guid.NewGuid():N}");
        try
        {
            var executor = new ScriptedChatClient(
                "executor",
                Read("executor-read"),
                AskPlanner("ask"),
                Write("initial", "feature.txt", "initial\n"),
                UpdateOutcomes("initial-complete"),
                SubmitReport("initial-report"),
                SubmitReport("direct-repair-report"),
                UpdateOutcomes("initial-complete"),
                SubmitReport("noop-repair-report"),
                Write("repair", "feature.txt", "repaired\n", true),
                UpdateOutcomes("repair-complete"),
                SubmitReport("repair-report")
            );
            var planner = new ScriptedChatClient(
                "planner",
                Read("planner-read"),
                TestSupport.Text(PlannerDecisionJson(PlannerDecisionValue.Proceed))
            );
            var reviewer = Reviewer(RequestChangesJson(), AcceptJson());
            var records = new FakeRecordSink();
            var factory = BuildParticipants(
                profile =>
                    profile switch
                    {
                        "executor" => executor,
                        "planner" => planner,
                        "reviewer" => reviewer,
                        _ => throw new InvalidOperationException(profile),
                    },
                records
            );
            var packet = TestSupport.Packet() with
            {
                Repository = repository,
                Verification = ["test -f feature.txt && grep -Eq 'initial|repaired' feature.txt"],
            };

            var result = await new PipelineRunner().RunAsync(
                new CadenceComposition(factory).Build(),
                CadenceState.Create(packet, TestSupport.Head(repository), workspace),
                new PipelineRunOptions(Observer: new NoOpPersistenceObserver()),
                cancellationToken: TestContext.Current.CancellationToken
            );

            result.Succeeded.Should().BeTrue();
            result.State.ReviewAttempt.Should().Be(1);
            result.State.ReviewRepairRequired.Should().BeFalse();
            executor.CallCount.Should().Be(11);
            File.ReadAllText(Path.Combine(workspace, "feature.txt")).Should().Be("repaired\n");
            records.VerificationResults.Should().HaveCount(2);
            records
                .VerificationResults.Select(x => x.CandidateSha)
                .Distinct()
                .Should()
                .HaveCount(2);
            records
                .VerificationResults.GroupBy(x => x.CandidateSha)
                .Should()
                .AllSatisfy(candidate =>
                    candidate.Select(x => x.Result.Command).Should().Equal(packet.Verification)
                );
        }
        finally
        {
            Directory.Delete(repository, true);
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, true);
            }
        }
    }

    [Fact]
    public async Task Red_verification_routes_through_the_production_graph_with_exact_executor_evidence()
    {
        var repository = TestSupport.CreateGitRepository();
        var workspace = Path.Combine(Path.GetTempPath(), $"cadence-red-{Guid.NewGuid():N}");
        try
        {
            const string command = "printf 'red-out'; printf 'red-error' >&2; exit 7";
            var executor = new ScriptedChatClient(
                "executor",
                Read("executor-read"),
                AskPlanner("ask"),
                Write("implementation", "feature.txt", "delivered\n"),
                UpdateOutcomes("complete"),
                SubmitReport("report"),
                AskPlanner("after-red")
            );
            var planner = new ScriptedChatClient(
                "planner",
                Read("planner-read"),
                TestSupport.Text(PlannerDecisionJson(PlannerDecisionValue.Proceed)),
                Read("planner-read-red"),
                TestSupport.Text(PlannerDecisionJson(PlannerDecisionValue.Stop))
            );
            var factory = BuildParticipants(profile => profile == "executor" ? executor : planner);
            var packet = TestSupport.Packet() with
            {
                Repository = repository,
                Verification = [command],
            };

            var result = await new PipelineRunner().RunAsync(
                new CadenceComposition(factory).Build(),
                CadenceState.Create(packet, TestSupport.Head(repository), workspace),
                new PipelineRunOptions(Observer: new NoOpPersistenceObserver()),
                TestContext.Current.CancellationToken
            );

            result.Succeeded.Should().BeFalse();
            result.State.VerificationResults.Should().ContainSingle();
            var redRequest = executor.Requests.Single(request =>
                request.Any(message => message.Text.Contains(command, StringComparison.Ordinal))
            );
            var evidence = string.Join("\n", redRequest.Select(message => message.Text));
            evidence.Should().Contain(command);
            evidence.Should().Contain("exit 7");
            evidence.Should().Contain("red-out");
            evidence.Should().Contain("red-error");
            executor.CallCount.Should().Be(6);
            result
                .State.ExecutorTransition.Should()
                .BeOfType<ExecutorTransition.PlannerRequested>()
                .Which.Request.Question.Should()
                .Be("May I implement the requested file?");
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Review_cap_uses_typed_human_answer_and_routes_continue_directly_to_executor(
        bool continueRepairs
    )
    {
        var repository = TestSupport.CreateGitRepository();
        var workspace = Path.Combine(Path.GetTempPath(), $"cadence-cap-{Guid.NewGuid():N}");
        try
        {
            var executorResponses = new List<ChatResponse>
            {
                Read("executor-read"),
                AskPlanner("ask"),
                Write("implementation", "feature.txt", "initial\n"),
                UpdateOutcomes("complete"),
                SubmitReport("report"),
            };
            if (continueRepairs)
            {
                executorResponses.Add(Write("repair", "feature.txt", "repaired\n", true));
                executorResponses.Add(UpdateOutcomes("repair-complete"));
                executorResponses.Add(SubmitReport("repair-report"));
            }
            var executor = new ScriptedChatClient("executor", executorResponses.ToArray());
            var planner = new ScriptedChatClient(
                "planner",
                Read("planner-read"),
                TestSupport.Text(PlannerDecisionJson(PlannerDecisionValue.Proceed))
            );
            var reviewerDecisions = new List<string> { RequestChangesJson() };
            if (continueRepairs)
            {
                reviewerDecisions.Add(AcceptJson());
            }
            var reviewer = Reviewer(reviewerDecisions.ToArray());
            var factory = BuildParticipants(profile =>
                profile switch
                {
                    "executor" => executor,
                    "planner" => planner,
                    "reviewer" => reviewer,
                    _ => throw new InvalidOperationException(profile),
                }
            );
            var composition = new CadenceComposition(factory);
            var observer = new RecordingPersistenceObserver();
            ReviewerHumanRequest? request = null;
            var interactions = new PipelineInteractionHandlers().Handle(
                composition.ReviewerHumanInput,
                (context, _) =>
                {
                    request = context.Request;
                    return ValueTask.FromResult<ReviewerHumanAnswer>(
                        continueRepairs
                            ? new ReviewerHumanAnswer.ContinueRepairs()
                            : new ReviewerHumanAnswer.Stop()
                    );
                }
            );
            var packet = TestSupport.Packet() with
            {
                Repository = repository,
                Verification = ["test -f feature.txt"],
            };

            var result = await new PipelineRunner().RunAsync(
                composition.Build(),
                CadenceState.Create(
                    packet,
                    TestSupport.Head(repository),
                    workspace,
                    maximumReviewAttempts: 1
                ),
                new PipelineRunOptions(Interactions: interactions, Observer: observer),
                TestContext.Current.CancellationToken
            );

            request.Should().BeOfType<ReviewerHumanRequest.RepairCap>();
            if (continueRepairs)
            {
                result.Succeeded.Should().BeTrue();
                executor.CallCount.Should().Be(8);
                var starts = observer
                    .Observations.OfType<PipelineStepStarted>()
                    .Select(observation => observation.StepId)
                    .ToArray();
                var human = Array.LastIndexOf(starts, "ReviewerHumanInput");
                human.Should().BeGreaterThanOrEqualTo(0);
                starts[human + 1].Should().Be(CadenceIds.Executor);
            }
            else
            {
                result.Succeeded.Should().BeFalse();
                executor.CallCount.Should().Be(5);
                result.State.ReviewerHumanResolution.Should().Be(ReviewerHumanResolution.Stop);
            }
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Dirty_checkpoint_lifecycle_retains_session_and_revokes_authority_until_planner_reapproves()
    {
        var started = DateTimeOffset.Parse("2026-08-11T10:00:00Z");
        var time = new FakeTimeProvider(started);
        var repository = TestSupport.CreateGitRepository();
        var workspace = Path.Combine(Path.GetTempPath(), $"cadence-checkpoint-{Guid.NewGuid():N}");
        try
        {
            var executor = new ScriptedChatClient(
                "executor",
                Read("executor-read"),
                AskPlanner("ask"),
                Write("first", "first.txt", "first\n"),
                Write("blocked", "blocked.txt", "blocked\n"),
                WriteCheckpoint("checkpoint", []),
                Write("blocked-after-checkpoint", "blocked-after.txt", "blocked\n"),
                AskPlanner("reapprove"),
                Write("after-approval", "after.txt", "after\n"),
                AskPlanner("final-ask")
            )
            {
                BeforeCall = call =>
                {
                    if (call == 4)
                    {
                        time.Value = started.AddMinutes(5);
                    }
                },
            };
            var planner = new ScriptedChatClient(
                "planner",
                Read("planner-read"),
                TestSupport.Text(PlannerDecisionJson(PlannerDecisionValue.Proceed)),
                Read("planner-read-reapprove"),
                TestSupport.Text(PlannerDecisionJson(PlannerDecisionValue.Proceed)),
                Read("planner-read-stop"),
                TestSupport.Text(PlannerDecisionJson(PlannerDecisionValue.Stop))
            );
            var records = new FakeRecordSink();
            var factory = BuildParticipants(
                profile => profile == "executor" ? executor : planner,
                records,
                time
            );

            var result = await new PipelineRunner().RunAsync(
                new CadenceComposition(factory).Build(),
                CadenceState.Create(
                    TestSupport.Packet() with
                    {
                        Repository = repository,
                    },
                    TestSupport.Head(repository),
                    workspace,
                    time
                ),
                new PipelineRunOptions(Observer: new NoOpPersistenceObserver()),
                TestContext.Current.CancellationToken
            );

            result.Succeeded.Should().BeFalse();
            File.Exists(Path.Combine(workspace, "first.txt")).Should().BeTrue();
            File.Exists(Path.Combine(workspace, "blocked.txt")).Should().BeFalse();
            File.Exists(Path.Combine(workspace, "blocked-after.txt")).Should().BeFalse();
            File.Exists(Path.Combine(workspace, "after.txt")).Should().BeTrue();
            records.Checkpoints.Should().ContainSingle();
            result.State.MutationAuthorized.Should().BeFalse();
            executor
                .Requests[5]
                .Should()
                .Contain(message =>
                    message
                        .Contents.OfType<FunctionCallContent>()
                        .Any(call => call.Name == "write_checkpoint")
                );
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Token_threshold_accepts_checkpoint_and_rotates_to_a_fresh_executor_session()
    {
        var repository = TestSupport.CreateGitRepository();
        var workspace = Path.Combine(Path.GetTempPath(), $"cadence-token-{Guid.NewGuid():N}");
        try
        {
            var highUsage = TestSupport.Text("Still working.");
            highUsage.Usage = new UsageDetails { InputTokenCount = 61, OutputTokenCount = 1 };
            var executor = new ScriptedChatClient(
                "executor",
                highUsage,
                WriteCheckpoint("token-checkpoint"),
                AskPlanner("fresh-session-ask")
            );
            var planner = new ScriptedChatClient(
                "planner",
                Read("planner-read"),
                TestSupport.Text(PlannerDecisionJson(PlannerDecisionValue.Stop))
            );
            var records = new FakeRecordSink();
            var factory = BuildParticipants(
                profile => profile == "executor" ? executor : planner,
                records,
                TimeProvider.System,
                _ => new CadenceAgentProfile(100, 20, 80)
            );

            var result = await new PipelineRunner().RunAsync(
                new CadenceComposition(factory).Build(),
                CadenceState.Create(
                    TestSupport.Packet() with
                    {
                        Repository = repository,
                    },
                    TestSupport.Head(repository),
                    workspace
                ),
                new PipelineRunOptions(Observer: new NoOpPersistenceObserver()),
                TestContext.Current.CancellationToken
            );

            result.Succeeded.Should().BeFalse();
            records.Checkpoints.Should().ContainSingle();
            result.State.LatestCheckpoint!.Summary.Should().Be("Durable checkpoint");
            executor.CallCount.Should().Be(3);
            executor
                .Requests[2]
                .Should()
                .NotContain(message => message.Text.Contains("Still working."));
            executor
                .Requests[2]
                .Should()
                .NotContain(message =>
                    message
                        .Contents.OfType<FunctionCallContent>()
                        .Any(call => call.Name == "write_checkpoint")
                );
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Every_checkpoint_revokes_authority_and_routes_executor_to_planner(
        bool hasUncertainties
    )
    {
        var workspace = Path.Combine(
            Path.GetTempPath(),
            $"cadence-checkpoint-auth-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(workspace);
        try
        {
            var executor = new ScriptedChatClient(
                "executor",
                WriteCheckpoint(
                    "checkpoint",
                    hasUncertainties ? ["The integration owner is unclear."] : []
                ),
                AskPlanner("required-reapproval")
            );
            var participant = BuildParticipants(_ => executor).Create().Executor;
            var state = TestSupport
                .State(workspace)
                .RecordPlannerDecision(
                    new PlannerDecision(
                        PlannerDecisionValue.ProceedWithConstraints,
                        "The current approach is approved.",
                        ["Preserve the public contract."],
                        ["src/a.cs"],
                        "Continue through the approved seam."
                    )
                );

            var result = await new PipelineRunner().RunAsync(
                Pipeline.Start(participant, "checkpoint-auth-proof").Build(participant),
                state,
                cancellationToken: TestContext.Current.CancellationToken
            );

            result.State.MutationAuthorized.Should().BeFalse();
            result.State.PlannerConstraints.Should().Equal("Preserve the public contract.");
            result
                .State.ExecutorTransition.Should()
                .BeOfType<ExecutorTransition.PlannerRequested>();
            executor.CallCount.Should().Be(2);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task Invalid_ask_planner_stays_with_executor_for_correction()
    {
        var workspace = Path.Combine(Path.GetTempPath(), $"cadence-correction-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        try
        {
            var executor = new ScriptedChatClient(
                "executor",
                TestSupport.ToolCall(
                    "invalid-ask",
                    "ask_planner",
                    new Dictionary<string, object?>
                    {
                        ["question"] = "",
                        ["proposedApproach"] = "",
                        ["evidence"] = Array.Empty<string>(),
                    }
                ),
                AskPlanner("corrected-ask")
            );
            var participant = BuildParticipants(_ => executor).Create().Executor;

            var result = await new PipelineRunner().RunAsync(
                Pipeline.Start(participant, "correction-proof").Build(participant),
                TestSupport.State(workspace),
                cancellationToken: TestContext.Current.CancellationToken
            );

            result
                .State.ExecutorTransition.Should()
                .BeOfType<ExecutorTransition.PlannerRequested>();
            executor.CallCount.Should().Be(2);
        }
        finally
        {
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task Reorient_discards_prior_conversation_and_authorizes_the_fresh_executor()
    {
        var repository = TestSupport.CreateGitRepository();
        var workspace = Path.Combine(Path.GetTempPath(), $"cadence-reorient-{Guid.NewGuid():N}");
        try
        {
            var executor = new ScriptedChatClient(
                "executor",
                TestSupport.Text("POISONED SESSION MARKER"),
                AskPlanner("reorient-ask", PlannerQuestionType.SessionReliability),
                Write("write-after-reorient", "reoriented.txt", "approved\n"),
                AskPlanner("stop", PlannerQuestionType.ImplementationSurfaceReview)
            );
            var planner = new ScriptedChatClient(
                "planner",
                Read("planner-read-reorient"),
                TestSupport.Text(PlannerDecisionJson(PlannerDecisionValue.Reorient)),
                Read("planner-read-stop"),
                TestSupport.Text(PlannerDecisionJson(PlannerDecisionValue.Stop))
            );
            var records = new FakeRecordSink
            {
                Context = new CadenceLedgerContext(
                    new OutcomeProgressDocument(
                        "packet",
                        [
                            new OutcomeProgress(
                                "outcome-1",
                                "Deliver the feature",
                                OutcomeStatus.InProgress,
                                ["ledger evidence"],
                                "Durable implementation state",
                                "Durable next action"
                            ),
                        ]
                    ),
                    null,
                    new ProgressCheckpointRecord(
                        "Durable checkpoint summary",
                        ["durable.txt"],
                        ["Preserve durable constraint"],
                        ["Durable uncertainty"],
                        "Ask Planner"
                    ),
                    ["Preserve durable constraint"],
                    [
                        new PlannerDecision(
                            PlannerDecisionValue.ProceedWithConstraints,
                            "Durable prior decision",
                            ["Preserve durable constraint"],
                            ["src/a.cs"],
                            "Continue safely."
                        ),
                    ],
                    [],
                    [],
                    []
                ),
            };
            var factory = BuildParticipants(
                profile => profile == "executor" ? executor : planner,
                records
            );
            var state = TestSupport.State(workspace) with
            {
                Packet = TestSupport.Packet() with { Repository = repository },
                PlannerConstraints = ["Preserve durable constraint"],
            };

            var result = await new PipelineRunner().RunAsync(
                new CadenceComposition(factory).Build(),
                state,
                new PipelineRunOptions(Observer: new NoOpPersistenceObserver()),
                TestContext.Current.CancellationToken
            );

            result.Succeeded.Should().BeFalse();
            File.Exists(Path.Combine(workspace, "reoriented.txt")).Should().BeTrue();
            result.State.PlannerConstraints.Should().Equal("Preserve durable constraint");
            result.State.MutationAuthorized.Should().BeFalse();
            executor
                .Requests[2]
                .Should()
                .NotContain(message => message.Text.Contains("POISONED SESSION MARKER"));
            var freshContext = string.Join(
                "\n",
                executor.Requests[2].Select(message => message.Text)
            );
            freshContext.Should().Contain("Durable implementation state");
            freshContext.Should().Contain("Durable checkpoint summary");
            freshContext.Should().Contain("Preserve durable constraint");
            freshContext.Should().Contain("Durable prior decision");
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Public_runner_reaches_review_ready_through_configured_agents_and_retains_executor_session()
    {
        var repository = TestSupport.CreateGitRepository();
        var workspace = Path.Combine(Path.GetTempPath(), $"cadence-happy-{Guid.NewGuid():N}");
        try
        {
            var executor = new ScriptedChatClient(
                "executor",
                Read("executor-read"),
                AskPlanner("ask"),
                Write("implementation", "feature.txt", "delivered\n"),
                UpdateOutcomes("complete"),
                SubmitReport("report")
            );
            var planner = new ScriptedChatClient(
                "planner",
                Read("planner-read"),
                TestSupport.Text(PlannerDecisionJson(PlannerDecisionValue.Proceed))
            );
            var reviewer = Reviewer(AcceptJson());
            var records = new FakeRecordSink();
            var factory = BuildParticipants(
                profile =>
                    profile switch
                    {
                        "executor" => executor,
                        "planner" => planner,
                        "reviewer" => reviewer,
                        _ => throw new InvalidOperationException(profile),
                    },
                records
            );
            var packet = TestSupport.Packet() with
            {
                Repository = repository,
                Verification = ["test -f feature.txt && grep -q delivered feature.txt"],
            };

            var result = await new PipelineRunner().RunAsync(
                new CadenceComposition(factory).Build(),
                CadenceState.Create(packet, TestSupport.Head(repository), workspace),
                new PipelineRunOptions(Observer: new NoOpPersistenceObserver()),
                cancellationToken: TestContext.Current.CancellationToken
            );

            result.Succeeded.Should().BeTrue();
            result.State.CandidateSha.Should().NotBeNullOrWhiteSpace();
            result.State.VerifiedCandidateSha.Should().Be(result.State.CandidateSha);
            result.State.ReviewerCandidateSha.Should().Be(result.State.CandidateSha);
            result.State.ReviewerDecision!.Decision.Should().Be(ReviewDecisionValue.Accept);
            records.Candidate!.CandidateSha.Should().Be(result.State.CandidateSha);
            executor
                .Requests[2]
                .Should()
                .Contain(message =>
                    message
                        .Contents.OfType<FunctionCallContent>()
                        .Any(call => call.Name == "ask_planner")
                );
            planner.AdvertisedTools.SelectMany(tools => tools).Should().Contain("file_access_read");
            reviewer
                .AdvertisedTools.SelectMany(tools => tools)
                .Should()
                .Contain("file_access_read");
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }

    private static CadenceParticipantsFactory BuildParticipants(
        Func<string, IChatClient> clients,
        FakeRecordSink? records = null,
        TimeProvider? timeProvider = null,
        Func<string, CadenceAgentProfile>? profiles = null,
        IReadOnlyList<AgentSkill>? skills = null
    )
    {
        records ??= new FakeRecordSink();
        timeProvider ??= TimeProvider.System;
        profiles ??= _ => new CadenceAgentProfile(200_000, 32_000, 80);
        var git = new GitProcess();
        var checkpoint = new DirtyWorkCheckpointPolicy(git, timeProvider);
        var capabilities = CadenceCapabilities.Create(
            new CheckpointAcceptance(git, records),
            records,
            timeProvider,
            checkpoint
        );
        return new CadenceParticipantsFactory(
            clients,
            profiles,
            records,
            TestSupport.Doctrine(),
            skills ?? [],
            new WorkspacePreparation(git),
            git,
            checkpoint,
            capabilities.AskPlanner,
            capabilities.UpdateOutcomes,
            capabilities.SubmitReport,
            capabilities.WriteCheckpoint
        );
    }

    private static ChatResponse Read(string id, string fileName = "README.md") =>
        TestSupport.ToolCall(
            id,
            "file_access_read",
            new Dictionary<string, object?> { ["fileName"] = fileName }
        );

    private static ChatResponse GitChangedFiles(string id, string baseSha, string candidateSha) =>
        TestSupport.ToolCall(
            id,
            "git_changed_files",
            new Dictionary<string, object?>
            {
                ["baseSha"] = baseSha,
                ["candidateSha"] = candidateSha,
            }
        );

    private static ChatResponse GitDiff(
        string id,
        string baseSha,
        string candidateSha,
        string? path = null
    )
    {
        var arguments = new Dictionary<string, object?>
        {
            ["baseSha"] = baseSha,
            ["candidateSha"] = candidateSha,
        };
        if (path is not null)
        {
            arguments["path"] = path;
        }
        return TestSupport.ToolCall(id, "git_diff", arguments);
    }

    private static ScriptedChatClient Reviewer(params string[] decisions)
    {
        var responses = new List<Func<IReadOnlyList<ChatMessage>, ChatResponse>>();
        foreach (var decision in decisions)
        {
            responses.Add(messages =>
            {
                var (baseSha, candidateSha) = CandidateRange(messages);
                return GitChangedFiles($"changed-{Guid.NewGuid():N}", baseSha, candidateSha);
            });
            responses.Add(messages =>
            {
                var (baseSha, candidateSha) = CandidateRange(messages);
                return GitDiff($"diff-{Guid.NewGuid():N}", baseSha, candidateSha);
            });
            responses.Add(_ => RunVerification($"verification-{Guid.NewGuid():N}", 1));
            responses.Add(_ => TestSupport.Text(decision));
        }
        return ScriptedChatClient.Dynamic("reviewer", responses.ToArray());
    }

    private static ScriptedChatClient NoChangeReviewer() =>
        ScriptedChatClient.Dynamic(
            "reviewer",
            messages =>
            {
                var (baseSha, candidateSha) = CandidateRange(messages);
                return GitChangedFiles("changed-no-change", baseSha, candidateSha);
            },
            messages =>
            {
                var (baseSha, candidateSha) = CandidateRange(messages);
                return GitDiff("diff-no-change", baseSha, candidateSha);
            },
            _ => Read("review-existing", "README.md"),
            _ => RunVerification("verification-no-change", 1),
            _ => TestSupport.Text(AcceptJson())
        );

    private static ChatResponse RunVerification(string id, int index) =>
        TestSupport.ToolCall(id, $"run_verification_{index}", new Dictionary<string, object?>());

    private static (string BaseSha, string CandidateSha) CandidateRange(
        IReadOnlyList<ChatMessage> messages
    )
    {
        var text = string.Join('\n', messages.Select(message => message.Text));
        return (ExtractSha(text, "Pinned base: "), ExtractSha(text, "Candidate SHA: "));
    }

    private static string ExtractSha(string text, string marker)
    {
        var start = text.LastIndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException($"Reviewer prompt omitted '{marker.Trim()}'.");
        }
        start += marker.Length;
        var end = text.IndexOf('\n', start);
        return text[start..(end < 0 ? text.Length : end)].Trim();
    }

    private static ChatResponse Write(
        string id,
        string fileName,
        string content,
        bool overwrite = false
    ) =>
        TestSupport.ToolCall(
            id,
            "file_access_write",
            new Dictionary<string, object?>
            {
                ["fileName"] = fileName,
                ["content"] = content,
                ["overwrite"] = overwrite,
            }
        );

    private static ChatResponse AskPlanner(
        string id,
        PlannerQuestionType questionType = PlannerQuestionType.ImplementationSurfaceReview
    ) =>
        TestSupport.ToolCall(
            id,
            "ask_planner",
            new Dictionary<string, object?>
            {
                ["question"] = "May I implement the requested file?",
                ["questionType"] = questionType.ToString(),
                ["currentSlice"] = "requested file",
                ["proposedApproach"] = "Create feature.txt and verify its content.",
                ["evidence"] = new[] { "README.md establishes the repository baseline." },
            }
        );

    private static ChatResponse WriteCheckpoint(
        string id,
        IReadOnlyList<string>? uncertainties = null
    ) =>
        TestSupport.ToolCall(
            id,
            "write_checkpoint",
            new Dictionary<string, object?>
            {
                ["summary"] = "Durable checkpoint",
                ["uncertainties"] = uncertainties ?? ["Continuity is uncertain."],
                ["nextAction"] = "Continue implementation.",
            }
        );

    private static ChatResponse SubmitReport(string id) =>
        TestSupport.ToolCall(
            id,
            "submit_report",
            new Dictionary<string, object?>
            {
                ["summary"] = "Implemented the requested feature file.",
                ["addressedConstraints"] = Array.Empty<object>(),
                ["regressionTests"] = new Dictionary<string, object?>
                {
                    ["disposition"] = RegressionTestDisposition.Added.ToString(),
                    ["evidence"] = new[] { "The configured verification exercises feature.txt." },
                },
            }
        );

    private static ChatResponse UpdateOutcomes(string id) =>
        TestSupport.ToolCall(
            id,
            "update_outcomes",
            new Dictionary<string, object?>
            {
                ["updates"] = new object[]
                {
                    new Dictionary<string, object?>
                    {
                        ["outcomeId"] = "outcome-1",
                        ["status"] = OutcomeStatus.Complete.ToString(),
                        ["evidence"] = new[]
                        {
                            $"feature.txt contains the requested behavior ({id}).",
                        },
                        ["implementationState"] = $"The requested behavior is implemented ({id}).",
                        ["nextAction"] = null,
                    },
                },
            }
        );

    private static string PlannerDecisionJson(PlannerDecisionValue decision) =>
        $$"""
            {"decision":"{{decision}}","rationale":"Repository inspection confirms the direct implementation is safe.","constraints":[],"evidenceUsed":["README.md"],"safeNextAction":"Implement through the inspected seam.","correctedApproach":{{(
                decision is PlannerDecisionValue.ReviseApproach or PlannerDecisionValue.Reorient
                    ? "\"Use the durable repository state through the inspected seam.\""
                    : "null"
            )}},"humanQuestion":null,"humanDecisionDomain":null}
            """;

    private static string AcceptJson() =>
        $$"""
            {"decision":"Accept","doctrineHash":"{{TestSupport.Doctrine().Sha256}}","summary":"The verified candidate delivers the requested feature.","outcomes":[{"outcomeId":"outcome-1","delivered":true,"evidence":[{"kind":"FileLine","path":"feature.txt","line":1}]}],"findings":[],"constraintAssessments":[],"humanQuestion":null,"humanDecisionDomain":null}
            """;

    private static string RequestChangesJson() =>
        $$"""
            {"decision":"RequestChanges","doctrineHash":"{{TestSupport.Doctrine().Sha256}}","summary":"The candidate requires a specific repair.","outcomes":[{"outcomeId":"outcome-1","delivered":false,"evidence":[{"kind":"FileLine","path":"feature.txt","line":1}]}],"findings":[{"severity":"High","description":"Replace the initial content with repaired content.","evidence":[{"kind":"DoctrineClause","doctrineClause":"Correctness over taste."},{"kind":"FileLine","path":"feature.txt","line":1}]}],"constraintAssessments":[],"humanQuestion":null,"humanDecisionDomain":null}
            """;
}
