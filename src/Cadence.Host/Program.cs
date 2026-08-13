using System.CommandLine;
using Cadence.Git;
using Microsoft.Extensions.DependencyInjection;
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
        var runIdArgument = new Argument<string>("run-id") { Description = "Cadence run ID." };
        var resumePacketOption = new Option<string?>("--packet")
        {
            Description = "Packet path for legacy runs that predate packet persistence.",
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
        var records = new RunRecordStore(
            Path.Combine(runDirectory, "records.json"),
            Guid.CreateVersion7()
        );
        await records.InitializeAsync(packet, cancellationToken);

        return await ExecuteAsync(
            execution,
            runId,
            records,
            CadenceState.Create(packet, string.Empty, workspace),
            publish,
            branch,
            cancellationToken
        );
    }

    private static async Task<int> ResumeAsync(
        string runIdText,
        string? packetPath,
        string? explicitHome,
        string? explicitConfig,
        bool publish,
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
        var runDirectory = Path.Combine(home, "runs", runId.ToString("N"));
        var workspace = Path.Combine(runDirectory, "workspace");
        var records = new RunRecordStore(
            Path.Combine(runDirectory, "records.json"),
            Guid.CreateVersion7()
        );
        var recovery = await records.ReadRecoveryAsync(cancellationToken);
        var packet =
            recovery.Packet
            ?? (
                packetPath is null
                    ? throw new InvalidOperationException(
                        "This legacy run does not contain its packet. Pass --packet <path>."
                    )
                    : PacketReader.Read(packetPath)
            );
        var pinnedBaseSha =
            recovery.PinnedBaseSha
            ?? await ReadWorkspaceHeadAsync(workspace, execution.GitTimeout, cancellationToken);
        var state = CadenceState.Recover(packet, pinnedBaseSha, workspace, recovery);

        return await ExecuteAsync(
            execution,
            runId,
            records,
            state,
            publish,
            branch,
            cancellationToken
        );
    }

    private static async Task<int> ExecuteAsync(
        ExecutionConfiguration execution,
        Guid runId,
        RunRecordStore records,
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
                records,
                execution.ReviewerDoctrine,
                timeProvider,
                execution.GitTimeout,
                execution.Skills
            )
        );
        await using var provider = services.BuildServiceProvider();
        var composition = provider.GetRequiredService<CadenceComposition>();
        var terminal = new TerminalHumanInteraction(records);
        var interactions = new PipelineInteractionHandlers()
            .Handle(composition.PlannerHumanInput, terminal.WaitForPlannerAsync)
            .Handle(composition.ReviewerHumanInput, terminal.WaitForReviewerAsync);
        var pipeline = composition.Build();
        var result = await new PipelineRunner().RunWithTerminalAsync(
            pipeline,
            initialState,
            new TerminalPipelineRunOptions
            {
                Persistence = records,
                Run = new PipelineRunOptions(runId, interactions),
                Display = new TerminalDisplayOptions
                {
                    FormatInteraction = terminal.FormatInteraction,
                    SubmitTextAsync = terminal.SubmitAsync,
                    CanSubmitText = terminal.HasPending,
                },
            },
            cancellationToken
        );

        if (!result.Succeeded)
        {
            return 3;
        }

        var candidate =
            await records.ReadPublicationCandidateAsync(cancellationToken)
            ?? throw new InvalidOperationException("Successful run has no accepted candidate.");
        Console.WriteLine($"Accepted:  {candidate.CandidateSha}");
        if (publish)
        {
            await PublishCoreAsync(records, branch, execution.GitTimeout, cancellationToken);
        }
        return 0;
    }

    private static async Task<string> ReadWorkspaceHeadAsync(
        string workspace,
        TimeSpan gitTimeout,
        CancellationToken cancellationToken
    )
    {
        if (!Directory.Exists(workspace))
        {
            throw new InvalidOperationException($"Resume workspace not found: {workspace}");
        }
        var git = new GitProcess(timeout: gitTimeout);
        var result = await git.RunAsync(workspace, ["rev-parse", "HEAD"], cancellationToken);
        if (result.TimedOut || result.ExitCode != 0)
        {
            throw new InvalidOperationException("Resume workspace HEAD could not be read.");
        }
        return result.Stdout.Trim();
    }

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
        string? branch,
        CancellationToken cancellationToken
    )
    {
        if (!Guid.TryParse(runIdText, out var runId))
        {
            throw new InvalidOperationException($"Invalid run ID '{runIdText}'.");
        }
        var home = ResolveHome(explicitHome);
        var records = new RunRecordStore(
            Path.Combine(home, "runs", runId.ToString("N"), "records.json")
        );
        await PublishCoreAsync(records, branch, null, cancellationToken);
        return 0;
    }

    private static async Task PublishCoreAsync(
        RunRecordStore records,
        string? branch,
        TimeSpan? gitTimeout,
        CancellationToken cancellationToken
    )
    {
        var result = await new PublicationOperation(
            new Git.GitProcess(timeout: gitTimeout),
            records
        ).ExecuteAsync(branch, cancellationToken);
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
