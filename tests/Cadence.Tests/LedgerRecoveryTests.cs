using Cadence.Git;
using FluentAssertions;
using Tandem.Ledger;

namespace Cadence.Tests;

public sealed class LedgerRecoveryTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"cadence-ledger-recovery-{Guid.NewGuid():N}"
    );

    [Fact]
    public async Task AcceptedCadenceState_ReopensFromLedgerAndResumesFromDurableFacts()
    {
        var path = Path.Combine(_directory, "ledger.sqlite3");
        var repository = TestSupport.CreateGitRepository();
        var workspace = Path.Combine(_directory, "workspace");
        var runId = Guid.CreateVersion7();
        try
        {
            var store = new SqliteLedgerStore(path);
            var observer = await store.CreateObserverAsync(
                runId,
                "cadence",
                TestContext.Current.CancellationToken
            );
            var packet = TestSupport.Packet() with { Repository = repository };
            var initial = CadenceState
                .Create(packet, string.Empty, workspace)
                .RecordCheckpoint(
                    new WriteCheckpointRequest(
                        "Durable checkpoint",
                        ["Confirm the integration seam."],
                        "Ask Planner"
                    ),
                    DateTimeOffset.Parse("2026-08-14T10:00:00Z")
                );
            var stage = new PrepareWorkspaceStage(new WorkspacePreparation(new GitProcess()));
            var pipeline = Pipeline.Start(stage, "ledger-recovery").Persist().Build(stage);

            var persisted = await new PipelineRunner().RunAsync(
                pipeline,
                initial,
                new PipelineRunOptions(runId, Observer: observer),
                TestContext.Current.CancellationToken
            );

            var reopened = new SqliteLedgerStore(path);
            var accepted = await reopened.ReadLatestAcceptedAsync<CadenceState>(
                runId,
                TestContext.Current.CancellationToken
            );
            var resumed = accepted!.Value.Resume(packet);

            resumed.WorkspacePath.Should().Be(workspace);
            resumed.PinnedBaseSha.Should().Be(TestSupport.Head(repository));
            resumed.LatestCheckpoint.Should().BeEquivalentTo(persisted.State.LatestCheckpoint);
            resumed.ExecutorTransition.Should().BeOfType<ExecutorTransition.PlannerRequested>();
            resumed.MutationAuthorized.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
