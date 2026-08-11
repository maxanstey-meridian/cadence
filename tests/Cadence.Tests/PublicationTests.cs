using Cadence.Git;
using FluentAssertions;

namespace Cadence.Tests;

public sealed class PublicationTests
{
    [Fact]
    public async Task Explicit_publication_branch_must_remain_isolated()
    {
        var records = new FakeRecordSink();
        await records.AcceptPublicationCandidateAsync(
            "accepted",
            new PublicationCandidateDocument(
                "accepted",
                "/repository",
                "/workspace",
                "Feature",
                "base",
                "1234567890abcdef",
                TestSupport.Doctrine().Source,
                TestSupport.Doctrine().Sha256,
                [Assessment()],
                [Verification()],
                Decision()
            ),
            TestContext.Current.CancellationToken
        );

        var act = async () =>
            await new PublicationOperation(new GitProcess(), records).ExecuteAsync(
                "main",
                TestContext.Current.CancellationToken
            );

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
            var records = new FakeRecordSink();
            await records.AcceptPublicationCandidateAsync(
                "accepted",
                new PublicationCandidateDocument(
                    "accepted",
                    repository,
                    workspace,
                    "Feature",
                    TestSupport.Head(repository),
                    TestSupport.Head(repository),
                    TestSupport.Doctrine().Source,
                    TestSupport.Doctrine().Sha256,
                    [Assessment()],
                    [Verification()],
                    Decision()
                ),
                TestContext.Current.CancellationToken
            );

            var act = async () =>
                await new PublicationOperation(new GitProcess(), records).ExecuteAsync(
                    "cadence/feature",
                    TestContext.Current.CancellationToken
                );

            await act.Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage("*does not equal candidate*");
            records.PublicationResults.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
            Directory.Delete(workspace, recursive: true);
        }
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
            var records = new FakeRecordSink();
            await records.AcceptPublicationCandidateAsync(
                "accepted",
                new PublicationCandidateDocument(
                    "accepted",
                    repository,
                    workspace,
                    "Feature",
                    sourceHead,
                    candidateSha,
                    TestSupport.Doctrine().Source,
                    TestSupport.Doctrine().Sha256,
                    [Assessment()],
                    [Verification()],
                    Decision()
                ),
                TestContext.Current.CancellationToken
            );
            var operation = new PublicationOperation(new GitProcess(), records);

            var first = await operation.ExecuteAsync(
                "cadence/feature",
                TestContext.Current.CancellationToken
            );
            var second = await operation.ExecuteAsync(
                "cadence/feature",
                TestContext.Current.CancellationToken
            );

            first.Should().Be(second);
            first.CandidateSha.Should().Be(candidateSha);
            TestSupport.Git(repository, "rev-parse", "--verify", "refs/heads/cadence/feature");
            TestSupport.Head(repository).Should().Be(sourceHead);
            records
                .PublicationResults.Should()
                .HaveCount(2)
                .And.OnlyContain(result => result.Reconciled);
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
}
