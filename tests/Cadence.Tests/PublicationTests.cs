using Cadence.Git;
using FluentAssertions;

namespace Cadence.Tests;

public sealed class PublicationTests
{
    [Fact]
    public async Task Explicit_publication_branch_must_remain_isolated()
    {
        var state = AcceptedState("/repository", "/workspace", "base", "1234567890abcdef");

        var act = async () =>
            await Operation().ExecuteAsync(state, "main", TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*cadence/*");
    }

    [Fact]
    public async Task Publication_refuses_workspace_head_different_from_accepted_candidate()
    {
        var repository = TestSupport.CreateGitRepository();
        var workspace = TestSupport.CreateGitRepository();
        try
        {
            File.AppendAllText(Path.Combine(workspace, "README.md"), "different head\n");
            TestSupport.Git(workspace, "add", "README.md");
            TestSupport.Git(workspace, "commit", "-m", "different");
            var state = AcceptedState(
                repository,
                workspace,
                TestSupport.Head(repository),
                TestSupport.Head(repository)
            );

            var act = async () =>
                await Operation()
                    .ExecuteAsync(state, "cadence/feature", TestContext.Current.CancellationToken);

            await act.Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage("*does not equal candidate*");
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
            Directory.Delete(workspace, recursive: true);
        }
    }

    [Fact]
    public async Task Publication_refuses_candidate_not_accepted_by_acceptance_stage()
    {
        var state = AcceptedState("/repository", "/workspace", "base", "1234567890abcdef") with
        {
            AcceptedCandidateSha = null,
        };

        var act = async () =>
            await Operation()
                .ExecuteAsync(state, "cadence/feature", TestContext.Current.CancellationToken);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*no accepted publication candidate*");
    }

    [Fact]
    public async Task Publication_refuses_candidate_reviewed_under_another_doctrine()
    {
        var state = AcceptedState("/repository", "/workspace", "base", "1234567890abcdef");

        var act = async () =>
            await new PublicationOperation(new GitProcess(), "different-doctrine").ExecuteAsync(
                state,
                "cadence/feature",
                TestContext.Current.CancellationToken
            );

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*doctrine does not match*");
    }

    [Fact]
    public async Task Publishing_the_same_candidate_twice_reconciles_idempotently()
    {
        var repository = TestSupport.CreateGitRepository();
        var workspace = Path.Combine(Path.GetTempPath(), $"cadence-publish-{Guid.NewGuid():N}");
        try
        {
            TestSupport.Git(repository, "clone", repository, workspace);
            var sourceHead = TestSupport.Head(repository);
            File.AppendAllText(Path.Combine(workspace, "README.md"), "candidate\n");
            TestSupport.Git(workspace, "config", "user.name", "Cadence Tests");
            TestSupport.Git(workspace, "config", "user.email", "cadence-tests@localhost");
            TestSupport.Git(workspace, "add", "README.md");
            TestSupport.Git(workspace, "commit", "-m", "candidate");
            var candidateSha = TestSupport.Head(workspace);
            var state = AcceptedState(repository, workspace, sourceHead, candidateSha);
            var operation = Operation();

            var first = await operation.ExecuteAsync(
                state,
                "cadence/feature",
                TestContext.Current.CancellationToken
            );
            var second = await operation.ExecuteAsync(
                state,
                "cadence/feature",
                TestContext.Current.CancellationToken
            );

            first.Should().Be(second);
            first.CandidateSha.Should().Be(candidateSha);
            TestSupport.Git(repository, "rev-parse", "--verify", "refs/heads/cadence/feature");
            TestSupport.Head(repository).Should().Be(sourceHead);
            first.Reconciled.Should().BeTrue();
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

    private static ReviewOutcomeAssessment Assessment() =>
        new("outcome-1", true, [TestSupport.FileEvidence()]);

    private static PublicationOperation Operation() =>
        new(new GitProcess(), TestSupport.Doctrine().Sha256);

    private static VerificationResult Verification() =>
        new(0, "test", 0, "passed", "", TimeSpan.Zero, false);

    private static ReviewDecision Decision() =>
        new(
            ReviewDecisionValue.Accept,
            TestSupport.Doctrine().Sha256,
            "Accepted",
            [Assessment()],
            [],
            []
        );

    private static CadenceState AcceptedState(
        string repository,
        string workspace,
        string baseSha,
        string candidateSha
    ) =>
        TestSupport.State(repository) with
        {
            Packet = TestSupport.Packet() with { Repository = repository, Title = "Feature" },
            WorkspacePath = workspace,
            PinnedBaseSha = baseSha,
            CandidateSha = candidateSha,
            VerifiedCandidateSha = candidateSha,
            VerificationIndex = 1,
            VerificationResults = [Verification()],
            ReviewerCandidateSha = candidateSha,
            ReviewerDecision = Decision(),
            AcceptedCandidateSha = candidateSha,
        };
}
