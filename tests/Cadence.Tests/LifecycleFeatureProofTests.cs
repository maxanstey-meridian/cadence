using Cadence.Git;
using FluentAssertions;
using Microsoft.Extensions.AI;

namespace Cadence.Tests;

public sealed class LifecycleFeatureProofTests
{
    [Theory(Timeout = 30_000)]
    [InlineData("fresh-executor")]
    [InlineData("verification")]
    [InlineData("reviewer")]
    [InlineData("candidate-acceptance")]
    public async Task Pending_operator_instruction_preempts_production_recovery_routes(
        string recovery
    )
    {
        var repository = TestSupport.CreateGitRepository();
        var runRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(runRoot, "workspace");
        try
        {
            Directory.CreateDirectory(runRoot);
            TestSupport.Git(runRoot, "clone", repository, workspace);
            TestSupport.Git(workspace, "remote", "remove", "origin");
            var candidate = TestSupport.Head(repository);
            var packet = TestSupport.Packet() with
            {
                Repository = repository,
                Verification = [new PacketCommand("test", "true")],
            };
            var state = CadenceState.Create(packet, candidate, workspace) with
            {
                ResumeRequested = true,
                OperatorInstruction = "Preserve inherited work.",
                OperatorInstructionPending = true,
            };
            state = recovery switch
            {
                "fresh-executor" => state,
                "verification" => state with { CandidateSha = candidate },
                "reviewer" => state with
                {
                    CandidateSha = candidate,
                    VerificationIndex = 1,
                    VerificationResults =
                    [
                        new VerificationResult(0, "test", "true", 0, "", "", TimeSpan.Zero, false),
                    ],
                },
                "candidate-acceptance" => state with
                {
                    CandidateSha = candidate,
                    VerificationIndex = 1,
                    VerificationResults =
                    [
                        new VerificationResult(0, "test", "true", 0, "", "", TimeSpan.Zero, false),
                    ],
                    ReviewerDecision = TestContracts.Review(
                        ReviewDecisionValue.Accept,
                        "Accepted",
                        []
                    ),
                },
                _ => throw new ArgumentOutOfRangeException(nameof(recovery)),
            };
            var planner = new ScriptedChatClient(
                "planner",
                Read("planner-read"),
                TestSupport.Text(PlannerJson(PlannerDecisionValue.Stop))
            );
            var otherRole = new ScriptedChatClient("other-role");

            var result = await new PipelineRunner().RunAsync(
                new CadenceComposition(Factory(x => x == "planner" ? planner : otherRole)).Build(),
                state,
                new PipelineRunOptions(Observer: new NoOpPersistenceObserver()),
                TestContext.Current.CancellationToken
            );

            result.Succeeded.Should().BeFalse();
            planner.CallCount.Should().Be(2);
            otherRole.CallCount.Should().Be(0);
            File.Exists(Path.Combine(workspace, "README.md")).Should().BeTrue();
        }
        finally
        {
            Directory.Delete(repository, true);
            if (Directory.Exists(runRoot))
            {
                Directory.Delete(runRoot, true);
            }
        }
    }

    [Fact(Timeout = 15_000)]
    public async Task Simple_report_captures_candidate_without_copied_packet_facts()
    {
        await InRepository(async repository =>
        {
            File.WriteAllText(Path.Combine(repository, "feature.txt"), "done\n");
            var state = CandidateState(repository)
                .RecordImplementationReport(TestContracts.Report("Ready", "feature"));

            var result = await new CaptureCandidateStage(new GitProcess()).ExecuteAsync(
                state,
                TestContext.Current.CancellationToken
            );

            var captured = result.Should().BeOfType<Outcome<CadenceState>.Success>().Subject.State;
            captured.CandidateSha.Should().Be(TestSupport.Head(repository));
            captured.VerificationResults.Should().BeEmpty();
        });
    }

    [Fact(Timeout = 15_000)]
    public async Task Initial_no_change_creates_allow_empty_candidate()
    {
        await InRepository(async repository =>
        {
            var original = TestSupport.Head(repository);
            var state = CandidateState(repository)
                .RecordImplementationReport(
                    TestContracts.Report("Already satisfied", "verify existing behavior")
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

    [Fact(Timeout = 15_000)]
    public async Task Initial_no_change_candidate_passes_verification_and_acceptance_stages()
    {
        await InRepository(async repository =>
        {
            var packet = TestSupport.Packet() with
            {
                Repository = repository,
                Verification = [new PacketCommand("existing-behavior", "test -f README.md")],
            };
            var state = CadenceState
                .Create(packet, TestSupport.Head(repository), repository)
                .RecordImplementationReport(
                    TestContracts.Report("Already satisfied", "verify existing behavior")
                );
            var captured = await new CaptureCandidateStage(new GitProcess()).ExecuteAsync(
                state,
                TestContext.Current.CancellationToken
            );
            state = captured.Should().BeOfType<Outcome<CadenceState>.Success>().Subject.State;

            var verificationStage = new VerificationStage(
                new VerificationOperation(new GitProcess())
            );
            var verificationRun = await new PipelineRunner().RunAsync(
                Pipeline.Start(verificationStage, "initial-no-change").Build(verificationStage),
                state,
                cancellationToken: TestContext.Current.CancellationToken
            );
            state = verificationRun.State;
            state
                .VerificationResults.Should()
                .ContainSingle(result => result.ExitCode == 0 && !result.TimedOut);
            state = state.RecordReviewDecision(
                TestContracts.Review(
                    ReviewDecisionValue.Accept,
                    "Accepted",
                    DeliveryObligations
                        .From(state)
                        .Select(x => new ReviewAssessment(x.Reference, true, "Verified."))
                        .ToArray(),
                    []
                )
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

    [Fact(Timeout = 15_000)]
    public async Task Unchanged_repair_is_rejected_but_changed_tree_is_captured()
    {
        await InRepository(async repository =>
        {
            var finding = new ReviewFinding(
                ReviewFindingSeverity.High,
                "Behavior is wrong",
                "README.md:1"
            );
            var rejected = CandidateState(repository)
                .RecordReviewDecision(
                    TestContracts.Review(ReviewDecisionValue.RequestChanges, "Repair", [finding])
                )
                .RecordImplementationReport(TestContracts.Report("Repaired", "repair"));

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

    [Fact(Timeout = 15_000)]
    public async Task Fresh_executor_requires_initial_contact_and_planner_authorization_before_mutation()
    {
        var repository = TestSupport.CreateGitRepository();
        var runRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var workspace = Path.Combine(runRoot, "workspace");
        try
        {
            var executor = new ScriptedChatClient(
                "executor",
                Ask("ungrounded"),
                Read("executor-read"),
                Ask("grounded"),
                Write("authorized", "authorized.txt", "ok\n"),
                Ask("changed-approach"),
                Write("reauthorized", "reauthorized.txt", "ok\n"),
                Ask("stop")
            );
            var planner = new ScriptedChatClient(
                "planner",
                Read("planner-read"),
                TestSupport.Text(PlannerJson(PlannerDecisionValue.Proceed)),
                Read("changed-read"),
                TestSupport.Text(PlannerJson(PlannerDecisionValue.Proceed)),
                Read("stop-read"),
                TestSupport.Text(PlannerJson(PlannerDecisionValue.Stop))
            );
            var observer = new RecordingPersistenceObserver();

            var result = await new PipelineRunner().RunAsync(
                new CadenceComposition(Factory(x => x == "executor" ? executor : planner)).Build(),
                CadenceState.Create(
                    TestSupport.Packet() with
                    {
                        Repository = repository,
                    },
                    TestSupport.Head(repository),
                    workspace
                ),
                new PipelineRunOptions(Observer: observer),
                TestContext.Current.CancellationToken
            );

            result.Succeeded.Should().BeFalse();
            File.ReadAllText(Path.Combine(workspace, "authorized.txt")).Should().Be("ok\n");
            File.ReadAllText(Path.Combine(workspace, "reauthorized.txt")).Should().Be("ok\n");
            var writeStarts = observer
                .Observations.OfType<PipelineAgentUpdated>()
                .Select(update => update.Update)
                .OfType<AgentUpdate.ToolStarted>()
                .Where(started => started.Name == "file_access_write")
                .ToArray();
            writeStarts.Should().HaveCount(2);
            writeStarts[0]
                .Arguments.GetProperty("fileName")
                .GetString()
                .Should()
                .Be("authorized.txt");
            writeStarts[0].Arguments.GetProperty("content").GetString().Should().Be("ok\n");
            writeStarts[0].Arguments.GetProperty("overwrite").GetBoolean().Should().BeFalse();
            observer
                .Observations.OfType<PipelineActionAttempted>()
                .Where(action =>
                    action.ActionName == "file_access_write" && action.Effect == "WorkspaceMutation"
                )
                .Should()
                .HaveCount(2);
            observer
                .Observations.OfType<PipelineActionCompleted>()
                .Where(action =>
                    action.ActionName == "file_access_write"
                    && action.Effect == "WorkspaceMutation"
                    && action.Result == "Completed"
                )
                .Should()
                .HaveCount(2);
            executor.CallCount.Should().Be(7);
            planner.CallCount.Should().Be(6);
            executor
                .Requests[3]
                .SelectMany(message => message.Contents.OfType<FunctionCallContent>())
                .Should()
                .Contain(call => call.CallId == "executor-read");
        }
        finally
        {
            Directory.Delete(repository, true);
            if (Directory.Exists(runRoot))
            {
                Directory.Delete(runRoot, true);
            }
        }
    }

    [Fact(Timeout = 15_000)]
    public async Task Planner_rejects_a_decision_when_no_repository_evidence_was_examined()
    {
        var repository = TestSupport.CreateGitRepository();
        try
        {
            var planner = new ScriptedChatClient(
                "planner",
                TestSupport.Text(PlannerJson(PlannerDecisionValue.Proceed)),
                Read("planner-read"),
                TestSupport.Text(PlannerJson(PlannerDecisionValue.Proceed))
            );
            var participant = Factory(_ => planner).Create().Planner;
            var state = TestSupport.State(repository) with
            {
                ExecutorTransition = new ExecutorTransition.PlannerRequested(
                    new("slice", "Proceed?", "Implement directly.", ["README.md"])
                ),
            };

            var result = await new PipelineRunner().RunAsync(
                Pipeline.Start(participant, "planner-grounding").Build(participant),
                state,
                cancellationToken: TestContext.Current.CancellationToken
            );

            planner.CallCount.Should().Be(3);
            result.State.MutationAuthorized.Should().BeTrue();
            planner
                .Requests[1]
                .Select(message => message.Text)
                .Should()
                .Contain(text =>
                    text.Contains(
                        "You have not examined any repository evidence",
                        StringComparison.Ordinal
                    )
                    && text.Contains(
                        "whether the proposed engineering direction is sufficient",
                        StringComparison.Ordinal
                    )
                );
        }
        finally
        {
            Directory.Delete(repository, true);
        }
    }

    [Fact(Timeout = 15_000)]
    public async Task Planner_rejects_runtime_capability_availability_as_human_permissions()
    {
        var repository = TestSupport.CreateGitRepository();
        try
        {
            var planner = new ScriptedChatClient(
                "planner",
                TestSupport.Text(
                    "{\"decision\":\"NeedsHuman\",\"rationale\":\"The authorized command capability is unavailable.\",\"constraints\":[],\"evidenceUsed\":[\"Current packet commands\"],\"safeNextAction\":\"Provide the command capability.\",\"correctedApproach\":null,\"humanQuestion\":\"Can the command capability be exposed?\",\"humanDecisionDomain\":\"Permissions\"}"
                ),
                Read("read"),
                TestSupport.Text(PlannerJson(PlannerDecisionValue.Proceed))
            );
            var participant = Factory(_ => planner).Create().Planner;
            var state = TestSupport.State(repository) with
            {
                ExecutorTransition = new ExecutorTransition.PlannerRequested(
                    new("slice", "Proceed?", "Use packet commands.", ["README.md"])
                ),
            };

            var result = await new PipelineRunner().RunAsync(
                Pipeline.Start(participant, "planner-human-boundary").Build(participant),
                state,
                cancellationToken: TestContext.Current.CancellationToken
            );

            planner.CallCount.Should().Be(3);
            result.State.MutationAuthorized.Should().BeTrue();
            result.State.PlannerDecision!.Decision.Should().Be(PlannerDecisionValue.Proceed);
        }
        finally
        {
            Directory.Delete(repository, true);
        }
    }

    [Fact(Timeout = 15_000)]
    public async Task Planner_accepts_product_permission_policy_as_human_owned()
    {
        var repository = TestSupport.CreateGitRepository();
        try
        {
            var planner = new ScriptedChatClient(
                "planner",
                TestSupport.Text(
                    "{\"decision\":\"NeedsHuman\",\"rationale\":\"The packet does not decide the Member access policy.\",\"constraints\":[],\"evidenceUsed\":[\"Current authorization policy\"],\"safeNextAction\":\"Obtain the intended Member access policy.\",\"correctedApproach\":null,\"humanQuestion\":\"Should Members be permitted to view Cases?\",\"humanDecisionDomain\":\"Permissions\"}"
                ),
                Read("policy-read"),
                TestSupport.Text(
                    "{\"decision\":\"NeedsHuman\",\"rationale\":\"The packet does not decide the Member access policy.\",\"constraints\":[],\"evidenceUsed\":[\"Current authorization policy\"],\"safeNextAction\":\"Obtain the intended Member access policy.\",\"correctedApproach\":null,\"humanQuestion\":\"Should Members be permitted to view Cases?\",\"humanDecisionDomain\":\"Permissions\"}"
                )
            );
            var participant = Factory(_ => planner).Create().Planner;

            var result = await new PipelineRunner().RunAsync(
                Pipeline.Start(participant, "planner-product-permissions").Build(participant),
                TestSupport.State(repository),
                cancellationToken: TestContext.Current.CancellationToken
            );

            planner.CallCount.Should().Be(3);
            result.State.PlannerDecision!.Decision.Should().Be(PlannerDecisionValue.NeedsHuman);
            result
                .State.PlannerDecision.HumanDecisionDomain.Should()
                .Be(HumanDecisionDomain.Permissions);
        }
        finally
        {
            Directory.Delete(repository, true);
        }
    }

    [Fact(Timeout = 15_000)]
    public async Task Dirty_checkpoint_routes_directly_to_planner_before_executor_continues()
    {
        var started = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var time = new MutableTimeProvider(started);
        var repository = TestSupport.CreateGitRepository();
        var workspace = Path.Combine(Path.GetTempPath(), $"cadence-dirty-{Guid.NewGuid():N}");
        try
        {
            var executor = new ScriptedChatClient(
                "executor",
                Read("initial-executor-read"),
                Ask("initial-executor-ask"),
                Write("first", "first.txt", "first\n"),
                Write("blocked", "blocked.txt", "blocked\n"),
                Ask("bypass-planner"),
                Write("still-blocked", "still-blocked.txt", "blocked\n"),
                Report("bypass"),
                Checkpoint("checkpoint"),
                Write("after", "after.txt", "after\n"),
                Ask("stop")
            )
            {
                BeforeCall = call =>
                {
                    if (call == 4)
                    {
                        time.Now = started.AddMinutes(5);
                    }
                },
            };
            var planner = new ScriptedChatClient(
                "planner",
                Read("initial"),
                TestSupport.Text(PlannerJson(PlannerDecisionValue.Proceed)),
                Read("bypass-approve"),
                TestSupport.Text(PlannerJson(PlannerDecisionValue.Proceed)),
                Read("checkpoint-reapprove"),
                TestSupport.Text(PlannerJson(PlannerDecisionValue.Proceed)),
                Read("stop-read"),
                TestSupport.Text(PlannerJson(PlannerDecisionValue.Stop))
            );
            var result = await new PipelineRunner().RunAsync(
                new CadenceComposition(
                    Factory(x => x == "executor" ? executor : planner, time: time)
                ).Build(),
                CadenceState.Create(
                    TestSupport.Packet() with
                    {
                        Repository = repository,
                    },
                    TestSupport.Head(repository),
                    workspace,
                    timeProvider: time
                ),
                new PipelineRunOptions(Observer: new NoOpPersistenceObserver()),
                TestContext.Current.CancellationToken
            );

            result.Succeeded.Should().BeFalse();
            File.Exists(Path.Combine(workspace, "first.txt")).Should().BeTrue();
            File.Exists(Path.Combine(workspace, "blocked.txt")).Should().BeFalse();
            File.Exists(Path.Combine(workspace, "still-blocked.txt")).Should().BeFalse();
            File.Exists(Path.Combine(workspace, "after.txt")).Should().BeTrue();
            var checkpointRequest = executor.Requests.FindIndex(request =>
                request.Any(message =>
                    message
                        .Contents.OfType<FunctionCallContent>()
                        .Any(call => call.Name == "write_checkpoint")
                )
            );
            checkpointRequest.Should().BeGreaterThanOrEqualTo(0);
            executor
                .Requests.Skip(checkpointRequest + 1)
                .SelectMany(x => x)
                .Should()
                .Contain(message =>
                    message
                        .Contents.OfType<FunctionCallContent>()
                        .Any(call => call.CallId == "first")
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

    [Fact(Timeout = 15_000)]
    public async Task Token_threshold_checkpoint_resets_the_executor_session()
    {
        var repository = TestSupport.CreateGitRepository();
        var workspace = Path.Combine(Path.GetTempPath(), $"cadence-token-{Guid.NewGuid():N}");
        try
        {
            var highUsage = TestSupport.Text("UNIQUE OLD CONTEXT");
            highUsage.Usage = new UsageDetails { InputTokenCount = 61, OutputTokenCount = 1 };
            var executor = new ScriptedChatClient(
                "executor",
                Read("initial-executor-read"),
                Ask("initial-executor-ask"),
                highUsage,
                Checkpoint("token"),
                Ask("stop")
            );
            var planner = new ScriptedChatClient(
                "planner",
                Read("initial-approve"),
                TestSupport.Text(PlannerJson(PlannerDecisionValue.Proceed)),
                Read("checkpoint-approve"),
                TestSupport.Text(PlannerJson(PlannerDecisionValue.Proceed)),
                Read("stop-read"),
                TestSupport.Text(PlannerJson(PlannerDecisionValue.Stop))
            );
            var result = await new PipelineRunner().RunAsync(
                new CadenceComposition(
                    Factory(
                        x => x == "executor" ? executor : planner,
                        profiles: _ => new(100, 20, 80)
                    )
                ).Build(),
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
            executor
                .Requests[^1]
                .Should()
                .NotContain(message => message.Text.Contains("UNIQUE OLD CONTEXT"));
            executor
                .Requests[^1]
                .Should()
                .NotContain(message =>
                    message
                        .Contents.OfType<FunctionCallContent>()
                        .Any(call => call.Name == "write_checkpoint")
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

    [Fact(Timeout = 15_000)]
    public async Task Executor_verification_tools_are_authorized_diagnostic_and_rerun_post_capture()
    {
        var repository = TestSupport.CreateGitRepository();
        var markerFile = Path.Combine(
            Path.GetTempPath(),
            $"cadence-verification-{Guid.NewGuid():N}"
        );
        try
        {
            var packet = TestSupport.Packet() with
            {
                Repository = repository,
                Verification = [new("test", $"echo run >> {markerFile}")],
                Commands =
                [
                    new("install-dependencies", "task install"),
                    new("generate-contracts", "task contracts"),
                ],
            };
            var before = new ScriptedChatClient("before", Read("before-read"), Ask("before"));
            var beforeParticipant = Factory(_ => before).Create().Executor;
            await new PipelineRunner().RunAsync(
                Pipeline.Start(beforeParticipant, "before-authorization").Build(beforeParticipant),
                CadenceState.Create(packet, TestSupport.Head(repository), repository),
                cancellationToken: TestContext.Current.CancellationToken
            );
            before.AdvertisedTools.SelectMany(x => x).Should().NotContain("run_verification_test");
            before
                .AdvertisedTools.SelectMany(x => x)
                .Should()
                .NotContain("run_command_install-dependencies");
            before
                .AdvertisedTools.SelectMany(x => x)
                .Should()
                .NotContain("run_command_generate-contracts");

            var executor = new ScriptedChatClient(
                "executor",
                TestSupport.ToolCall(
                    "diagnostic",
                    "run_verification_test",
                    new Dictionary<string, object?>()
                ),
                Ask("done")
            );
            var participant = Factory(_ => executor).Create().Executor;
            var authorized = CadenceState
                .Create(packet, TestSupport.Head(repository), repository)
                .RecordPlannerDecision(
                    new(PlannerDecisionValue.Proceed, "Approved", [], ["README.md"], "Implement.")
                );
            var diagnostic = await new PipelineRunner().RunAsync(
                Pipeline.Start(participant, "diagnostic-verification").Build(participant),
                authorized,
                cancellationToken: TestContext.Current.CancellationToken
            );

            executor.AdvertisedTools.SelectMany(x => x).Should().Contain("run_verification_test");
            executor
                .AdvertisedTools.SelectMany(x => x)
                .Should()
                .Contain("run_command_install-dependencies");
            executor
                .AdvertisedTools.SelectMany(x => x)
                .Should()
                .Contain("run_command_generate-contracts");
            executor.AdvertisedTools.SelectMany(x => x).Should().NotContain("run_command_1");
            executor.AdvertisedTools.SelectMany(x => x).Should().NotContain("run_command_2");
            diagnostic.State.VerificationResults.Should().BeEmpty();
            File.ReadAllLines(markerFile).Should().HaveCount(1);

            var captured = diagnostic.State with { CandidateSha = TestSupport.Head(repository) };
            var stage = new VerificationStage(new VerificationOperation(new GitProcess()));
            var verified = await new PipelineRunner().RunAsync(
                Pipeline.Start(stage, "authoritative-verification").Build(stage),
                captured,
                cancellationToken: TestContext.Current.CancellationToken
            );
            verified.State.VerificationResults.Should().ContainSingle(x => x.ExitCode == 0);
            File.ReadAllLines(markerFile).Should().HaveCount(2);
        }
        finally
        {
            Directory.Delete(repository, true);
            if (File.Exists(markerFile))
            {
                File.Delete(markerFile);
            }
        }
    }

    [Fact(Timeout = 15_000)]
    public async Task Explicit_context_reset_discards_poison_and_hydrates_durable_state()
    {
        var repository = TestSupport.CreateGitRepository();
        var workspace = Path.Combine(Path.GetTempPath(), $"cadence-reset-{Guid.NewGuid():N}");
        try
        {
            var executor = new ScriptedChatClient(
                "executor",
                TestSupport.Text("POISONED-CONTEXT-MARKER"),
                Reset("reset"),
                Write("fresh-write", "fresh.txt", "fresh\n"),
                Ask("stop")
            );
            var planner = new ScriptedChatClient(
                "planner",
                Read("reset-approve"),
                TestSupport.Text(PlannerJson(PlannerDecisionValue.Proceed)),
                Read("stop-read"),
                TestSupport.Text(PlannerJson(PlannerDecisionValue.Stop))
            );
            var initial = CadenceState
                .Create(
                    TestSupport.Packet() with
                    {
                        Repository = repository,
                    },
                    TestSupport.Head(repository),
                    workspace
                )
                .RecordPlannerDecision(
                    new(PlannerDecisionValue.Proceed, "Approved", [], ["README.md"], "Implement.")
                ) with
            {
                OutcomeProgress =
                [
                    new("outcome-1", OutcomeStatus.InProgress, "Durable evidence", "Continue"),
                ],
            };
            var result = await new PipelineRunner().RunAsync(
                new CadenceComposition(Factory(x => x == "executor" ? executor : planner)).Build(),
                initial,
                new PipelineRunOptions(Observer: new NoOpPersistenceObserver()),
                TestContext.Current.CancellationToken
            );

            File.Exists(Path.Combine(workspace, "fresh.txt"))
                .Should()
                .BeTrue(
                    $"state workspace={result.State.WorkspacePath}; files={string.Join(',', Directory.GetFiles(workspace).Select(Path.GetFileName))}; last={string.Join('|', executor.Requests[^1].Select(x => x.Text))}"
                );
            var fresh = string.Join(
                "\n",
                executor.Requests.Skip(2).SelectMany(x => x).Select(x => x.Text)
            );
            fresh.Should().Contain("Reset checkpoint").And.Contain("Durable evidence");
            fresh.Should().NotContain("POISONED-CONTEXT-MARKER");
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

    private static CadenceParticipantsFactory Factory(
        Func<string, IChatClient> clients,
        Func<string, CadenceAgentProfile>? profiles = null,
        TimeProvider? time = null
    )
    {
        var git = new GitProcess();
        time ??= TimeProvider.System;
        var dirty = new DirtyWorkCheckpointPolicy(git, time);
        var capabilities = CadenceCapabilities.Create(time, dirty);
        return new(
            clients,
            profiles ?? (_ => new(200_000, 32_000, 80)),
            TestSupport.Doctrine(),
            [],
            new(git),
            git,
            dirty,
            capabilities.AskPlanner,
            capabilities.UpdateOutcomes,
            capabilities.SubmitReport,
            capabilities.WriteCheckpoint,
            capabilities.ResetContext
        );
    }

    private static CadenceState CandidateState(string repository) =>
        TestSupport.State(repository) with
        {
            PinnedBaseSha = TestSupport.Head(repository),
        };

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

    private static ChatResponse Read(string id) =>
        TestSupport.ToolCall(
            id,
            "file_access_read",
            new Dictionary<string, object?> { ["path"] = "README.md" }
        );

    private static ChatResponse Ask(string id) =>
        TestSupport.ToolCall(
            id,
            "ask_planner",
            new Dictionary<string, object?>
            {
                ["currentSlice"] = "slice",
                ["question"] = "Stop?",
                ["proposedApproach"] = "Stop safely.",
                ["evidence"] = new[] { "README.md" },
            }
        );

    private static ChatResponse Checkpoint(string id) =>
        TestSupport.ToolCall(
            id,
            "write_checkpoint",
            new Dictionary<string, object?>
            {
                ["summary"] = "Threshold checkpoint",
                ["uncertainties"] = Array.Empty<string>(),
                ["nextAction"] = "Continue.",
            }
        );

    private static ChatResponse Report(string id) =>
        TestSupport.ToolCall(
            id,
            "submit_report",
            new Dictionary<string, object?>
            {
                ["summary"] = "Bypass",
                ["commitMessage"] = "bypass",
                ["obligationClaims"] = Array.Empty<object>(),
                ["regressionTestEvidence"] = "test",
            }
        );

    private static ChatResponse Reset(string id) =>
        TestSupport.ToolCall(
            id,
            "reset_context",
            new Dictionary<string, object?>
            {
                ["summary"] = "Reset checkpoint",
                ["uncertainties"] = Array.Empty<string>(),
                ["nextAction"] = "Continue from durable state.",
                ["reason"] = "Conversation is contradictory.",
            }
        );

    private static ChatResponse Write(string id, string file, string content) =>
        TestSupport.ToolCall(
            id,
            "file_access_write",
            new Dictionary<string, object?>
            {
                ["fileName"] = file,
                ["content"] = content,
                ["overwrite"] = false,
            }
        );

    private sealed class MutableTimeProvider(DateTimeOffset value) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = value;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static string PlannerJson(PlannerDecisionValue decision) =>
        $$"""{"decision":"{{decision}}","rationale":"Repository read supports this decision.","constraints":[],"evidenceUsed":["README.md"],"safeNextAction":"Continue safely.","correctedApproach":null,"humanQuestion":null,"humanDecisionDomain":null}""";
}
