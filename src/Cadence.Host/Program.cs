using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Tandem.Ledger;
using Tandem.Packets;
using Tandem.Terminal;

namespace Cadence.Host;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var packetArgument = new Argument<string>("packet")
        {
            Description = "Path to a YAML packet with frontmatter.",
        };
        var runIdArgument = new Argument<string>("run")
        {
            Description = "Cadence run ID or delivery packet path.",
        };
        var resumePacketOption = new Option<string?>("--packet")
        {
            Description = "Compatible replacement packet path.",
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

        var resume = new Command("resume", "Resume an interrupted executor-phase run")
        {
            runIdArgument,
            resumePacketOption,
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

        return await new RootCommand("Cadence runnable CLI host") { run, resume, publish }
            .Parse(args)
            .InvokeAsync();
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
        var packet = PacketReader.Read(packetPath);
        var execution = LoadExecutionConfiguration(home, explicitConfig);
        var runId = Guid.CreateVersion7();
        var runDirectory = Path.Combine(home, "runs", runId.ToString("N"));
        var workspace = Path.Combine(runDirectory, "workspace");
        Directory.CreateDirectory(runDirectory);
        var store = new SqliteLedgerStore(Path.Combine(runDirectory, "ledger.sqlite3"));

        return await ExecuteAsync(
            execution,
            runId,
            store,
            CadenceState.Create(packet, string.Empty, workspace),
            publish,
            branch,
            cancellationToken
        );
    }

    private static async Task<int> ResumeAsync(
        string target,
        string? packetPath,
        string? explicitHome,
        string? explicitConfig,
        bool publish,
        string? branch,
        CancellationToken cancellationToken
    )
    {
        var home = ResolveHome(explicitHome);
        var resolved = await ResolveResumeTargetAsync(target, home, cancellationToken);
        var runId = resolved.RunId;
        var execution = LoadExecutionConfiguration(home, explicitConfig);
        var runDirectory = Path.Combine(home, "runs", runId.ToString("N"));
        var workspace = Path.Combine(runDirectory, "workspace");
        var store = new SqliteLedgerStore(Path.Combine(runDirectory, "ledger.sqlite3"));
        var accepted =
            await store.ReadLatestAcceptedAsync<CadenceState>(runId, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Run '{runId:N}' has no accepted Cadence state."
            );
        if (
            !string.Equals(
                Path.GetFullPath(accepted.Value.WorkspacePath),
                Path.GetFullPath(workspace),
                StringComparison.Ordinal
            )
        )
        {
            throw new InvalidOperationException(
                $"Run '{runId:N}' belongs to workspace '{accepted.Value.WorkspacePath}', not '{workspace}'."
            );
        }
        var packet = packetPath is not null
            ? PacketReader.Read(packetPath)
            : resolved.Packet ?? accepted.Value.Packet;
        var state = accepted.Value.Resume(packet);
        var run = await store.GetRunAsync(runId, cancellationToken);
        if (run.Status != LedgerRunStatus.Running && IsResumableStatus(run.Status))
        {
            await store.ReopenRunAsync(runId, cancellationToken);
        }

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

    internal static async ValueTask<ResumeTarget> ResolveResumeTargetAsync(
        string target,
        string home,
        CancellationToken cancellationToken
    )
    {
        if (Guid.TryParse(target, out var runId))
        {
            return new ResumeTarget(runId, null);
        }

        var packet = PacketReader.Read(target);
        var runsDirectory = Path.Combine(home, "runs");
        if (!Directory.Exists(runsDirectory))
        {
            throw new InvalidOperationException($"No retained run matches packet '{target}'.");
        }

        foreach (
            var runDirectory in Directory
                .EnumerateDirectories(runsDirectory)
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
        )
        {
            if (!Guid.TryParse(Path.GetFileName(runDirectory), out runId))
            {
                continue;
            }
            var ledgerPath = Path.Combine(runDirectory, "ledger.sqlite3");
            if (!File.Exists(ledgerPath))
            {
                continue;
            }

            var store = new SqliteLedgerStore(ledgerPath);
            var run = await store.GetRunAsync(runId, cancellationToken);
            if (!IsResumableStatus(run.Status))
            {
                continue;
            }
            var accepted = await store.ReadLatestAcceptedAsync<CadenceState>(
                runId,
                cancellationToken
            );
            if (accepted is not null && PacketsMatch(accepted.Value.Packet, packet))
            {
                return new ResumeTarget(runId, packet);
            }
        }

        throw new InvalidOperationException($"No retained run matches packet '{target}'.");
    }

    private static bool PacketsMatch(Packet left, Packet right) =>
        string.Equals(left.Title, right.Title, StringComparison.Ordinal)
        && string.Equals(left.Repository, right.Repository, StringComparison.Ordinal)
        && string.Equals(left.Base, right.Base, StringComparison.Ordinal)
        && left.Outcomes.SequenceEqual(right.Outcomes)
        && left.Acceptance.SequenceEqual(right.Acceptance)
        && left.Commands.SequenceEqual(right.Commands, StringComparer.Ordinal)
        && left.Verification.SequenceEqual(right.Verification, StringComparer.Ordinal)
        && left.Constraints.SequenceEqual(right.Constraints, StringComparer.Ordinal)
        && string.Equals(
            left.ImplementationContext,
            right.ImplementationContext,
            StringComparison.Ordinal
        );

    internal sealed record ResumeTarget(Guid RunId, Packet? Packet);

    private static async Task<int> ExecuteAsync(
        ExecutionConfiguration execution,
        Guid runId,
        SqliteLedgerStore store,
        CadenceState initialState,
        bool publish,
        string? branch,
        CancellationToken cancellationToken
    )
    {
        var clients = new ConfiguredChatClients(execution.Configuration);
        var timeProvider = TimeProvider.System;
        var services = new ServiceCollection();
        services.AddCadence(
            new CadenceOptions(
                clients.Build,
                clients.ResolveProfile,
                execution.ReviewerDoctrine,
                timeProvider,
                execution.GitTimeout,
                execution.Skills
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
            await PublishCoreAsync(
                store,
                runId,
                branch,
                execution.GitTimeout,
                execution.ReviewerDoctrine.Sha256,
                cancellationToken
            );
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

    internal static bool IsResumableStatus(LedgerRunStatus status) =>
        status
            is LedgerRunStatus.Running
                or LedgerRunStatus.Failed
                or LedgerRunStatus.Faulted
                or LedgerRunStatus.Interrupted;

    private static ExecutionConfiguration LoadExecutionConfiguration(
        string home,
        string? explicitConfig
    )
    {
        var path = Path.GetFullPath(explicitConfig ?? Path.Combine(home, "config.json"));
        var configuration = HostConfiguration.Load(path);
        return new ExecutionConfiguration(
            configuration,
            ReviewerDoctrine.Load(configuration.ResolveReviewerDoctrinePath(path)),
            configuration.ResolveSkillDirectories(path).Select(AgentSkill.FromDirectory).ToArray(),
            TimeSpan.FromSeconds(configuration.GitTimeoutSeconds)
        );
    }

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
        var execution = LoadExecutionConfiguration(home, explicitConfig);
        var store = new SqliteLedgerStore(
            Path.Combine(home, "runs", runId.ToString("N"), "ledger.sqlite3")
        );
        await PublishCoreAsync(
            store,
            runId,
            branch,
            execution.GitTimeout,
            execution.ReviewerDoctrine.Sha256,
            cancellationToken
        );
        return 0;
    }

    private static async Task PublishCoreAsync(
        SqliteLedgerStore store,
        Guid runId,
        string? branch,
        TimeSpan? gitTimeout,
        string reviewerDoctrineHash,
        CancellationToken cancellationToken
    )
    {
        var state =
            (await store.ReadLatestAcceptedAsync<CadenceState>(runId, cancellationToken))?.Value
            ?? throw new InvalidOperationException(
                $"Run '{runId:N}' has no accepted Cadence state."
            );
        var result = await new PublicationOperation(
            new Git.GitProcess(timeout: gitTimeout),
            reviewerDoctrineHash
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
