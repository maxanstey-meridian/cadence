using System.Text.Json;
using Cadence.Host;
using FluentAssertions;

namespace Cadence.Tests;

public sealed class HostBoundaryTests
{
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

            var act = () => new YamlPacketReader().Read(packetPath);

            act.Should().Throw<InvalidOperationException>().WithMessage("*non-blank*");
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
            File.Delete(packetPath);
        }
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
