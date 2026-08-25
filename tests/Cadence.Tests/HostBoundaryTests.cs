using System.Text.Json;
using System.Text.Json.Nodes;
using Cadence.Host;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Tandem.Ledger;
using Tandem.OpenAICompatible;
using Tandem.Packets;
using Tandem.Terminal;

namespace Cadence.Tests;

[Collection("Host global state")]
public sealed class HostBoundaryTests
{
    [Fact]
    public async Task Terminal_human_interaction_submits_typed_answers_through_the_display_seam()
    {
        var terminal = new TerminalHumanInteraction();
        var request = new ReviewerHumanRequest.RepairCap(
            "Continue repairs?",
            "The repair limit was reached."
        );
        var context = new PipelineInteractionContext<ReviewerHumanRequest, ReviewerHumanAnswer>(
            Guid.CreateVersion7(),
            "request-1",
            "reviewer-human",
            request
        );

        var waiting = terminal.WaitForReviewerAsync(context, TestContext.Current.CancellationToken);

        terminal.HasPending().Should().BeTrue();
        terminal
            .FormatInteraction(
                new PipelineInteractionRequested<ReviewerHumanRequest>(
                    context.RunId,
                    context.InteractionId,
                    context.RequestId,
                    request
                )
            )
            .Should()
            .Be(
                new TerminalInteractionPrompt(
                    "Continue repairs?",
                    "The repair limit was reached.\nAnswer continue or stop."
                )
            );

        await terminal.SubmitAsync("continue", TestContext.Current.CancellationToken);

        (await waiting).Should().BeOfType<ReviewerHumanAnswer.ContinueRepairs>();
        terminal.HasPending().Should().BeFalse();
    }

    [Fact]
    public void OpenRouter_completions_preserve_reasoning_for_pipeline_observers()
    {
        var configuration = new HostConfiguration(
            new Dictionary<string, ProviderConfiguration>
            {
                ["openrouter"] = new("https://openrouter.ai/api/v1", "CADENCE_TEST_OPENROUTER_KEY"),
                ["local"] = new("http://127.0.0.1:10531/v1", null, "responses"),
            },
            new Dictionary<string, ProfileConfiguration>
            {
                ["executor"] = new("openrouter", "deepseek/model", 1, 1, 80),
                ["planner"] = new("local", "gpt-sol", 1, 1, 80, "low"),
                ["reviewer"] = new("local", "gpt-sol", 1, 1, 80, "low"),
            },
            "reviewer.md"
        );
        var previousKey = Environment.GetEnvironmentVariable("CADENCE_TEST_OPENROUTER_KEY");
        Environment.SetEnvironmentVariable("CADENCE_TEST_OPENROUTER_KEY", "test-key");
        try
        {
            var clients = new ConfiguredChatClients(configuration);
            var executor = clients.Build("executor");
            executor.Should().BeOfType<StreamRetryChatClient>();
            executor.GetService<OpenRouterReasoningChatClient>().Should().NotBeNull();
            executor.GetService<ChatClientMetadata>()!.DefaultModelId.Should().Be("deepseek/model");
            clients
                .Build("planner")
                .GetService<ChatClientMetadata>()!
                .DefaultModelId.Should()
                .Be("gpt-sol");
        }
        finally
        {
            Environment.SetEnvironmentVariable("CADENCE_TEST_OPENROUTER_KEY", previousKey);
        }
    }

    [Fact]
    public void Host_configuration_resolves_and_loads_typed_reviewer_doctrine()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cadence-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var configPath = Path.Combine(directory, "config.json");
        var doctrinePath = Path.Combine(directory, "reviewer-doctrine.json");
        const string content =
            "{\n  \"clauses\": [{ \"id\": \"material-correctness\", \"text\": \"Authored  text.\" }]\n}\n";
        try
        {
            File.WriteAllText(doctrinePath, content);
            File.WriteAllText(configPath, ConfigurationJson("reviewer-doctrine.json"));

            var configuration = HostConfiguration.Load(configPath);
            var doctrine = ReviewerDoctrine.Load(
                configuration.ResolveReviewerDoctrinePath(configPath)
            );

            doctrine
                .Clauses.Should()
                .Equal(new ReviewerDoctrineClause("material-correctness", "Authored  text."));
            ((IList<ReviewerDoctrineClause>)doctrine.Clauses)
                .Invoking(clauses => clauses.Add(new("other", "text")))
                .Should()
                .Throw<NotSupportedException>();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Host_configuration_requires_reviewer_doctrine_file(string? doctrineFile)
    {
        var path = Path.Combine(Path.GetTempPath(), $"cadence-config-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, ConfigurationJson(doctrineFile));

            var act = () => HostConfiguration.Load(path);

            act.Should().Throw<InvalidOperationException>().WithMessage("*reviewerDoctrineFile*");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Reviewer_doctrine_rejects_missing_and_blank_files()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cadence-doctrine-{Guid.NewGuid():N}.json");
        var missing = () => ReviewerDoctrine.Load(path);
        missing.Should().Throw<InvalidOperationException>();

        File.WriteAllText(path, " \n\t");
        try
        {
            var blank = () => ReviewerDoctrine.Load(path);
            blank.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{}")]
    [InlineData("{\"extra\":true,\"clauses\":[{\"id\":\"one\",\"text\":\"text\"}]}")]
    [InlineData("{\"clauses\":[]}")]
    [InlineData("{\"clauses\":[{\"id\":\" \",\"text\":\"text\"}]}")]
    [InlineData(
        "{\"clauses\":[{\"id\":\"same\",\"text\":\"one\"},{\"id\":\"same\",\"text\":\"two\"}]}"
    )]
    public void Reviewer_doctrine_rejects_invalid_documents(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"cadence-doctrine-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, content);
            var act = () => ReviewerDoctrine.Load(path);
            act.Should().Throw<InvalidOperationException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Host_configuration_resolves_and_validates_skill_directories_from_config_directory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cadence-skills-{Guid.NewGuid():N}");
        var skill = Path.Combine(directory, "skills", "meridian");
        Directory.CreateDirectory(skill);
        File.WriteAllText(
            Path.Combine(skill, "SKILL.md"),
            "---\nname: meridian\ndescription: Review doctrine.\n---\n\n# Meridian\n"
        );
        var configPath = Path.Combine(directory, "config.json");
        try
        {
            File.WriteAllText(configPath, ConfigurationJson("reviewer.md", ["skills/meridian"]));

            var resolved = HostConfiguration.Load(configPath).ResolveSkillDirectories(configPath);

            resolved.Should().Equal(skill);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Host_configuration_rejects_invalid_skill_directories()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cadence-skills-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(directory, "missing-manifest"));
        var configPath = Path.Combine(directory, "config.json");
        try
        {
            File.WriteAllText(configPath, ConfigurationJson("reviewer.md", ["missing-manifest"]));
            var missingManifest = () =>
                HostConfiguration.Load(configPath).ResolveSkillDirectories(configPath);

            missingManifest.Should().Throw<InvalidOperationException>().WithMessage("*SKILL.md*");

            File.WriteAllText(configPath, ConfigurationJson("reviewer.md", ["missing"]));
            var missingDirectory = () =>
                HostConfiguration.Load(configPath).ResolveSkillDirectories(configPath);

            missingDirectory.Should().Throw<InvalidOperationException>().WithMessage("*not found*");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Host_configuration_rejects_skill_paths_that_resolve_to_the_same_directory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cadence-skills-{Guid.NewGuid():N}");
        var skill = Path.Combine(directory, "skill");
        Directory.CreateDirectory(skill);
        File.WriteAllText(Path.Combine(skill, "SKILL.md"), "# Skill\n");
        var configPath = Path.Combine(directory, "config.json");
        try
        {
            File.WriteAllText(configPath, ConfigurationJson("reviewer.md", ["skill", "./skill"]));

            var act = () => HostConfiguration.Load(configPath).ResolveSkillDirectories(configPath);

            act.Should().Throw<InvalidOperationException>().WithMessage("*distinct paths*");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Host_configuration_rejects_blank_skill_directory_paths(string path)
    {
        var configPath = Path.Combine(
            Path.GetTempPath(),
            $"cadence-config-{Guid.NewGuid():N}.json"
        );
        try
        {
            File.WriteAllText(configPath, ConfigurationJson("reviewer.md", [path]));

            var act = () => HostConfiguration.Load(configPath);

            act.Should().Throw<InvalidOperationException>().WithMessage("*skillDirectories*");
        }
        finally
        {
            File.Delete(configPath);
        }
    }

    [Fact]
    public void Host_configuration_rejects_a_non_positive_git_timeout()
    {
        var path = Path.Combine(Path.GetTempPath(), $"cadence-config-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(
                path,
                """
                {
                  "gitTimeoutSeconds": 0,
                  "providers": {},
                  "profiles": {
                    "executor": { "provider": "local", "model": "model", "contextWindowTokens": 1, "maxOutputTokens": 1, "checkpointAtPercent": 80 },
                    "planner": { "provider": "local", "model": "model", "contextWindowTokens": 1, "maxOutputTokens": 1, "checkpointAtPercent": 80 },
                    "reviewer": { "provider": "local", "model": "model", "contextWindowTokens": 1, "maxOutputTokens": 1, "checkpointAtPercent": 80 }
                  }
                }
                """
            );

            var act = () => HostConfiguration.Load(path);

            act.Should().Throw<InvalidOperationException>().WithMessage("*must be positive*");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("commands", "  - label: generate\n    command: ''")]
    [InlineData("commands", "  - null")]
    [InlineData("commands", "  - label: '   '\n    command: task generate")]
    [InlineData("commands", "  - label: generate\n    command: '   '")]
    [InlineData(
        "commands",
        "  - label: duplicate\n    command: one\n  - label: ' duplicate '\n    command: two"
    )]
    [InlineData("commands", "  - label: invalid.label\n    command: task generate")]
    [InlineData("verification", "  - label: check\n    command: ''")]
    [InlineData("verification", "  - null")]
    [InlineData("verification", "  - label: '   '\n    command: task check")]
    [InlineData("verification", "  - label: check\n    command: '   '")]
    [InlineData(
        "verification",
        "  - label: duplicate\n    command: one\n  - label: ' duplicate '\n    command: two"
    )]
    [InlineData("verification", "  - label: invalid.label\n    command: task check")]
    public void Packet_reader_rejects_invalid_labeled_command_entries(string role, string entries)
    {
        var repository = TestSupport.CreateTemporaryDirectory();
        var packetPath = Path.Combine(Path.GetTempPath(), $"cadence-packet-{Guid.NewGuid():N}.md");
        try
        {
            var content = ValidPacket(
                repository,
                role == "commands" ? $"commands:\n{entries}" : ""
            );
            if (role == "verification")
            {
                content = content.Replace(
                    "verification:\n  - label: test\n    command: dotnet test",
                    $"verification:\n{entries}",
                    StringComparison.Ordinal
                );
            }
            File.WriteAllText(packetPath, content);

            var act = () => PacketReader.Read(packetPath);

            act.Should().Throw<PacketFileException>().WithMessage("*validation failed*");
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
            File.Delete(packetPath);
        }
    }

    [Theory]
    [InlineData("commands", 53)]
    [InlineData("verification", 48)]
    public void Packet_reader_rejects_tool_names_longer_than_64_characters(
        string role,
        int labelLength
    )
    {
        var repository = TestSupport.CreateTemporaryDirectory();
        var packetPath = Path.Combine(Path.GetTempPath(), $"cadence-packet-{Guid.NewGuid():N}.md");
        try
        {
            var entries = $"  - label: {new string('a', labelLength)}\n    command: task check";
            var content = ValidPacket(
                repository,
                role == "commands" ? $"commands:\n{entries}" : ""
            );
            if (role == "verification")
            {
                content = content.Replace(
                    "verification:\n  - label: test\n    command: dotnet test",
                    $"verification:\n{entries}",
                    StringComparison.Ordinal
                );
            }
            File.WriteAllText(packetPath, content);

            var act = () => PacketReader.Read(packetPath);

            act.Should().Throw<PacketFileException>().WithMessage("*validation failed*");
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
            File.Delete(packetPath);
        }
    }

    [Fact]
    public void Packet_reader_preserves_authored_lists_and_resolves_context_and_repository()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cadence-packet-{Guid.NewGuid():N}");
        var repository = Path.Combine(directory, "repository");
        Directory.CreateDirectory(repository);
        var path = Path.Combine(directory, "packet.md");
        try
        {
            var content = """
                ---
                title: "  Deliver behavior  "
                repository: ./repository
                base: "  main  "
                outcomes:
                  - id: " first "
                    description: " First result "
                  - id: second
                    description: Second result
                acceptance:
                  - id: " criterion-first "
                    outcome: " first "
                    requirement: " First scenario "
                  - id: criterion-second
                    outcome: second
                    requirement: Second scenario
                commands:
                  - label: " generate "
                    command: " task generate "
                  - label: contracts
                    command: "task contracts"
                verification:
                  - label: "test-1"
                    command: " dotnet test "
                  - label: "test-2"
                    command: "dotnet test"
                constraints:
                  - id: " preserve-text "
                    requirement: " Preserve exact text "
                ---

                Inspect first.
                Then implement.
                """;
            File.WriteAllText(
                path,
                content.Replace("Inspect first.\n", "Inspect first.\r\n", StringComparison.Ordinal)
            );

            var packet = PacketReader.Read(path);

            packet.Title.Should().Be("Deliver behavior");
            packet.Repository.Should().Be(repository);
            packet.Base.Should().Be("main");
            packet.Outcomes.Select(outcome => outcome.Id).Should().Equal("first", "second");
            packet
                .Acceptance.Select(criterion => criterion.Id)
                .Should()
                .Equal("criterion-first", "criterion-second");
            packet
                .Acceptance.Select(criterion => criterion.OutcomeId)
                .Should()
                .Equal("first", "second");
            packet
                .Acceptance.Select(criterion => criterion.Requirement)
                .Should()
                .Equal("First scenario", "Second scenario");
            packet
                .Commands.Should()
                .Equal(
                    new PacketCommand("generate", "task generate"),
                    new PacketCommand("contracts", "task contracts")
                );
            packet
                .Verification.Should()
                .Equal(
                    new PacketCommand("test-1", "dotnet test"),
                    new PacketCommand("test-2", "dotnet test")
                );
            packet
                .Constraints.Should()
                .Equal(new PacketConstraint("preserve-text", "Preserve exact text"));
            packet.ImplementationContext.Should().Be("Inspect first.\nThen implement.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("unknown: value")]
    [InlineData("title: duplicate")]
    public void Packet_reader_rejects_unknown_and_duplicate_frontmatter(string extra)
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cadence-packet-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var packetPath = Path.Combine(directory, "packet.md");
        try
        {
            File.WriteAllText(packetPath, ValidPacket(directory, extra));

            var exception = Assert.Throws<PacketFileException>(() => PacketReader.Read(packetPath));

            exception.Problems.Should().ContainSingle().Which.Path.Should().Be("$");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Packet_reader_rejects_trimmed_duplicate_outcomes()
    {
        var content = ValidPacket(Path.GetTempPath(), "")
            .Replace(
                "    description: Deliver behavior",
                "    description: Deliver behavior\n  - id: \" outcome-1 \"\n    description: Duplicate",
                StringComparison.Ordinal
            );

        var act = () => PacketFile.Parse(content, new PacketValidator(), "packet.md");

        act.Should().Throw<PacketFileException>().WithMessage("*validation failed*");
    }

    [Fact]
    public void Packet_reader_defaults_omitted_constraints_to_empty()
    {
        var repository = TestSupport.CreateTemporaryDirectory();
        var packetPath = Path.Combine(Path.GetTempPath(), $"cadence-packet-{Guid.NewGuid():N}.md");
        try
        {
            File.WriteAllText(packetPath, ValidPacket(repository, ""));

            PacketReader.Read(packetPath).Constraints.Should().BeEmpty();
            PacketReader.Read(packetPath).Commands.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
            File.Delete(packetPath);
        }
    }

    [Theory]
    [InlineData("outcomes")]
    [InlineData("verification")]
    public void Packet_reader_reports_missing_required_lists_without_validator_fault(string field)
    {
        var repository = Path.GetTempPath();
        var content = ValidPacket(repository, "");
        var start = content.IndexOf($"{field}:", StringComparison.Ordinal);
        var end =
            field == "outcomes"
                ? content.IndexOf("acceptance:", start, StringComparison.Ordinal)
                : content.IndexOf("---", start, StringComparison.Ordinal);
        content = content.Remove(start, end - start);

        var act = () => PacketFile.Parse(content, new PacketValidator(), "packet.md");

        act.Should().Throw<PacketFileException>().WithMessage("*validation failed*");
    }

    [Theory]
    [InlineData("outcomes:\n  - null", "Packet outcomes must not contain null values.")]
    [InlineData("constraints:\n  - null", "Packet constraints must not contain null values.")]
    public void Packet_reader_reports_null_collection_elements_as_validation_failures(
        string replacement,
        string message
    )
    {
        var repository = Path.GetTempPath();
        var content = replacement.StartsWith("outcomes:", StringComparison.Ordinal)
            ? ValidPacket(repository, "")
                .Replace(
                    "outcomes:\n  - id: outcome-1\n    description: Deliver behavior",
                    replacement,
                    StringComparison.Ordinal
                )
            : ValidPacket(repository, replacement);

        var act = () => PacketFile.Parse(content, new PacketValidator(), "packet.md");

        act.Should()
            .Throw<PacketFileException>()
            .Which.Problems.Should()
            .Contain(problem => problem.Message == message);
    }

    [Fact]
    public void Packet_reader_requires_yaml_ambiguous_commands_to_be_quoted()
    {
        var repository = Path.GetTempPath();
        var content = ValidPacket(repository, "")
            .Replace("command: dotnet test", "command: true", StringComparison.Ordinal);

        var act = () => PacketFile.Parse(content, new PacketValidator(), "packet.md");

        act.Should()
            .Throw<PacketFileException>()
            .Which.Problems.Should()
            .Contain(problem => problem.Path == "$.verification[0].command");
    }

    [Fact]
    public void Checked_in_example_parses_through_the_production_reader()
    {
        var root = FindRepositoryRoot();

        var packet = PacketReader.Read(Path.Combine(root, "examples", "packet.md"));

        packet.Repository.Should().Be(root);
        packet.Commands.Should().Equal(new PacketCommand("format", "task format"));
        packet.Verification.Should().Equal(new PacketCommand("check", "task check"));
        packet.ImplementationContext.Should().Contain("host boundary tests");
    }

    [Fact]
    public async Task Validate_reads_the_effective_packet_without_creating_a_run()
    {
        var home = Path.Combine(Path.GetTempPath(), $"cadence-validate-{Guid.NewGuid():N}");
        var repository = TestSupport.CreateTemporaryDirectory();
        Directory.CreateDirectory(home);
        var packetPath = Path.Combine(home, "packet.md");
        File.WriteAllText(
            Path.Combine(home, "config.json"),
            ConfigurationWithRepositoriesJson(
                new Dictionary<string, object?>
                {
                    [repository] = new
                    {
                        verification = new[] { new { label = "check", command = "task check" } },
                    },
                }
            )
        );
        File.WriteAllText(
            packetPath,
            ValidPacket(repository, "")
                .Replace("verification:\n  - label: test\n    command: dotnet test\n", "")
        );
        var previousFactory = Program.ChatClientFactoryOverride;
        Program.ChatClientFactoryOverride = _ =>
            throw new InvalidOperationException("Validation must not create a model client.");
        try
        {
            var exitCode = await Program.Main(["validate", packetPath, "--home", home]);

            exitCode.Should().Be(0);
            Directory.Exists(Path.Combine(home, "runs")).Should().BeFalse();
        }
        finally
        {
            Program.ChatClientFactoryOverride = previousFactory;
            Directory.Delete(repository, recursive: true);
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public async Task Invalid_packet_fails_before_configuration_and_run_directory_creation()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cadence-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var packetPath = Path.Combine(directory, "packet.md");
        File.WriteAllText(packetPath, "not frontmatter");
        try
        {
            var exitCode = await Program.Main(["run", packetPath, "--home", directory]);

            exitCode.Should().Be(1);
            Directory.Exists(Path.Combine(directory, "runs")).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Invalid_configuration_fails_before_run_directory_creation()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cadence-cli-{Guid.NewGuid():N}");
        var repository = TestSupport.CreateTemporaryDirectory();
        Directory.CreateDirectory(directory);
        var packetPath = Path.Combine(directory, "packet.md");
        File.WriteAllText(packetPath, ValidPacket(repository, ""));
        try
        {
            var exitCode = await Program.Main(["run", packetPath, "--home", directory]);

            exitCode.Should().Be(1);
            Directory.Exists(Path.Combine(directory, "runs")).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Terminal_cancellation_is_recorded_as_resumable_interruption()
    {
        Program
            .MapTerminalStatus(TerminalPipelineStatus.Cancelled)
            .Should()
            .Be(LedgerRunStatus.Interrupted);
    }

    [Theory(Timeout = 30_000)]
    [InlineData(LedgerRunStatus.Running)]
    [InlineData(LedgerRunStatus.Ready)]
    [InlineData(LedgerRunStatus.Failed)]
    [InlineData(LedgerRunStatus.Faulted)]
    [InlineData(LedgerRunStatus.Interrupted)]
    [InlineData(LedgerRunStatus.Cancelled)]
    public async Task Resume_reopens_every_status_and_reaches_the_pipeline_in_the_same_run(
        LedgerRunStatus status
    )
    {
        var home = Path.Combine(Path.GetTempPath(), $"cadence-universal-resume-{Guid.NewGuid():N}");
        var source = TestSupport.CreateGitRepository();
        var runId = Guid.CreateVersion7();
        var runDirectory = Path.Combine(home, "runs", runId.ToString("N"));
        var workspace = Path.Combine(runDirectory, "workspace");
        Directory.CreateDirectory(runDirectory);
        TestSupport.Git(runDirectory, "clone", source, workspace);
        TestSupport.Git(workspace, "remote", "remove", "origin");
        File.WriteAllText(
            Path.Combine(home, "reviewer-doctrine.json"),
            "{\"clauses\":[{\"id\":\"review\",\"text\":\"Review doctrine.\"}]}"
        );
        var currentSkill = Path.Combine(home, "skills", "current-source-skill");
        Directory.CreateDirectory(currentSkill);
        File.WriteAllText(Path.Combine(currentSkill, "SKILL.md"), "# Current source skill");
        File.WriteAllText(
            Path.Combine(home, "config.json"),
            ConfigurationWithRepositoriesJson(
                new Dictionary<string, object?>
                {
                    [source] = new
                    {
                        skillDirectories = new[] { "skills/current-source-skill" },
                        commands = new[] { new { label = "changed", command = "changed command" } },
                        verification = new[]
                        {
                            new { label = "changed", command = "changed verification" },
                        },
                    },
                },
                "reviewer-doctrine.json"
            )
        );
        var store = new SqliteLedgerStore(Path.Combine(runDirectory, "ledger.sqlite3"));
        var observer = await store.CreateObserverAsync(
            runId,
            "cadence",
            TestContext.Current.CancellationToken
        );
        var checkpoint = new WriteCheckpointRequest("Retained work.", [], "Consult Planner.");
        var state = CadenceState.Create(
            TestSupport.Packet() with
            {
                Repository = source,
                Commands = [new("retained-command", "retained command")],
                Verification = [new("retained-verification", "retained verification")],
            },
            TestSupport.Head(source),
            workspace
        ) with
        {
            LatestCheckpoint = checkpoint,
            ExecutorTransition = new ExecutorTransition.CheckpointWritten(checkpoint),
        };
        await observer.ObserveAsync(
            new PipelineStepCompleted(
                runId,
                CadenceIds.Executor,
                new PipelineRunOutcome(
                    OutcomeKinds.CheckpointWritten,
                    CadenceIds.Executor,
                    "Checkpoint written.",
                    JsonSerializer.SerializeToElement(new { }),
                    TimeSpan.Zero
                ),
                new PipelineAcceptedValue(
                    typeof(CadenceState).FullName!,
                    JsonSerializer.SerializeToElement(state, TandemJson.CreateTypedContract())
                )
            ),
            TestContext.Current.CancellationToken
        );
        if (status != LedgerRunStatus.Running)
        {
            await store.CompleteRunAsync(runId, status, TestContext.Current.CancellationToken);
        }
        var sawRunning = false;
        var planner = new ScriptedChatClient(
            "planner",
            TestSupport.ToolCall(
                "read",
                "file_access_read",
                new Dictionary<string, object?> { ["path"] = "README.md" }
            ),
            TestSupport.Text(
                "{\"decision\":\"Stop\",\"rationale\":\"Repository read supports stopping.\",\"constraints\":[],\"evidenceUsed\":[\"README.md\"],\"safeNextAction\":\"Stop safely.\",\"correctedApproach\":null,\"humanQuestion\":null,\"humanDecisionDomain\":null}"
            )
        )
        {
            BeforeCall = _ =>
                sawRunning =
                    store
                        .GetRunAsync(runId, TestContext.Current.CancellationToken)
                        .AsTask()
                        .GetAwaiter()
                        .GetResult()
                        .Status == LedgerRunStatus.Running,
        };
        var previousFactory = Program.ChatClientFactoryOverride;
        Program.ChatClientFactoryOverride = name =>
            name == CadenceIds.Planner ? planner : new ScriptedChatClient(name);
        try
        {
            var exitCode = await Program.Main([
                "resume",
                runId.ToString("N"),
                "--instruction",
                "Preserve retained work.",
                "--home",
                home,
            ]);

            exitCode.Should().Be(3);
            sawRunning.Should().BeTrue();
            planner.CallCount.Should().Be(2);
            Directory.GetDirectories(Path.Combine(home, "runs")).Should().ContainSingle();
            Directory.Exists(workspace).Should().BeTrue();
            var accepted = await store.ReadLatestAcceptedAsync<CadenceState>(
                runId,
                TestContext.Current.CancellationToken
            );
            accepted.Should().NotBeNull();
            accepted!
                .Value.Packet.Commands.Should()
                .Equal(new PacketCommand("retained-command", "retained command"));
            accepted
                .Value.Packet.Verification.Should()
                .Equal(new PacketCommand("retained-verification", "retained verification"));
            accepted.Value.OperatorInstruction.Should().Be("Preserve retained work.");
        }
        finally
        {
            Program.ChatClientFactoryOverride = previousFactory;
            Directory.Delete(source, true);
            Directory.Delete(home, true);
        }
    }

    [Fact(Timeout = 30_000)]
    public async Task Resume_packet_override_replaces_incompatible_persisted_packet_before_deserialization()
    {
        var home = Path.Combine(Path.GetTempPath(), $"cadence-packet-resume-{Guid.NewGuid():N}");
        var source = TestSupport.CreateGitRepository();
        var runId = Guid.CreateVersion7();
        var runDirectory = Path.Combine(home, "runs", runId.ToString("N"));
        var workspace = Path.Combine(runDirectory, "workspace");
        var packetPath = Path.Combine(home, "packet.md");
        Directory.CreateDirectory(runDirectory);
        TestSupport.Git(runDirectory, "clone", source, workspace);
        TestSupport.Git(workspace, "remote", "remove", "origin");
        File.WriteAllText(
            Path.Combine(home, "reviewer-doctrine.json"),
            "{\"clauses\":[{\"id\":\"review\",\"text\":\"Review doctrine.\"}]}"
        );
        File.WriteAllText(
            Path.Combine(home, "config.json"),
            ConfigurationWithRepositoriesJson(
                new Dictionary<string, object?>
                {
                    [source] = new
                    {
                        commands = new[]
                        {
                            new { label = "install-dependencies", command = "default install" },
                            new { label = "repository-only", command = "task repository-only" },
                        },
                        verification = new[] { new { label = "test", command = "default test" } },
                    },
                },
                "reviewer-doctrine.json"
            )
        );
        File.WriteAllText(
            packetPath,
            ValidPacket(
                source,
                "commands:\n  - label: install-dependencies\n    command: pnpm install\n  - label: generate-contracts\n    command: task contracts"
            )
        );
        var store = new SqliteLedgerStore(Path.Combine(runDirectory, "ledger.sqlite3"));
        var observer = await store.CreateObserverAsync(
            runId,
            "cadence",
            TestContext.Current.CancellationToken
        );
        var checkpoint = new WriteCheckpointRequest("Retained work.", [], "Consult Planner.");
        var retained = CadenceState.Create(
            TestSupport.Packet() with
            {
                Repository = source,
                Verification =
                [
                    new("focused-api-tests", "dotnet test apps/api/CaseBridge.slnx"),
                    new("focused-ui-tests", "pnpm --filter @casebridge/ui test"),
                ],
            },
            TestSupport.Head(source),
            workspace
        ) with
        {
            LatestCheckpoint = checkpoint,
            ExecutorTransition = new ExecutorTransition.CheckpointWritten(checkpoint),
        };
        var options = TandemJson.CreateTypedContract();
        var legacyPayload = JsonSerializer.SerializeToNode(retained, options)!.AsObject();
        legacyPayload["packet"]!.AsObject()["commands"] = new JsonArray(
            JsonValue.Create("pnpm install"),
            JsonValue.Create("task contracts")
        );
        var payload = JsonSerializer.SerializeToElement(legacyPayload, options);
        var deserializeLegacy = () => payload.Deserialize<CadenceState>(options);
        deserializeLegacy.Should().Throw<JsonException>();
        await observer.ObserveAsync(
            new PipelineStepCompleted(
                runId,
                CadenceIds.Executor,
                new PipelineRunOutcome(
                    OutcomeKinds.CheckpointWritten,
                    CadenceIds.Executor,
                    "Checkpoint written.",
                    JsonSerializer.SerializeToElement(new { }),
                    TimeSpan.Zero
                ),
                new PipelineAcceptedValue(typeof(CadenceState).FullName!, payload)
            ),
            TestContext.Current.CancellationToken
        );
        var executor = new ScriptedChatClient(
            "executor",
            TestSupport.ToolCall(
                "executor-read-1",
                "file_access_read",
                new Dictionary<string, object?> { ["path"] = "README.md" }
            ),
            TestSupport.ToolCall(
                "executor-ask-1",
                "ask_planner",
                new Dictionary<string, object?>
                {
                    ["currentSlice"] = "Resume with the replacement packet.",
                    ["question"] = "May implementation continue?",
                    ["proposedApproach"] = "Use only replacement-packet commands.",
                    ["evidence"] = new[] { "README.md" },
                }
            ),
            TestSupport.ToolCall(
                "executor-read-2",
                "file_access_read",
                new Dictionary<string, object?> { ["path"] = "README.md" }
            ),
            TestSupport.ToolCall(
                "executor-ask-2",
                "ask_planner",
                new Dictionary<string, object?>
                {
                    ["currentSlice"] = "Replacement commands are exposed.",
                    ["question"] = "Stop the proof run.",
                    ["proposedApproach"] = "Stop after proving tool replacement.",
                    ["evidence"] = new[] { "README.md" },
                }
            )
        );
        var planner = new ScriptedChatClient(
            "planner",
            TestSupport.ToolCall(
                "planner-read-1",
                "file_access_read",
                new Dictionary<string, object?> { ["path"] = "README.md" }
            ),
            TestSupport.Text(
                "{\"decision\":\"Proceed\",\"rationale\":\"Repository read supports continuing with the replacement packet.\",\"constraints\":[],\"evidenceUsed\":[\"README.md\"],\"safeNextAction\":\"Use the replacement packet commands.\",\"correctedApproach\":null,\"humanQuestion\":null,\"humanDecisionDomain\":null}"
            ),
            TestSupport.ToolCall(
                "planner-read-2",
                "file_access_read",
                new Dictionary<string, object?> { ["path"] = "README.md" }
            ),
            TestSupport.Text(
                "{\"decision\":\"Stop\",\"rationale\":\"Repository read supports stopping the proof run.\",\"constraints\":[],\"evidenceUsed\":[\"README.md\"],\"safeNextAction\":\"Stop safely.\",\"correctedApproach\":null,\"humanQuestion\":null,\"humanDecisionDomain\":null}"
            )
        );
        var previousFactory = Program.ChatClientFactoryOverride;
        Program.ChatClientFactoryOverride = name => name == CadenceIds.Planner ? planner : executor;
        try
        {
            var exitCode = await Program.Main([
                "resume",
                runId.ToString("N"),
                "--packet",
                packetPath,
                "--home",
                home,
            ]);

            exitCode.Should().Be(3);
            planner.CallCount.Should().Be(4);
            executor.CallCount.Should().Be(4);
            Directory.GetDirectories(Path.Combine(home, "runs")).Should().ContainSingle();
            Directory.Exists(workspace).Should().BeTrue();
            var accepted = await store.ReadLatestAcceptedAsync<CadenceState>(
                runId,
                TestContext.Current.CancellationToken
            );
            accepted.Should().NotBeNull();
            accepted!
                .Value.Packet.Commands.Should()
                .Equal(
                    new PacketCommand("install-dependencies", "pnpm install"),
                    new PacketCommand("repository-only", "task repository-only"),
                    new PacketCommand("generate-contracts", "task contracts")
                );
            accepted
                .Value.Packet.Verification.Should()
                .Equal(new PacketCommand("test", "dotnet test"));
            accepted.Value.LatestCheckpoint.Should().BeNull();
            accepted.Value.PlannerConstraints.Should().BeEmpty();
            executor
                .AdvertisedTools.SelectMany(tools => tools)
                .Should()
                .Contain("run_command_install-dependencies")
                .And.Contain("run_command_repository-only")
                .And.Contain("run_command_generate-contracts")
                .And.Contain("run_verification_test")
                .And.NotContain("run_verification_focused-api-tests")
                .And.NotContain("run_verification_focused-ui-tests")
                .And.NotContain("run_command_1")
                .And.NotContain("run_command_2");
        }
        finally
        {
            Program.ChatClientFactoryOverride = previousFactory;
            Directory.Delete(source, true);
            Directory.Delete(home, true);
        }
    }

    [Fact]
    public void Resume_with_a_supplied_packet_restarts_packet_derived_delivery_state()
    {
        var state = TestSupport.State() with
        {
            MutationAuthorized = true,
            PlannerDecision = new PlannerDecision(
                PlannerDecisionValue.Stop,
                "The current packet cannot continue.",
                [],
                [],
                "Stop."
            ),
            LatestCheckpoint = new WriteCheckpointRequest(
                "Implemented the production path.",
                ["Dependencies are absent."],
                "Install dependencies and continue verification."
            ),
        };
        var packet = state.Packet with
        {
            Commands = [new("install", "pnpm install"), new("contracts", "task contracts")],
        };

        var resumed = Program.CreateResumeState(state, packet);

        resumed.Packet.Should().BeSameAs(packet);
        resumed.MutationAuthorized.Should().BeFalse();
        resumed.PlannerDecision.Should().BeNull();
        resumed.ResumeRequested.Should().BeTrue();
        resumed.WorkspacePath.Should().Be(state.WorkspacePath);
        resumed.PinnedBaseSha.Should().Be(state.PinnedBaseSha);
        resumed.LatestCheckpoint.Should().BeNull();
        resumed.ExecutorTransition.Should().BeNull();
        resumed.PlannerConstraints.Should().BeEmpty();
        resumed
            .OutcomeProgress.Should()
            .OnlyContain(progress => progress.Status == OutcomeStatus.NotStarted);
        resumed.CandidateSha.Should().BeNull();
        resumed.VerificationIndex.Should().Be(0);
        resumed.VerificationResults.Should().BeEmpty();
        resumed.ReviewerDecision.Should().BeNull();
        resumed.ReviewerHumanAnswer.Should().BeNull();
        resumed.AcceptedCandidateSha.Should().BeNull();
    }

    [Fact]
    public void Resume_with_a_supplied_packet_rejects_a_different_repository()
    {
        var state = TestSupport.State();
        var packet = state.Packet with { Repository = "/different-source" };

        var resume = () => Program.CreateResumeState(state, packet);

        resume
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*does not match retained run repository*");
    }

    [Fact]
    public async Task Cross_repository_packet_rejection_does_not_reopen_the_ledger()
    {
        var home = Path.Combine(Path.GetTempPath(), $"cadence-repository-guard-{Guid.NewGuid():N}");
        var retainedRepository = TestSupport.CreateGitRepository();
        var replacementRepository = TestSupport.CreateGitRepository();
        var runId = Guid.CreateVersion7();
        var runDirectory = Path.Combine(home, "runs", runId.ToString("N"));
        var workspace = Path.Combine(runDirectory, "workspace");
        Directory.CreateDirectory(runDirectory);
        var config = Path.Combine(home, "config.json");
        var packet = Path.Combine(home, "replacement.md");
        File.WriteAllText(config, ConfigurationJson("reviewer-doctrine.json"));
        File.WriteAllText(packet, ValidPacket(replacementRepository, ""));
        var store = new SqliteLedgerStore(Path.Combine(runDirectory, "ledger.sqlite3"));
        var observer = await store.CreateObserverAsync(
            runId,
            "cadence",
            TestContext.Current.CancellationToken
        );
        var state = CadenceState.Create(
            TestSupport.Packet() with
            {
                Repository = retainedRepository,
            },
            TestSupport.Head(retainedRepository),
            workspace
        );
        await observer.ObserveAsync(
            new PipelineStepCompleted(
                runId,
                CadenceIds.Executor,
                new PipelineRunOutcome(
                    OutcomeKinds.CheckpointWritten,
                    CadenceIds.Executor,
                    "Checkpoint written.",
                    JsonSerializer.SerializeToElement(new { }),
                    TimeSpan.Zero
                ),
                new PipelineAcceptedValue(
                    typeof(CadenceState).FullName!,
                    JsonSerializer.SerializeToElement(state, TandemJson.CreateTypedContract())
                )
            ),
            TestContext.Current.CancellationToken
        );
        await store.CompleteRunAsync(
            runId,
            LedgerRunStatus.Failed,
            TestContext.Current.CancellationToken
        );

        try
        {
            var exitCode = await Program.Main([
                "resume",
                runId.ToString("N"),
                "--packet",
                packet,
                "--home",
                home,
                "--config",
                config,
            ]);

            exitCode.Should().Be(1);
            (await store.GetRunAsync(runId, TestContext.Current.CancellationToken))
                .Status.Should()
                .Be(LedgerRunStatus.Failed);
            var latest = await store.ReadLatestAcceptedAsync<CadenceState>(
                runId,
                TestContext.Current.CancellationToken
            );
            latest!.Value.Packet.Repository.Should().Be(retainedRepository);
        }
        finally
        {
            Directory.Delete(retainedRepository, true);
            Directory.Delete(replacementRepository, true);
            Directory.Delete(home, true);
        }
    }

    [Fact]
    public void Resume_hydrates_missing_legacy_outcome_progress_without_inferring_completion()
    {
        var state = TestSupport.State() with { ReviewRepairRequired = true };
        var options = TandemJson.CreateTypedContract();
        var json = JsonSerializer.SerializeToNode(state, options)!.AsObject();
        json.Remove("outcomeProgress");
        var legacy = json.Deserialize<CadenceState>(options)!;

        var resumed = Program.CreateResumeState(legacy);

        resumed
            .OutcomeProgress.Should()
            .Equal(
                new OutcomeProgress(
                    "outcome-1",
                    OutcomeStatus.NotStarted,
                    "",
                    "Produce the complete candidate state required by this outcome."
                )
            );
        new SubmitReportRequestValidator(resumed)
            .Validate(TestContracts.Report("Done", "report"))
            .IsValid.Should()
            .BeFalse();
        var repaired = resumed.RecordOutcomeUpdates(
            new([new("outcome-1", OutcomeStatus.Complete, "Repair completed.", null)])
        );
        repaired.ReviewRepairRequired.Should().BeFalse();
    }

    [Fact]
    public void Resume_without_a_packet_preserves_retained_delivery_state()
    {
        var state = TestSupport.State() with
        {
            MutationAuthorized = true,
            PlannerDecision = new PlannerDecision(
                PlannerDecisionValue.Stop,
                "Stop.",
                [],
                ["README.md"],
                "Stop."
            ),
            LatestCheckpoint = new WriteCheckpointRequest(
                "Retained work.",
                [],
                "Continue retained work."
            ),
            OperatorInstruction = "Retained recovery instruction.",
            OperatorInstructionPending = true,
        };

        var resumed = Program.CreateResumeState(state);

        resumed.Packet.Should().BeSameAs(state.Packet);
        resumed.MutationAuthorized.Should().BeFalse();
        resumed.PlannerDecision.Should().BeNull();
        resumed.ResumeRequested.Should().BeTrue();
        resumed.LatestCheckpoint.Should().BeSameAs(state.LatestCheckpoint);
        resumed.OutcomeProgress.Should().BeSameAs(state.OutcomeProgress);
        resumed.CandidateSha.Should().Be(state.CandidateSha);
        resumed.VerificationResults.Should().BeSameAs(state.VerificationResults);
        resumed.OperatorInstruction.Should().Be(state.OperatorInstruction);
        resumed.OperatorInstructionPending.Should().BeTrue();
    }

    [Fact]
    public async Task Resume_rejects_a_ledger_bound_to_another_workspace()
    {
        var home = Path.Combine(Path.GetTempPath(), $"cadence-resume-home-{Guid.NewGuid():N}");
        var runId = Guid.CreateVersion7();
        var runDirectory = Path.Combine(home, "runs", runId.ToString("N"));
        var configPath = Path.Combine(home, "config.json");
        Directory.CreateDirectory(runDirectory);
        File.WriteAllText(
            Path.Combine(home, "reviewer-doctrine.json"),
            "{\"clauses\":[{\"id\":\"review\",\"text\":\"Review doctrine.\"}]}"
        );
        File.WriteAllText(configPath, ConfigurationJson("reviewer-doctrine.json"));
        try
        {
            var store = new SqliteLedgerStore(Path.Combine(runDirectory, "ledger.sqlite3"));
            var observer = await store.CreateObserverAsync(
                runId,
                "cadence",
                TestContext.Current.CancellationToken
            );
            var state = TestSupport.State(Path.Combine(home, "another-run", "workspace"));
            await observer.ObserveAsync(
                new PipelineStepCompleted(
                    runId,
                    CadenceIds.Executor,
                    new PipelineRunOutcome(
                        OutcomeKinds.CheckpointWritten,
                        CadenceIds.Executor,
                        "Checkpoint written.",
                        JsonSerializer.SerializeToElement(new { }),
                        TimeSpan.Zero
                    ),
                    new PipelineAcceptedValue(
                        typeof(CadenceState).FullName!,
                        JsonSerializer.SerializeToElement(state, TandemJson.CreateTypedContract())
                    )
                ),
                TestContext.Current.CancellationToken
            );

            var exitCode = await Program.Main([
                "resume",
                runId.ToString("N"),
                "--home",
                home,
                "--config",
                configPath,
            ]);

            exitCode.Should().Be(1);
            Directory.Exists(Path.Combine(runDirectory, "workspace")).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Fact]
    public void Repository_defaults_match_resolved_source_and_merge_commands_by_label()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"cadence-repository-defaults-{Guid.NewGuid():N}"
        );
        var repository = Path.Combine(root, "source");
        Directory.CreateDirectory(repository);
        try
        {
            var configuration = new HostConfiguration(
                new Dictionary<string, ProviderConfiguration>(),
                new Dictionary<string, ProfileConfiguration>(),
                "reviewer.md",
                Repositories: new Dictionary<string, RepositoryConfiguration>
                {
                    [Path.Combine(repository, ".")] = new(
                        Commands: [new("install", "default install"), new("generate", "generate")],
                        Verification: [new("check", "default check")]
                    ),
                }
            );
            var packetPath = Path.Combine(root, "packet.md");
            File.WriteAllText(
                packetPath,
                ValidPacket(
                    "source",
                    "commands:\n  - label: install\n    command: packet install\n  - label: finish\n    command: finish"
                )
            );

            var packet = PacketReader.Read(packetPath, configuration);

            packet.Repository.Should().Be(repository);
            packet
                .Commands.Should()
                .Equal(
                    new PacketCommand("install", "packet install"),
                    new PacketCommand("generate", "generate"),
                    new PacketCommand("finish", "finish")
                );
            packet
                .Verification.Should()
                .Equal(
                    new PacketCommand("check", "default check"),
                    new PacketCommand("test", "dotnet test")
                );
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Repository_defaults_use_platform_path_identity()
    {
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
        {
            return;
        }
        var repository = TestSupport.CreateTemporaryDirectory();
        try
        {
            var configured = repository.ToUpperInvariant();
            var configuration = new HostConfiguration(
                new Dictionary<string, ProviderConfiguration>(),
                new Dictionary<string, ProfileConfiguration>(),
                "reviewer.md",
                Repositories: new Dictionary<string, RepositoryConfiguration>
                {
                    [configured] = new(Commands: [new("check", "task check")]),
                }
            );

            configuration.FindRepository(repository).Should().NotBeNull();
        }
        finally
        {
            Directory.Delete(repository, true);
        }
    }

    [Fact]
    public void Repository_verification_allows_authored_packet_to_omit_verification()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"cadence-inherited-verification-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(root);
        try
        {
            var configuration = new HostConfiguration(
                new Dictionary<string, ProviderConfiguration>(),
                new Dictionary<string, ProfileConfiguration>(),
                "reviewer.md",
                Repositories: new Dictionary<string, RepositoryConfiguration>
                {
                    [root] = new(Verification: [new("check", "task check")]),
                }
            );
            var path = Path.Combine(root, "packet.md");
            File.WriteAllText(
                path,
                ValidPacket(root, "")
                    .Replace("verification:\n  - label: test\n    command: dotnet test\n", "")
            );
            PacketReader
                .Read(path, configuration)
                .Verification.Should()
                .Equal(new PacketCommand("check", "task check"));
            var act = () => PacketReader.Read(path);
            act.Should()
                .Throw<InvalidOperationException>()
                .WithMessage("*at least one verification*");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Effective_skills_put_global_first_deduplicate_overlap_and_use_source_repository()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"cadence-repository-skills-{Guid.NewGuid():N}"
        );
        var global = Path.Combine(root, "skills", "global");
        var local = Path.Combine(root, "skills", "local");
        Directory.CreateDirectory(global);
        Directory.CreateDirectory(local);
        File.WriteAllText(Path.Combine(global, "SKILL.md"), "# Global");
        File.WriteAllText(Path.Combine(local, "SKILL.md"), "# Local");
        try
        {
            var configuration = new HostConfiguration(
                new Dictionary<string, ProviderConfiguration>(),
                new Dictionary<string, ProfileConfiguration>(),
                "reviewer.md",
                ["skills/global"],
                Repositories: new Dictionary<string, RepositoryConfiguration>
                {
                    [Path.Combine(root, "source")] = new(["skills/global", "skills/local"]),
                }
            );
            configuration
                .ResolveSkillDirectories(
                    Path.Combine(root, "config.json"),
                    Path.Combine(root, "source")
                )
                .Should()
                .Equal(global, local);
            configuration
                .ResolveSkillDirectories(
                    Path.Combine(root, "config.json"),
                    Path.Combine(root, "runs", "id", "workspace")
                )
                .Should()
                .Equal(global);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("relative/repository")]
    public void Host_configuration_rejects_nonabsolute_repository_keys(string key)
    {
        var root = Path.Combine(Path.GetTempPath(), $"cadence-repository-key-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "config.json");
            File.WriteAllText(
                path,
                ConfigurationWithRepositoriesJson(
                    new Dictionary<string, object?> { [key] = new { } }
                )
            );
            var act = () => HostConfiguration.Load(path);
            act.Should().Throw<InvalidOperationException>().WithMessage("*absolute paths*");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Host_configuration_rejects_repository_keys_with_colliding_normalized_identity()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cadence-repository-key-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "config.json");
            File.WriteAllText(
                path,
                ConfigurationWithRepositoriesJson(
                    new Dictionary<string, object?>
                    {
                        [root] = new { },
                        [Path.Combine(root, ".")] = new { },
                    }
                )
            );
            var act = () => HostConfiguration.Load(path);
            act.Should().Throw<InvalidOperationException>().WithMessage("*distinct paths*");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Host_configuration_rejects_invalid_repository_commands_through_packet_policy()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"cadence-repository-command-{Guid.NewGuid():N}"
        );
        Directory.CreateDirectory(root);
        try
        {
            var path = Path.Combine(root, "config.json");
            File.WriteAllText(
                path,
                ConfigurationWithRepositoriesJson(
                    new Dictionary<string, object?>
                    {
                        [root] = new
                        {
                            commands = new[]
                            {
                                new { label = "duplicate", command = "one" },
                                new { label = "duplicate", command = "two" },
                            },
                        },
                    }
                )
            );
            var act = () => HostConfiguration.Load(path);
            act.Should().Throw<FluentValidation.ValidationException>().WithMessage("*Commands*");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Packet_layer_duplicates_are_rejected_before_repository_merge_and_unmatched_repository_gets_no_defaults()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cadence-packet-layer-{Guid.NewGuid():N}");
        var configured = Path.Combine(root, "configured");
        var unmatched = Path.Combine(root, "unmatched");
        Directory.CreateDirectory(configured);
        Directory.CreateDirectory(unmatched);
        try
        {
            var configuration = new HostConfiguration(
                new Dictionary<string, ProviderConfiguration>(),
                new Dictionary<string, ProfileConfiguration>(),
                "reviewer.md",
                Repositories: new Dictionary<string, RepositoryConfiguration>
                {
                    [configured] = new(
                        Commands: [new("default", "default")],
                        Verification: [new("configured", "configured")]
                    ),
                }
            );
            var unmatchedPath = Path.Combine(root, "unmatched.md");
            File.WriteAllText(unmatchedPath, ValidPacket(unmatched, ""));
            var packet = PacketReader.Read(unmatchedPath, configuration);
            packet.Commands.Should().BeEmpty();
            packet.Verification.Should().Equal(new PacketCommand("test", "dotnet test"));

            var duplicatePath = Path.Combine(root, "duplicate.md");
            File.WriteAllText(
                duplicatePath,
                ValidPacket(
                    configured,
                    "commands:\n  - label: same\n    command: one\n  - label: same\n    command: two"
                )
            );
            var act = () => PacketReader.Read(duplicatePath, configuration);
            act.Should()
                .Throw<PacketFileException>()
                .Which.Problems.Should()
                .Contain(problem => problem.Message.Contains("unique", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Repository_skill_validation_rejects_within_layer_duplicates_and_invalid_effective_directories()
    {
        var root = Path.Combine(Path.GetTempPath(), $"cadence-skill-validation-{Guid.NewGuid():N}");
        var source = Path.Combine(root, "source");
        var valid = Path.Combine(root, "valid");
        var noManifest = Path.Combine(root, "no-manifest");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(valid);
        Directory.CreateDirectory(noManifest);
        File.WriteAllText(Path.Combine(valid, "SKILL.md"), "# valid");
        try
        {
            var duplicate = new HostConfiguration(
                new Dictionary<string, ProviderConfiguration>(),
                new Dictionary<string, ProfileConfiguration>(),
                "reviewer.md",
                Repositories: new Dictionary<string, RepositoryConfiguration>
                {
                    [source] = new(["valid", "./valid"]),
                }
            );
            duplicate
                .Invoking(value =>
                    value.ResolveSkillDirectories(Path.Combine(root, "config.json"), source)
                )
                .Should()
                .Throw<InvalidOperationException>()
                .WithMessage("*distinct paths*");
            var manifest = duplicate with
            {
                Repositories = new Dictionary<string, RepositoryConfiguration>
                {
                    [source] = new(["no-manifest"]),
                },
            };
            manifest
                .Invoking(value =>
                    value.ResolveSkillDirectories(Path.Combine(root, "config.json"), source)
                )
                .Should()
                .Throw<InvalidOperationException>()
                .WithMessage("*SKILL.md*");
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact(Timeout = 30_000)]
    public async Task Fresh_run_persists_the_complete_effective_packet_to_its_actual_ledger()
    {
        var home = Path.Combine(Path.GetTempPath(), $"cadence-fresh-effective-{Guid.NewGuid():N}");
        var source = TestSupport.CreateGitRepository();
        Directory.CreateDirectory(home);
        var packetPath = Path.Combine(home, "packet.md");
        File.WriteAllText(
            Path.Combine(home, "reviewer-doctrine.json"),
            "{\"clauses\":[{\"id\":\"review\",\"text\":\"Review doctrine.\"}]}"
        );
        File.WriteAllText(
            Path.Combine(home, "config.json"),
            ConfigurationWithRepositoriesJson(
                new Dictionary<string, object?>
                {
                    [source] = new
                    {
                        commands = new[]
                        {
                            new { label = "replace", command = "repository replace" },
                            new { label = "repository", command = "repository command" },
                        },
                        verification = new[]
                        {
                            new { label = "test", command = "repository test" },
                            new { label = "repository-check", command = "repository check" },
                        },
                    },
                },
                "reviewer-doctrine.json"
            )
        );
        File.WriteAllText(
            packetPath,
            ValidPacket(
                source,
                "commands:\n  - label: replace\n    command: packet replace\n  - label: packet\n    command: packet command"
            )
        );
        var executor = new ScriptedChatClient(
            "executor",
            TestSupport.ToolCall(
                "read",
                "file_access_read",
                new Dictionary<string, object?> { ["path"] = "README.md" }
            ),
            TestSupport.ToolCall(
                "ask",
                "ask_planner",
                new Dictionary<string, object?>
                {
                    ["currentSlice"] = "Inspect fresh effective packet.",
                    ["question"] = "Should this proof run stop?",
                    ["proposedApproach"] = "Stop after state persistence.",
                    ["evidence"] = new[] { "README.md" },
                }
            )
        );
        var planner = new ScriptedChatClient(
            "planner",
            TestSupport.ToolCall(
                "read",
                "file_access_read",
                new Dictionary<string, object?> { ["path"] = "README.md" }
            ),
            TestSupport.Text(
                "{\"decision\":\"Stop\",\"rationale\":\"The persistence proof can stop after repository inspection.\",\"constraints\":[],\"evidenceUsed\":[\"README.md\"],\"safeNextAction\":\"Stop safely.\",\"correctedApproach\":null,\"humanQuestion\":null,\"humanDecisionDomain\":null}"
            )
        );
        var previousFactory = Program.ChatClientFactoryOverride;
        Program.ChatClientFactoryOverride = name => name == CadenceIds.Planner ? planner : executor;
        try
        {
            var exitCode = await Program.Main(["run", packetPath, "--home", home]);
            exitCode.Should().Be(3);
            var runDirectories = Directory.GetDirectories(Path.Combine(home, "runs"));
            runDirectories.Should().ContainSingle();
            var runDirectory = runDirectories.Single();
            var workspace = Path.Combine(runDirectory, "workspace");
            workspace.Should().NotBe(source);
            var runId = Guid.ParseExact(Path.GetFileName(runDirectory), "N");
            var store = new SqliteLedgerStore(Path.Combine(runDirectory, "ledger.sqlite3"));
            var persisted = await store.ReadLatestAcceptedAsync<CadenceState>(
                runId,
                TestContext.Current.CancellationToken
            );
            persisted.Should().NotBeNull();
            persisted!.Value.WorkspacePath.Should().Be(workspace);
            persisted.Value.Packet.Repository.Should().Be(source);
            persisted
                .Value.Packet.Commands.Should()
                .Equal(
                    new PacketCommand("replace", "packet replace"),
                    new PacketCommand("repository", "repository command"),
                    new PacketCommand("packet", "packet command")
                );
            persisted
                .Value.Packet.Verification.Should()
                .Equal(
                    new PacketCommand("test", "dotnet test"),
                    new PacketCommand("repository-check", "repository check")
                );
        }
        finally
        {
            Program.ChatClientFactoryOverride = previousFactory;
            Directory.Delete(source, true);
            Directory.Delete(home, true);
        }
    }

    private static string ConfigurationWithRepositoriesJson(
        IReadOnlyDictionary<string, object?> repositories,
        string doctrineFile = "reviewer.md"
    )
    {
        var root = JsonNode.Parse(ConfigurationJson(doctrineFile))!.AsObject();
        root["repositories"] = JsonSerializer.SerializeToNode(repositories);
        return root.ToJsonString();
    }

    private static string ValidPacket(string repository, string extra) =>
        $$"""
            ---
            title: Test packet
            repository: {{repository}}
            base: main
            outcomes:
              - id: outcome-1
                description: Deliver behavior
            acceptance:
              - id: criterion-1
                outcome: outcome-1
                requirement: A concrete scenario proves delivery
            verification:
              - label: test
                command: dotnet test
            {{(
                extra.StartsWith("constraints:", StringComparison.Ordinal) ? "" : "constraints: []"
            )}}
            {{extra}}
            ---
            Body
            """;

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "Cadence.slnx")))
        {
            current = current.Parent;
        }
        return current?.FullName
            ?? throw new InvalidOperationException("Could not find the Cadence repository root.");
    }

    private static string ConfigurationJson(
        string? doctrineFile,
        IReadOnlyList<string>? skillDirectories = null
    ) =>
        $$"""
            {
              "reviewerDoctrineFile": {{JsonSerializer.Serialize(doctrineFile)}},
              "skillDirectories": {{JsonSerializer.Serialize(skillDirectories ?? [])}},
              "providers": { "local": { "baseUrl": "http://127.0.0.1:1/v1", "apiKeyEnvironmentVariable": null } },
              "profiles": {
                "executor": { "provider": "local", "model": "model", "contextWindowTokens": 200000, "maxOutputTokens": 32000, "checkpointAtPercent": 80 },
                "planner": { "provider": "local", "model": "model", "contextWindowTokens": 200000, "maxOutputTokens": 32000, "checkpointAtPercent": 80 },
                "reviewer": { "provider": "local", "model": "model", "contextWindowTokens": 200000, "maxOutputTokens": 32000, "checkpointAtPercent": 80 }
              }
            }
            """;
}
