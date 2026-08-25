using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Tandem.Ledger;
using Tandem.Packets;
using Tandem.Terminal;

namespace Cadence.Host;

internal static class Program
{
    internal static Func<string, IChatClient>? ChatClientFactoryOverride { get; set; }

    internal static IReadOnlySet<string> TruncatedToolNames { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "ask_planner",
            "update_outcomes",
            "submit_report",
            "write_checkpoint",
            "reset_context",
            "file_access_write",
            "file_access_replace",
        };

    public static async Task<int> Main(string[] args)
    {
        var packetArgument = new Argument<string>("packet")
        {
            Description = "Path to a YAML packet with frontmatter.",
        };
        var validatePacketArgument = new Argument<string>("packet")
        {
            Description = "Path to a YAML packet with frontmatter.",
        };
        var runIdArgument = new Argument<string>("run") { Description = "Cadence run ID." };
        var resumePacketOption = new Option<string?>("--packet")
        {
            Description = "Packet to use for the resumed run.",
        };
        var instructionOption = new Option<string?>("--instruction")
        {
            Description =
                "Operator recovery instruction to route through Planner before continuing.",
        };
        var homeOption = new Option<string?>("--home") { Description = "Cadence state directory." };
        var configOption = new Option<string?>("--config")
        {
            Description = "Provider configuration JSON.",
        };
        var branchOption = new Option<string?>("--branch") { Description = "Publication branch." };
        var publishOption = new Option<bool>("--publish")
        {
            Description = "Publish the accepted candidate after a successful run.",
        };
        var debugOption = new Option<bool>("--debug") { Description = "Show exception details." };

        var run = new Command("run", "Run a packet through Executor, Planner, and Reviewer")
        {
            packetArgument,
            homeOption,
            configOption,
            publishOption,
            branchOption,
            debugOption,
        };
        run.SetAction(
            async (parse, cancellationToken) =>
                await GuardAsync(
                    () =>
                        RunAsync(
                            parse.GetRequiredValue(packetArgument),
                            parse.GetValue(homeOption),
                            parse.GetValue(configOption),
                            parse.GetValue(publishOption),
                            parse.GetValue(branchOption),
                            cancellationToken
                        ),
                    parse.GetValue(debugOption)
                )
        );

        var validate = new Command("validate", "Validate a packet without creating a run")
        {
            validatePacketArgument,
            homeOption,
            configOption,
            debugOption,
        };
        validate.SetAction(
            async (parse, _) =>
                await GuardAsync(
                    () =>
                        ValidateAsync(
                            parse.GetRequiredValue(validatePacketArgument),
                            parse.GetValue(homeOption),
                            parse.GetValue(configOption)
                        ),
                    parse.GetValue(debugOption)
                )
        );

        var resume = new Command("resume", "Continue an existing durable delivery")
        {
            runIdArgument,
            resumePacketOption,
            instructionOption,
            homeOption,
            configOption,
            publishOption,
            branchOption,
            debugOption,
        };
        resume.SetAction(
            async (parse, cancellationToken) =>
                await GuardAsync(
                    () =>
                        ResumeAsync(
                            parse.GetRequiredValue(runIdArgument),
                            parse.GetValue(resumePacketOption),
                            ValidateInstruction(
                                parse.GetValue(instructionOption),
                                parse.GetValue(resumePacketOption)
                            ),
                            parse.GetValue(homeOption),
                            parse.GetValue(configOption),
                            parse.GetValue(publishOption),
                            parse.GetValue(branchOption),
                            cancellationToken
                        ),
                    parse.GetValue(debugOption)
                )
        );

        var publish = new Command("publish", "Publish the exact Reviewer-accepted candidate SHA")
        {
            runIdArgument,
            homeOption,
            configOption,
            branchOption,
            debugOption,
        };
        publish.SetAction(
            async (parse, cancellationToken) =>
                await GuardAsync(
                    () =>
                        PublishAsync(
                            parse.GetRequiredValue(runIdArgument),
                            parse.GetValue(homeOption),
                            parse.GetValue(configOption),
                            parse.GetValue(branchOption),
                            cancellationToken
                        ),
                    parse.GetValue(debugOption)
                )
        );

        return await new RootCommand("Cadence runnable CLI host") { run, validate, resume, publish }
            .Parse(args)
            .InvokeAsync();
    }

    private static Task<int> ValidateAsync(
        string packetPath,
        string? explicitHome,
        string? explicitConfig
    )
    {
        var home = ResolveHome(explicitHome);
        var host = LoadHostConfiguration(home, explicitConfig);
        var packet = PacketReader.Read(packetPath, host.Configuration);
        Console.WriteLine($"Valid packet: {packet.Title}");
        return Task.FromResult(0);
    }

    private static async Task<int> RunAsync(
        string packetPath,
        string? explicitHome,
        string? explicitConfig,
        bool publish,
        string? branch,
        CancellationToken cancellationToken
    )
    {
        var home = ResolveHome(explicitHome);
        var host = LoadHostConfiguration(home, explicitConfig);
        var packet = PacketReader.Read(packetPath, host.Configuration);
        var execution = LoadExecutionConfiguration(host, packet.Repository);
        var runId = Guid.CreateVersion7();
        var runDirectory = Path.Combine(home, "runs", runId.ToString("N"));
        var workspace = Path.Combine(runDirectory, "workspace");
        Directory.CreateDirectory(runDirectory);
        var store = new SqliteLedgerStore(Path.Combine(runDirectory, "ledger.sqlite3"));

        var timeProvider = TimeProvider.System;
        return await ExecuteAsync(
            execution,
            runId,
            store,
            CadenceState.Create(packet, string.Empty, workspace, timeProvider: timeProvider),
            publish,
            branch,
            cancellationToken,
            timeProvider
        );
    }

    private static async Task<int> ResumeAsync(
        string target,
        string? packetPath,
        string? instruction,
        string? explicitHome,
        string? explicitConfig,
        bool publish,
        string? branch,
        CancellationToken cancellationToken
    )
    {
        var home = ResolveHome(explicitHome);
        if (!Guid.TryParse(target, out var runId))
        {
            throw new InvalidOperationException($"Invalid run ID '{target}'.");
        }
        var host = LoadHostConfiguration(home, explicitConfig);
        var runDirectory = Path.Combine(home, "runs", runId.ToString("N"));
        var workspace = Path.Combine(runDirectory, "workspace");
        var store = new SqliteLedgerStore(Path.Combine(runDirectory, "ledger.sqlite3"));
        var packet = packetPath is null ? null : PacketReader.Read(packetPath, host.Configuration);
        var retained =
            (
                packet is null
                    ? (
                        await store.ReadLatestAcceptedAsync<CadenceState>(runId, cancellationToken)
                    )?.Value
                    : await ReadLatestAcceptedWithPacketAsync(
                        store,
                        runId,
                        packet,
                        cancellationToken
                    )
            )
            ?? throw new InvalidOperationException(
                $"Run '{runId:N}' has no accepted Cadence state."
            );
        if (
            !string.Equals(
                Path.GetFullPath(retained.WorkspacePath),
                Path.GetFullPath(workspace),
                StringComparison.Ordinal
            )
        )
        {
            throw new InvalidOperationException(
                $"Run '{runId:N}' belongs to workspace '{retained.WorkspacePath}', not '{workspace}'."
            );
        }
        var state = packet is null
            ? CreateResumeState(retained, instruction)
            : CreateResumeState(retained, packet);
        await store.ReopenRunAsync(runId, cancellationToken);
        if (instruction is not null)
        {
            await PersistOperatorInstructionAsync(store, runId, state, cancellationToken);
        }
        var execution = LoadExecutionConfiguration(host, state.Packet.Repository);

        return await ExecuteAsync(
            execution,
            runId,
            store,
            state,
            publish,
            branch,
            cancellationToken
        );
    }

    internal static async ValueTask PersistOperatorInstructionAsync(
        SqliteLedgerStore store,
        Guid runId,
        CadenceState state,
        CancellationToken cancellationToken
    )
    {
        var observer = await store.CreateObserverAsync(runId, "cadence", cancellationToken);
        await observer.ObserveAsync(
            new PipelineStepCompleted(
                runId,
                "resume.operator-instruction",
                new PipelineRunOutcome(
                    "resume.instruction.accepted",
                    "Accepted operator recovery instruction.",
                    "Accepted operator recovery instruction.",
                    default,
                    TimeSpan.Zero
                ),
                new PipelineAcceptedValue(
                    typeof(CadenceState).FullName ?? typeof(CadenceState).Name,
                    JsonSerializer.SerializeToElement(state, TandemJson.CreateTypedContract())
                )
            ),
            cancellationToken
        );
    }

    private static async ValueTask<CadenceState?> ReadLatestAcceptedWithPacketAsync(
        SqliteLedgerStore store,
        Guid runId,
        Packet packet,
        CancellationToken cancellationToken
    )
    {
        var valueType = typeof(CadenceState).FullName ?? typeof(CadenceState).Name;
        var entries = await store
            .ForRun(runId)
            .ReadAsync(PipelineJournal.Stream, cancellationToken);
        var accepted = entries.LastOrDefault(entry =>
            PipelineJournal.IsAccepted(entry.Value)
            && string.Equals(entry.Value.ValueType, valueType, StringComparison.Ordinal)
        );
        if (accepted is null)
        {
            return null;
        }

        var payload =
            accepted.Value.Payload
            ?? throw new LedgerDataException(
                $"Accepted value at sequence '{accepted.Sequence}' has no payload."
            );
        try
        {
            var options = TandemJson.CreateTypedContract();
            var state =
                JsonNode.Parse(payload.GetRawText())?.AsObject()
                ?? throw new JsonException(
                    $"Accepted value at sequence '{accepted.Sequence}' is null."
                );
            var retainedRepository =
                state["packet"]?["repository"]?.GetValue<string>()
                ?? throw new JsonException(
                    $"Accepted value at sequence '{accepted.Sequence}' has no packet repository."
                );
            if (!RepositoryPathIdentity.Equals(retainedRepository, packet.Repository))
            {
                throw new PacketRepositoryMismatchException(retainedRepository, packet.Repository);
            }
            state["packet"] = JsonSerializer.SerializeToNode(packet, options);
            return state.Deserialize<CadenceState>(options)
                ?? throw new JsonException(
                    $"Accepted value at sequence '{accepted.Sequence}' is null."
                );
        }
        catch (Exception exception)
            when (exception is JsonException or NotSupportedException or InvalidOperationException
                && exception is not PacketRepositoryMismatchException
            )
        {
            throw new LedgerDataException(
                $"Accepted value at sequence '{accepted.Sequence}' is malformed.",
                exception
            );
        }
    }

    private sealed class PacketRepositoryMismatchException(
        string retainedRepository,
        string replacementRepository
    )
        : InvalidOperationException(
            $"Replacement packet repository '{replacementRepository}' does not match retained run repository '{retainedRepository}'."
        );

    internal static CadenceState CreateResumeState(CadenceState state) =>
        CreateResumeState(state, (string?)null);

    internal static CadenceState CreateResumeState(CadenceState state, string? instruction) =>
        NormalizeLegacyOutcomeProgress(state) with
        {
            MutationAuthorized = false,
            PlannerDecision = null,
            ResumeRequested = true,
            OperatorInstruction = instruction ?? state.OperatorInstruction,
            OperatorInstructionPending =
                instruction is not null || state.OperatorInstructionPending,
        };

    private static string? ValidateInstruction(string? instruction, string? packetPath)
    {
        if (instruction is not null && packetPath is not null)
        {
            throw new InvalidOperationException(
                "--instruction and --packet cannot be used together."
            );
        }
        if (instruction is null)
        {
            return null;
        }
        var trimmed = instruction.Trim();
        return trimmed.Length > 0
            ? trimmed
            : throw new InvalidOperationException("--instruction requires a nonblank value.");
    }

    internal static CadenceState CreateResumeState(CadenceState state, Packet packet)
    {
        if (!RepositoryPathIdentity.Equals(state.Packet.Repository, packet.Repository))
        {
            throw new InvalidOperationException(
                $"Replacement packet repository '{packet.Repository}' does not match retained run repository '{state.Packet.Repository}'."
            );
        }

        return CadenceState.Create(
            packet,
            state.PinnedBaseSha,
            state.WorkspacePath,
            state.MaximumReviewAttempts
        ) with
        {
            ResumeRequested = true,
        };
    }

    internal static CadenceState NormalizeLegacyOutcomeProgress(CadenceState state) =>
        state.OutcomeProgress is { Count: > 0 } || state.Packet.Outcomes.Count == 0
            ? state
            : state with
            {
                OutcomeProgress = CadenceState.CreateInitialOutcomeProgress(state.Packet),
            };

    private static async Task<int> ExecuteAsync(
        ExecutionConfiguration execution,
        Guid runId,
        SqliteLedgerStore store,
        CadenceState initialState,
        bool publish,
        string? branch,
        CancellationToken cancellationToken,
        TimeProvider? timeProvider = null
    )
    {
        var clients = new ConfiguredChatClients(execution.Configuration);
        var buildClient = ChatClientFactoryOverride ?? clients.Build;
        var services = new ServiceCollection();
        services.AddCadence(
            new CadenceOptions(
                buildClient,
                clients.ResolveProfile,
                execution.ReviewerDoctrine,
                execution.GitTimeout,
                execution.Skills,
                TimeProvider: timeProvider
            )
        );
        await using var provider = services.BuildServiceProvider();
        var composition = provider.GetRequiredService<CadenceComposition>();
        var terminal = new TerminalHumanInteraction();
        var interactions = new PipelineInteractionHandlers()
            .Handle(composition.PlannerHumanInput, terminal.WaitForPlannerAsync)
            .Handle(composition.ReviewerHumanInput, terminal.WaitForReviewerAsync);
        var pipeline = composition.Build();
        var observer = await store.CreateObserverAsync(runId, "cadence", cancellationToken);
        var result = await new PipelineRunner().RunWithTerminalAsync(
            pipeline,
            initialState,
            new TerminalPipelineRunOptions
            {
                Persistence = observer,
                Run = new PipelineRunOptions(runId, interactions).WithRunLedger(
                    store.ForRun(runId)
                ),
                Display = new TerminalDisplayOptions
                {
                    FormatInteraction = terminal.FormatInteraction,
                    SubmitTextAsync = terminal.SubmitAsync,
                    CanSubmitText = terminal.HasPending,
                    Title = initialState.Packet.Title,
                    WorkingDirectory = initialState.Packet.Repository,
                    TruncatedToolNames = TruncatedToolNames,
                },
                TerminalizingAsync = async (completion, _) =>
                    await store.CompleteRunAsync(
                        runId,
                        MapTerminalStatus(completion.Status),
                        CancellationToken.None
                    ),
            },
            cancellationToken
        );

        if (!result.Succeeded)
        {
            return 3;
        }

        var candidate =
            result.State.AcceptedCandidateSha
            ?? throw new InvalidOperationException("Successful run has no accepted candidate.");
        Console.WriteLine($"Accepted:  {candidate}");
        if (publish)
        {
            await PublishCoreAsync(store, runId, branch, execution.GitTimeout, cancellationToken);
        }
        return 0;
    }

    internal static LedgerRunStatus MapTerminalStatus(TerminalPipelineStatus status) =>
        status switch
        {
            TerminalPipelineStatus.Succeeded => LedgerRunStatus.Ready,
            TerminalPipelineStatus.Failed => LedgerRunStatus.Failed,
            TerminalPipelineStatus.Cancelled => LedgerRunStatus.Interrupted,
            _ => LedgerRunStatus.Faulted,
        };

    private static HostConfigurationFile LoadHostConfiguration(string home, string? explicitConfig)
    {
        var path = Path.GetFullPath(explicitConfig ?? Path.Combine(home, "config.json"));
        return new HostConfigurationFile(HostConfiguration.Load(path), path);
    }

    private static ExecutionConfiguration LoadExecutionConfiguration(
        HostConfigurationFile host,
        string? repository = null
    ) =>
        new(
            host.Configuration,
            ReviewerDoctrine.Load(host.Configuration.ResolveReviewerDoctrinePath(host.Path)),
            host.Configuration.ResolveSkillDirectories(host.Path, repository)
                .Select(AgentSkill.FromDirectory)
                .ToArray(),
            TimeSpan.FromSeconds(host.Configuration.GitTimeoutSeconds)
        );

    private sealed record HostConfigurationFile(HostConfiguration Configuration, string Path);

    private sealed record ExecutionConfiguration(
        HostConfiguration Configuration,
        ReviewerDoctrine ReviewerDoctrine,
        IReadOnlyList<AgentSkill> Skills,
        TimeSpan GitTimeout
    );

    private static async Task<int> PublishAsync(
        string runIdText,
        string? explicitHome,
        string? explicitConfig,
        string? branch,
        CancellationToken cancellationToken
    )
    {
        if (!Guid.TryParse(runIdText, out var runId))
        {
            throw new InvalidOperationException($"Invalid run ID '{runIdText}'.");
        }
        var home = ResolveHome(explicitHome);
        var execution = LoadExecutionConfiguration(LoadHostConfiguration(home, explicitConfig));
        var store = new SqliteLedgerStore(
            Path.Combine(home, "runs", runId.ToString("N"), "ledger.sqlite3")
        );
        await PublishCoreAsync(store, runId, branch, execution.GitTimeout, cancellationToken);
        return 0;
    }

    private static async Task PublishCoreAsync(
        SqliteLedgerStore store,
        Guid runId,
        string? branch,
        TimeSpan? gitTimeout,
        CancellationToken cancellationToken
    )
    {
        var state =
            (await store.ReadLatestAcceptedAsync<CadenceState>(runId, cancellationToken))?.Value
            ?? throw new InvalidOperationException(
                $"Run '{runId:N}' has no accepted Cadence state."
            );
        var result = await new PublicationOperation(
            new Git.GitProcess(timeout: gitTimeout)
        ).ExecuteAsync(state, branch, cancellationToken);
        Console.WriteLine($"Published: {result.Branch}");
        Console.WriteLine($"SHA:       {result.CandidateSha}");
    }

    private static string ResolveHome(string? explicitHome) =>
        Path.GetFullPath(
            explicitHome
                ?? Environment.GetEnvironmentVariable("CADENCE_HOME")
                ?? Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".cadence"
                )
        );

    private static async Task<int> GuardAsync(Func<Task<int>> action, bool debug)
    {
        try
        {
            return await action();
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Cancelled.");
            return 4;
        }
        catch (PacketFileException exception)
        {
            Console.Error.WriteLine($"Error: {exception.Message}");
            foreach (var problem in exception.Problems)
            {
                var source = exception.SourceName ?? "packet";
                var location = problem.Line is { } line
                    ? $"{source}:{line}:{problem.Column ?? 1}"
                    : source;
                Console.Error.WriteLine($"  {location} {problem.Path}: {problem.Message}");
            }
            if (debug)
            {
                Console.Error.WriteLine(exception);
            }
            return 1;
        }
        catch (Exception exception)
        {
            var cause = exception;
            while (cause.InnerException is { } inner)
            {
                cause = inner;
            }
            Console.Error.WriteLine($"Error: {cause.Message}");
            if (debug)
            {
                Console.Error.WriteLine(exception);
            }
            return 1;
        }
    }
}
