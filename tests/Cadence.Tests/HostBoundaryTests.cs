using System.Text.Json;
using Cadence.Host;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Tandem.Ledger;
using Tandem.OpenAICompatible;
using Tandem.Packets;
using Tandem.Terminal;

namespace Cadence.Tests;

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
            Environment.SetEnvironmentVariable("CADENCE_TEST_OPENROUTER_KEY", null);
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
    [InlineData("")]
    [InlineData("   ")]
    public void Packet_reader_rejects_blank_verification_commands(string command)
    {
        var repository = TestSupport.CreateGitRepository();
        var packetPath = Path.Combine(Path.GetTempPath(), $"cadence-packet-{Guid.NewGuid():N}.md");
        try
        {
            File.WriteAllText(
                packetPath,
                $$"""
                ---
                title: Test packet
                repository: {{repository}}
                base: main
                outcomes:
                  - id: outcome-1
                    description: Deliver behavior
                acceptance: []
                verification:
                  - label: test
                    command: "{{command}}"
                ---
                Implement the behavior.
                """
            );

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
    [InlineData("")]
    [InlineData("   ")]
    public void Packet_reader_rejects_blank_repository_commands(string command)
    {
        var repository = TestSupport.CreateGitRepository();
        var packetPath = Path.Combine(Path.GetTempPath(), $"cadence-packet-{Guid.NewGuid():N}.md");
        try
        {
            File.WriteAllText(packetPath, ValidPacket(repository, $"commands:\n  - \"{command}\""));

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
                  - " task generate "
                  - "task contracts"
                verification:
                  - label: "test-1"
                    command: " dotnet test "
                  - label: "test-2"
                    command: "dotnet test"
                constraints:
                  - " Preserve exact text "
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
            packet.Commands.Should().Equal(" task generate ", "task contracts");
            packet
                .Verification.Should()
                .Equal(
                    new VerificationCommand("test-1", "dotnet test"),
                    new VerificationCommand("test-2", "dotnet test")
                );
            packet.Constraints.Should().Equal(" Preserve exact text ");
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
        var repository = TestSupport.CreateGitRepository();
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
        var repository = TestSupport.CreateGitRepository();
        var content = ValidPacket(repository, "");
        var start = content.IndexOf($"{field}:", StringComparison.Ordinal);
        var end =
            field == "outcomes"
                ? content.IndexOf("acceptance:", start, StringComparison.Ordinal)
                : content.IndexOf("---", start, StringComparison.Ordinal);
        content = content.Remove(start, end - start);

        try
        {
            var act = () => PacketFile.Parse(content, new PacketValidator(), "packet.md");

            act.Should().Throw<PacketFileException>().WithMessage("*validation failed*");
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Theory]
    [InlineData("outcomes:\n  - null", "Packet outcomes must not contain null values.")]
    [InlineData(
        "constraints:\n  - null",
        "Packet constraints must not contain null or blank values."
    )]
    public void Packet_reader_reports_null_collection_elements_as_validation_failures(
        string replacement,
        string message
    )
    {
        var repository = TestSupport.CreateGitRepository();
        var content = replacement.StartsWith("outcomes:", StringComparison.Ordinal)
            ? ValidPacket(repository, "")
                .Replace(
                    "outcomes:\n  - id: outcome-1\n    description: Deliver behavior",
                    replacement,
                    StringComparison.Ordinal
                )
            : ValidPacket(repository, replacement);

        try
        {
            var act = () => PacketFile.Parse(content, new PacketValidator(), "packet.md");

            act.Should()
                .Throw<PacketFileException>()
                .Which.Problems.Should()
                .Contain(problem => problem.Message == message);
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Fact]
    public void Packet_reader_requires_yaml_ambiguous_commands_to_be_quoted()
    {
        var repository = TestSupport.CreateGitRepository();
        var content = ValidPacket(repository, "")
            .Replace("command: dotnet test", "command: true", StringComparison.Ordinal);

        try
        {
            var act = () => PacketFile.Parse(content, new PacketValidator(), "packet.md");

            act.Should()
                .Throw<PacketFileException>()
                .Which.Problems.Should()
                .Contain(problem => problem.Path == "$.verification[0].command");
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Fact]
    public void Checked_in_example_parses_through_the_production_reader()
    {
        var root = FindRepositoryRoot();

        var packet = PacketReader.Read(Path.Combine(root, "examples", "packet.md"));

        packet.Repository.Should().Be(root);
        packet.Commands.Should().Equal("task format");
        packet.Verification.Should().Equal(new VerificationCommand("check", "task check"));
        packet.ImplementationContext.Should().Contain("host boundary tests");
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
        var repository = TestSupport.CreateGitRepository();
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

    [Theory]
    [InlineData(LedgerRunStatus.Running, true)]
    [InlineData(LedgerRunStatus.Failed, true)]
    [InlineData(LedgerRunStatus.Faulted, true)]
    [InlineData(LedgerRunStatus.Interrupted, true)]
    [InlineData(LedgerRunStatus.Ready, false)]
    [InlineData(LedgerRunStatus.Cancelled, false)]
    public void Explicit_resume_statuses_are_operator_controlled(
        LedgerRunStatus status,
        bool resumable
    ) => Program.IsResumableStatus(status).Should().Be(resumable);

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
              "providers": {
                "local": { "baseUrl": "http://127.0.0.1:1/v1", "apiKeyEnvironmentVariable": null }
              },
              "profiles": {
                "executor": { "provider": "local", "model": "model", "contextWindowTokens": 1, "maxOutputTokens": 1, "checkpointAtPercent": 80 },
                "planner": { "provider": "local", "model": "model", "contextWindowTokens": 1, "maxOutputTokens": 1, "checkpointAtPercent": 80 },
                "reviewer": { "provider": "local", "model": "model", "contextWindowTokens": 1, "maxOutputTokens": 1, "checkpointAtPercent": 80 }
              }
            }
            """;
}
