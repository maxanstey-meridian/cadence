using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
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
        var runIdArgument = new Argument<string>("run-id") { Description = "Run ID to publish." };
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

        return await new RootCommand("Cadence runnable CLI host") { run, publish }
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
        var configurationPath = Path.GetFullPath(
            explicitConfig ?? Path.Combine(home, "config.json")
        );
        var configuration = HostConfiguration.Load(configurationPath);
        var reviewerDoctrine = ReviewerDoctrine.Load(
            configuration.ResolveReviewerDoctrinePath(configurationPath)
        );
        var skills = configuration
            .ResolveSkillDirectories(configurationPath)
            .Select(AgentSkill.FromDirectory)
            .ToArray();
        var runId = Guid.CreateVersion7();
        var runDirectory = Path.Combine(home, "runs", runId.ToString("N"));
        var workspace = Path.Combine(runDirectory, "workspace");
        Directory.CreateDirectory(runDirectory);
        var records = new RunRecordStore(Path.Combine(runDirectory, "records.json"));
        await records.InitializeAsync(packet, cancellationToken);

        var clients = new ConfiguredChatClients(configuration);
        var timeProvider = TimeProvider.System;
        var gitTimeout = TimeSpan.FromSeconds(configuration.GitTimeoutSeconds);
        var services = new ServiceCollection();
        services.AddCadence(
            new CadenceOptions(
                clients.Build,
                clients.ResolveProfile,
                records,
                reviewerDoctrine,
                timeProvider,
                gitTimeout,
                skills
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
            CadenceState.Create(packet, string.Empty, workspace, timeProvider),
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
            await PublishCoreAsync(records, branch, gitTimeout, cancellationToken);
        }
        return 0;
    }

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
