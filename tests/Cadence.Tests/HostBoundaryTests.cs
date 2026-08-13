using System.Text.Json;
using System.Text.Json.Serialization;
using Cadence.Host;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Tandem.OpenAICompatible;
using Tandem.Packets;
using Tandem.Terminal;

namespace Cadence.Tests;

public sealed class HostBoundaryTests
{
    [Fact]
    public async Task Terminal_human_interaction_submits_typed_answers_through_the_display_seam()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cadence-human-{Guid.NewGuid():N}");
        try
        {
            var store = new RunRecordStore(Path.Combine(directory, "records.json"));
            await store.InitializeAsync(
                TestSupport.Packet(),
                TestContext.Current.CancellationToken
            );
            var terminal = new TerminalHumanInteraction(store);
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

            var waiting = terminal.WaitForReviewerAsync(
                context,
                TestContext.Current.CancellationToken
            );

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
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
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
            executor.Should().BeOfType<OpenRouterReasoningChatClient>();
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
    public void Host_configuration_resolves_and_loads_required_reviewer_doctrine_once_from_config_directory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cadence-config-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var configPath = Path.Combine(directory, "config.json");
        var doctrinePath = Path.Combine(directory, "reviewer.md");
        try
        {
            File.WriteAllText(doctrinePath, "Exact doctrine bytes.\n");
            File.WriteAllText(configPath, ConfigurationJson("reviewer.md"));

            var configuration = HostConfiguration.Load(configPath);
            var doctrine = ReviewerDoctrine.Load(
                configuration.ResolveReviewerDoctrinePath(configPath)
            );

            doctrine.Source.Should().Be(doctrinePath);
            doctrine.Content.Should().Be("Exact doctrine bytes.\n");
            doctrine
                .Sha256.Should()
                .Be("00ebc1df21ec80dc84bb28af0313eecdd9fa8f11e8c548636955efc827f5ebda");
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
        var path = Path.Combine(Path.GetTempPath(), $"cadence-doctrine-{Guid.NewGuid():N}.md");
        var missing = () => ReviewerDoctrine.Load(path);
        missing.Should().Throw<InvalidOperationException>().WithMessage("*not found*");

        File.WriteAllText(path, " \n\t");
        try
        {
            var blank = () => ReviewerDoctrine.Load(path);
            blank.Should().Throw<InvalidOperationException>().WithMessage("*blank*");
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
    public async Task Record_store_persists_the_latest_accepted_outcome_ledger_snapshot()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cadence-ledger-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "records.json");
        try
        {
            var store = new RunRecordStore(path);
            await store.InitializeAsync(
                TestSupport.Packet(),
                TestContext.Current.CancellationToken
            );
            await store.AcceptOutcomeLedgerAsync(
                "update-1",
                [
                    new OutcomeLedgerEntry(
                        "outcome-1",
                        "Deliver the feature",
                        OutcomeStatus.Complete,
                        ["src/a.cs: implemented"],
                        "Implemented.",
                        null
                    ),
                ],
                TestContext.Current.CancellationToken
            );

            var context = await store.ReadContextAsync(
                CadenceLedgerRole.Reviewer,
                TestContext.Current.CancellationToken
            );

            context.Outcomes!.AcceptedDecisionId.Should().Be("update-1");
            context
                .Outcomes.Outcomes.Should()
                .ContainSingle()
                .Which.Status.Should()
                .Be(OutcomeStatus.Complete);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
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
                verification:
                  - "{{command}}"
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
                verification:
                  - " dotnet test "
                  - "dotnet test"
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
            packet.Verification.Should().Equal(" dotnet test ", "dotnet test");
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
                ? content.IndexOf("verification:", start, StringComparison.Ordinal)
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
            .Replace("  - dotnet test", "  - true", StringComparison.Ordinal);

        try
        {
            var act = () => PacketFile.Parse(content, new PacketValidator(), "packet.md");

            act.Should()
                .Throw<PacketFileException>()
                .Which.Problems.Should()
                .Contain(problem => problem.Path == "$.verification[0]");
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
        packet.Verification.Should().Equal("task check");
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

    private static string ValidPacket(string repository, string extra) =>
        $$"""
            ---
            title: Test packet
            repository: {{repository}}
            base: main
            outcomes:
              - id: outcome-1
                description: Deliver behavior
            verification:
              - dotnet test
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

    [Fact]
    public async Task Record_store_acceptance_ids_are_idempotent()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cadence-records-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "records.json");
        try
        {
            var store = new RunRecordStore(path);
            var checkpoint = new ProgressCheckpointRecord("Summary", [], [], [], "Next");
            var verification = new VerificationResult(
                0,
                "test",
                0,
                "passed",
                "",
                TimeSpan.Zero,
                false
            );

            await store.AcceptCheckpointAsync(
                "checkpoint-1",
                checkpoint,
                TestContext.Current.CancellationToken
            );
            await store.AcceptCheckpointAsync(
                "checkpoint-1",
                checkpoint,
                TestContext.Current.CancellationToken
            );
            await store.AcceptVerificationResultAsync(
                "verification-1",
                "candidate",
                verification,
                TestContext.Current.CancellationToken
            );
            await store.AcceptVerificationResultAsync(
                "verification-1",
                "candidate",
                verification,
                TestContext.Current.CancellationToken
            );

            using var document = JsonDocument.Parse(
                await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)
            );
            document.RootElement.GetProperty("checkpoints").GetArrayLength().Should().Be(1);
            document.RootElement.GetProperty("verificationResults").GetArrayLength().Should().Be(1);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Record_store_records_typed_planner_decision_directly_without_reserialization()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cadence-records-{Guid.NewGuid():N}");
        var path = Path.Combine(directory, "records.json");
        try
        {
            var store = new RunRecordStore(path);
            var decision = new PlannerDecision(
                PlannerDecisionValue.Proceed,
                "go",
                [],
                ["README.md"],
                "implement the outcome"
            );

            await store.ObserveAsync(
                new OutputAccepted<PlannerDecision>(
                    Guid.CreateVersion7(),
                    CadenceIds.Planner,
                    "planner-output-1",
                    "agent.success",
                    typeof(PlannerDecision).FullName,
                    JsonSerializer.SerializeToElement(
                        decision,
                        new JsonSerializerOptions(JsonSerializerDefaults.Web)
                        {
                            Converters = { new JsonStringEnumConverter() },
                        }
                    ),
                    decision
                ),
                TestContext.Current.CancellationToken
            );

            var context = await store.ReadContextAsync(
                CadenceLedgerRole.Reviewer,
                TestContext.Current.CancellationToken
            );
            var recorded = context.PlannerDecisions.Should().ContainSingle().Which;
            recorded.Decision.Should().Be(PlannerDecisionValue.Proceed);
            recorded.Rationale.Should().Be("go");
            recorded.SafeNextAction.Should().Be("implement the outcome");

            await store.ObserveAsync(
                new OutputAccepted<PlannerDecision>(
                    Guid.CreateVersion7(),
                    "another-step",
                    "other-output",
                    "agent.success",
                    typeof(PlannerDecision).FullName,
                    JsonSerializer.SerializeToElement(decision),
                    decision
                ),
                TestContext.Current.CancellationToken
            );
            (
                await store.ReadContextAsync(
                    CadenceLedgerRole.Reviewer,
                    TestContext.Current.CancellationToken
                )
            )
                .PlannerDecisions.Should()
                .ContainSingle();
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static string ConfigurationJson(
        string? doctrineFile,
        IReadOnlyList<string>? skillDirectories = null
    ) =>
        $$"""
            {
              "reviewerDoctrineFile": {{JsonSerializer.Serialize(doctrineFile)}},
              "skillDirectories": {{JsonSerializer.Serialize(skillDirectories ?? [])}},
              "providers": {},
              "profiles": {
                "executor": { "provider": "local", "model": "model", "contextWindowTokens": 1, "maxOutputTokens": 1, "checkpointAtPercent": 80 },
                "planner": { "provider": "local", "model": "model", "contextWindowTokens": 1, "maxOutputTokens": 1, "checkpointAtPercent": 80 },
                "reviewer": { "provider": "local", "model": "model", "contextWindowTokens": 1, "maxOutputTokens": 1, "checkpointAtPercent": 80 }
              }
            }
            """;
}
