using Cadence.Git;
using FluentAssertions;

namespace Cadence.Tests;

public sealed class CandidateAcceptanceTests
{
    [Fact]
    public async Task Exact_reviewer_accepted_candidate_becomes_publishable()
    {
        var repository = TestSupport.CreateGitRepository();
        var records = new FakeRecordSink();
        var candidateSha = TestSupport.Head(repository);
        var state = TestSupport.State(repository) with
        {
            CandidateSha = candidateSha,
            VerifiedCandidateSha = candidateSha,
            VerificationIndex = 1,
            VerificationResults =
            [
                new VerificationResult(0, "dotnet test", 0, "ok", "", TimeSpan.Zero, false),
            ],
            ReviewerCandidateSha = candidateSha,
            ReviewerDecision = AcceptedDecision(),
        };

        try
        {
            var result = await Stage(records).ExecuteAsync(state, CancellationToken.None);

            result.Should().BeOfType<Outcome<CadenceState>.Success>();
            records.Candidate.Should().NotBeNull();
            records.Candidate!.CandidateSha.Should().Be(candidateSha);
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Fact]
    public async Task Stale_reviewer_decision_cannot_make_a_new_candidate_publishable()
    {
        var records = new FakeRecordSink();
        var state = TestSupport.State() with
        {
            CandidateSha = "new-candidate",
            VerifiedCandidateSha = "new-candidate",
            VerificationIndex = 1,
            VerificationResults =
            [
                new VerificationResult(0, "dotnet test", 0, "ok", "", TimeSpan.Zero, false),
            ],
            ReviewerCandidateSha = "old-candidate",
            ReviewerDecision = AcceptedDecision(),
        };

        var act = async () => await Stage(records).ExecuteAsync(state, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exact candidate*");
        records.Candidate.Should().BeNull();
    }

    [Fact]
    public async Task Unverified_candidate_cannot_become_publishable()
    {
        var records = new FakeRecordSink();
        var state = TestSupport.State() with
        {
            CandidateSha = "candidate-sha",
            ReviewerCandidateSha = "candidate-sha",
            ReviewerDecision = AcceptedDecision(),
        };

        var act = async () => await Stage(records).ExecuteAsync(state, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*exact candidate*");
    }

    [Theory]
    [InlineData("tracked")]
    [InlineData("untracked")]
    [InlineData("head")]
    public async Task Workspace_changes_after_review_prevent_acceptance(string change)
    {
        var repository = TestSupport.CreateGitRepository();
        try
        {
            var candidateSha = TestSupport.Head(repository);
            var state = AcceptedState(repository, candidateSha);
            switch (change)
            {
                case "tracked":
                    File.AppendAllText(Path.Combine(repository, "README.md"), "changed\n");
                    break;
                case "untracked":
                    File.WriteAllText(Path.Combine(repository, "untracked.txt"), "changed\n");
                    break;
                case "head":
                    File.WriteAllText(Path.Combine(repository, "next.txt"), "next\n");
                    TestSupport.Git(repository, "add", "next.txt");
                    TestSupport.Git(repository, "commit", "-m", "next");
                    break;
            }

            var act = async () =>
                await Stage(new FakeRecordSink()).ExecuteAsync(state, CancellationToken.None);

            await act.Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage("*workspace HEAD*");
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    private static AcceptCandidateStage Stage(FakeRecordSink records) =>
        new(records, TestSupport.Doctrine(), new GitProcess());

    private static CadenceState AcceptedState(string repository, string candidateSha) =>
        TestSupport.State(repository) with
        {
            CandidateSha = candidateSha,
            VerifiedCandidateSha = candidateSha,
            VerificationIndex = 1,
            VerificationResults =
            [
                new VerificationResult(0, "dotnet test", 0, "ok", "", TimeSpan.Zero, false),
            ],
            ReviewerCandidateSha = candidateSha,
            ReviewerDecision = AcceptedDecision(),
        };

    private static ReviewDecision AcceptedDecision() =>
        new(
            ReviewDecisionValue.Accept,
            TestSupport.Doctrine().Sha256,
            "The candidate delivers the outcome.",
            [
                new ReviewOutcomeAssessment(
                    "outcome-1",
                    true,
                    [TestSupport.FileEvidence("src/a.cs")]
                ),
            ],
            [],
            []
        );
}
