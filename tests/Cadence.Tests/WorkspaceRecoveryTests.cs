using Cadence.Git;
using FluentAssertions;

namespace Cadence.Tests;

public sealed class WorkspaceRecoveryTests
{
    [Fact]
    public async Task Missing_retained_workspace_is_rejected_instead_of_recreated()
    {
        var repository = TestSupport.CreateGitRepository();
        var workspace = Path.Combine(Path.GetTempPath(), $"cadence-missing-{Guid.NewGuid():N}");
        var records = new FakeRecordSink();
        var state = CadenceState.Recover(
            TestSupport.Packet() with
            {
                Repository = repository,
            },
            TestSupport.Head(repository),
            workspace,
            new RecoveryRecord(null, null, null, null, null, [], 0, [], null)
        );
        try
        {
            var stage = new PrepareWorkspaceStage(
                new WorkspacePreparation(new GitProcess()),
                records
            );

            var act = async () =>
                await stage.ExecuteAsync(state, TestContext.Current.CancellationToken);

            await act.Should()
                .ThrowAsync<WorkspacePreparationException>()
                .WithMessage("*not found*");
            Directory.Exists(workspace).Should().BeFalse();
            records.Workspace.Should().BeNull();
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Fact]
    public async Task Existing_workspace_is_valid_when_head_matches_persisted_base_and_remote_is_absent()
    {
        var repository = TestSupport.CreateGitRepository();
        var workspace = Path.Combine(Path.GetTempPath(), $"cadence-resume-{Guid.NewGuid():N}");
        try
        {
            TestSupport.Git(Path.GetTempPath(), "clone", "--no-local", repository, workspace);
            TestSupport.Git(workspace, "remote", "remove", "origin");
            File.AppendAllText(Path.Combine(workspace, "README.md"), "recovered\n");
            var head = TestSupport.Head(repository);

            var result = await new WorkspacePreparation(new GitProcess()).ValidateExistingAsync(
                head,
                workspace,
                TestContext.Current.CancellationToken
            );

            result.PinnedBaseSha.Should().Be(head);
            File.ReadAllText(Path.Combine(workspace, "README.md")).Should().Contain("recovered");
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Existing_workspace_remains_valid_when_the_source_branch_advances()
    {
        var repository = TestSupport.CreateGitRepository();
        var workspace = Path.Combine(Path.GetTempPath(), $"cadence-resume-{Guid.NewGuid():N}");
        try
        {
            TestSupport.Git(Path.GetTempPath(), "clone", "--no-local", repository, workspace);
            TestSupport.Git(workspace, "remote", "remove", "origin");
            var pinnedBase = TestSupport.Head(repository);
            File.AppendAllText(Path.Combine(repository, "README.md"), "advanced\n");
            TestSupport.Git(repository, "add", "README.md");
            TestSupport.Git(repository, "commit", "-m", "advance base");

            var result = await new WorkspacePreparation(new GitProcess()).ValidateExistingAsync(
                pinnedBase,
                workspace,
                TestContext.Current.CancellationToken
            );

            result.PinnedBaseSha.Should().Be(pinnedBase);
            TestSupport.Head(repository).Should().NotBe(pinnedBase);
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Existing_workspace_rejects_a_configured_remote()
    {
        var repository = TestSupport.CreateGitRepository();
        var workspace = Path.Combine(Path.GetTempPath(), $"cadence-resume-{Guid.NewGuid():N}");
        try
        {
            TestSupport.Git(Path.GetTempPath(), "clone", "--no-local", repository, workspace);
            var act = async () =>
                await new WorkspacePreparation(new GitProcess()).ValidateExistingAsync(
                    TestSupport.Head(repository),
                    workspace,
                    TestContext.Current.CancellationToken
                );

            await act.Should().ThrowAsync<WorkspacePreparationException>().WithMessage("*remote*");
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
            if (Directory.Exists(workspace))
            {
                Directory.Delete(workspace, recursive: true);
            }
        }
    }
}
