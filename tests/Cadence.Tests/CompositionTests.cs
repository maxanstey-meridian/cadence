using System.Runtime.CompilerServices;
using Cadence.Git;
using FluentAssertions;
using Microsoft.Extensions.AI;

namespace Cadence.Tests;

public sealed class CompositionTests
{
    [Fact]
    public void Graph_exposes_the_complete_single_run_lifecycle()
    {
        var git = new GitProcess();
        var checkpointPolicy = new DirtyWorkCheckpointPolicy(git, TimeProvider.System);
        var capabilities = CadenceCapabilities.Create(TimeProvider.System, checkpointPolicy);
        var factory = new CadenceParticipantsFactory(
            _ => new FakeChatClient(),
            _ => new CadenceAgentProfile(200_000, 32_000, 80),
            TestSupport.Doctrine(),
            [],
            new WorkspacePreparation(git),
            git,
            checkpointPolicy,
            capabilities.AskPlanner,
            capabilities.UpdateOutcomes,
            capabilities.SubmitReport,
            capabilities.WriteCheckpoint
        );

        var inspection = new CadenceComposition(factory).Build().Inspect();

        inspection.Name.Should().Be("cadence");
        inspection.StartStepId.Should().Be(CadenceIds.Prepare);
        inspection
            .StepIds.Should()
            .Contain([
                CadenceIds.Executor,
                CadenceIds.Planner,
                CadenceIds.PlannerFailure,
                CadenceIds.PlannerUnavailable,
                CadenceIds.CaptureCandidate,
                CadenceIds.Verify,
                CadenceIds.Reviewer,
                CadenceIds.AcceptCandidate,
                CadenceIds.Complete,
                CadenceIds.Failed,
            ]);
        inspection
            .Routes.Should()
            .Contain(route =>
                route.SourceId == CadenceIds.Executor && route.TargetId == CadenceIds.Planner
            );
        inspection
            .Routes.Count(route =>
                route.SourceId == CadenceIds.Executor && route.TargetId == CadenceIds.Planner
            )
            .Should()
            .Be(2, "both explicit questions and checkpoints route directly to Planner");
        inspection
            .Routes.Should()
            .Contain(route =>
                route.SourceId == CadenceIds.Planner && route.TargetId == CadenceIds.PlannerFailure
            );
        inspection
            .Routes.Should()
            .Contain(route =>
                route.SourceId == CadenceIds.PlannerFailure && route.TargetId == CadenceIds.Planner
            );
        inspection
            .Routes.Should()
            .Contain(route =>
                route.SourceId == CadenceIds.PlannerFailure
                && route.TargetId == CadenceIds.PlannerUnavailable
            );
        inspection
            .Routes.Should()
            .Contain(route =>
                route.SourceId == CadenceIds.Reviewer && route.TargetId == CadenceIds.Executor
            );
        inspection
            .Routes.Should()
            .Contain(route =>
                route.SourceId == CadenceIds.Reviewer
                && route.TargetId == CadenceIds.AcceptCandidate
            );
        inspection
            .Routes.Should()
            .Contain(route =>
                route.SourceId == CadenceIds.AcceptCandidate
                && route.TargetId == CadenceIds.Complete
            );
        inspection
            .Routes.Should()
            .NotContain(route =>
                route.SourceId == CadenceIds.CaptureCandidate
                && route.TargetId == CadenceIds.Reviewer
            );
    }

    private sealed class FakeChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        ) => throw new InvalidOperationException("Fake client must not execute.");

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            await Task.CompletedTask;
            yield break;
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}
