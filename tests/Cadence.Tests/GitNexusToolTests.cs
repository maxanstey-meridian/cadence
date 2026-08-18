using FluentAssertions;

namespace Cadence.Tests;

public sealed class GitNexusToolTests
{
    [Theory]
    [InlineData("status")]
    [InlineData("analyze")]
    [InlineData("impact")]
    [InlineData("detect-changes")]
    [InlineData("query")]
    public void Repository_analysis_subcommands_are_available(string subcommand)
    {
        var validate = () => GitNexusTool.Validate(subcommand, []);

        validate.Should().NotThrow();
    }

    [Theory]
    [InlineData("setup")]
    [InlineData("uninstall")]
    [InlineData("clean")]
    [InlineData("remove")]
    [InlineData("publish")]
    [InlineData("serve")]
    [InlineData("mcp")]
    [InlineData("eval-server")]
    public void Host_destructive_or_long_running_subcommands_are_rejected(string subcommand)
    {
        var validate = () => GitNexusTool.Validate(subcommand, []);

        validate
            .Should()
            .Throw<ArgumentException>()
            .WithMessage($"*'{subcommand}' is not available*");
    }

    [Fact]
    public void Process_arguments_are_forwarded_without_shell_interpretation()
    {
        var workspace = Workspace("019fff963b06736fbe9d5af6ccd4e784");

        var startInfo = GitNexusRepository.BuildStartInfo(
            workspace,
            "impact",
            ["CaseCreationPlanner", "--direction", "upstream"]
        );

        startInfo.FileName.Should().Be("gitnexus");
        startInfo.WorkingDirectory.Should().Be(Path.GetFullPath(workspace));
        startInfo.UseShellExecute.Should().BeFalse();
        startInfo
            .ArgumentList.Should()
            .Equal(
                "impact",
                "CaseCreationPlanner",
                "--direction",
                "upstream",
                "--repo",
                "cadence-019fff963b06736fbe9d5af6ccd4e784"
            );
    }

    [Fact]
    public void Analyze_is_forced_to_the_current_workspace()
    {
        var workspace = Workspace("019fff963b06736fbe9d5af6ccd4e784");

        var arguments = GitNexusTool.BuildArguments(workspace, "analyze", ["--index-only"]);

        arguments
            .Should()
            .Equal(
                "analyze",
                Path.GetFullPath(workspace),
                "--name",
                "cadence-019fff963b06736fbe9d5af6ccd4e784",
                "--index-only"
            );
    }

    [Theory]
    [InlineData("--repo")]
    [InlineData("-r")]
    [InlineData("--repo=Casebridge")]
    [InlineData("-r=Casebridge")]
    [InlineData("--name")]
    [InlineData("--name=Casebridge")]
    [InlineData("--allow-duplicate-name")]
    public void Repository_overrides_are_rejected(string argument)
    {
        var validate = () => GitNexusTool.Validate("impact", [argument, "Casebridge"]);

        validate
            .Should()
            .Throw<ArgumentException>()
            .WithMessage("*owns GitNexus repository identity*");
    }

    [Fact]
    public void Duplicate_repository_names_are_disambiguated_by_absolute_workspace_path()
    {
        var first = Workspace("019fff963b06736fbe9d5af6ccd4e784");
        var second = Workspace("019fff963b06736fbe9d5af6ccd4e785");

        var firstArguments = GitNexusTool.BuildArguments(first, "impact", ["ChangeStatusUseCase"]);
        var secondArguments = GitNexusTool.BuildArguments(
            second,
            "impact",
            ["ChangeStatusUseCase"]
        );

        firstArguments.Should().EndWith(["--repo", "cadence-019fff963b06736fbe9d5af6ccd4e784"]);
        secondArguments.Should().EndWith(["--repo", "cadence-019fff963b06736fbe9d5af6ccd4e785"]);
        firstArguments.Should().NotEqual(secondArguments);
    }

    [Fact]
    public async Task Missing_run_index_is_created_lazily_then_the_command_is_retried()
    {
        var workspace = Workspace("019fff963b06736fbe9d5af6ccd4e784");
        var invocations = new List<IReadOnlyList<string>>();
        var impactAttempts = 0;
        var repository = new GitNexusRepository(
            workspace,
            (startInfo, _) =>
            {
                var arguments = startInfo.ArgumentList.ToArray();
                invocations.Add(arguments);
                if (arguments[0] == "impact" && impactAttempts++ == 0)
                {
                    return Task.FromResult(
                        new GitNexusProcessResult(
                            1,
                            "Repository cadence-019fff963b06736fbe9d5af6ccd4e784 not found",
                            ""
                        )
                    );
                }
                return Task.FromResult(new GitNexusProcessResult(0, "ok", ""));
            }
        );

        var result = await repository.RunAsync(
            "impact",
            ["ChangeStatusUseCase"],
            TestContext.Current.CancellationToken
        );

        result.Should().Contain("stdout:\nok");
        invocations.Should().HaveCount(3);
        invocations[0]
            .Should()
            .Equal(
                "impact",
                "ChangeStatusUseCase",
                "--repo",
                "cadence-019fff963b06736fbe9d5af6ccd4e784"
            );
        invocations[1]
            .Should()
            .Equal(
                "analyze",
                Path.GetFullPath(workspace),
                "--name",
                "cadence-019fff963b06736fbe9d5af6ccd4e784",
                "--index-only"
            );
        invocations[2].Should().Equal(invocations[0]);
    }

    private static string Workspace(string runId) =>
        Path.Combine(Path.GetTempPath(), ".cadence", "runs", runId, "workspace");
}
