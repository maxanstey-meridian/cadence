using Cadence.Git;
using FluentAssertions;

namespace Cadence.Tests;

public sealed class CandidateLifecycleTests
{
    [Fact]
    public async Task Capture_creates_a_candidate_for_a_valid_no_change_packet()
    {
        var repository = TestSupport.CreateGitRepository();
        try
        {
            var baseSha = TestSupport.Head(repository);

            var outcome = await new CaptureCandidateStage(new GitProcess()).ExecuteAsync(
                TestSupport.State(repository),
                TestContext.Current.CancellationToken
            );
            var captured = outcome.Should().BeOfType<Outcome<CadenceState>.Success>().Subject.State;

            captured.CandidateSha.Should().NotBe(baseSha).And.Be(TestSupport.Head(repository));
            TestSupport.Git(repository, "diff", "--quiet", baseSha, captured.CandidateSha!);
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Fact]
    public async Task Capture_records_real_commit_and_invalidates_stale_verification_and_review()
    {
        var repository = TestSupport.CreateGitRepository();
        try
        {
            var oldCandidate = TestSupport.Head(repository);
            File.AppendAllText(Path.Combine(repository, "README.md"), "candidate\n");
            var state = TestSupport.State(repository) with
            {
                CandidateSha = oldCandidate,
                VerificationIndex = 1,
                VerificationResults = [Result(0, "old")],
                VerifiedCandidateSha = oldCandidate,
                ReviewerCandidateSha = oldCandidate,
                ReviewerDecision = AcceptedDecision(),
            };

            var outcome = await new CaptureCandidateStage(new GitProcess()).ExecuteAsync(
                state,
                TestContext.Current.CancellationToken
            );
            var captured = outcome.Should().BeOfType<Outcome<CadenceState>.Success>().Subject.State;

            captured.CandidateSha.Should().Be(TestSupport.Head(repository)).And.NotBe(oldCandidate);
            captured.VerificationIndex.Should().Be(0);
            captured.VerificationResults.Should().BeEmpty();
            captured.VerifiedCandidateSha.Should().BeNull();
            captured.ReviewerCandidateSha.Should().BeNull();
            captured.ReviewerDecision.Should().BeNull();
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Fact]
    public async Task Red_verification_retains_exact_command_evidence_for_the_candidate()
    {
        var repository = TestSupport.CreateGitRepository();
        try
        {
            var candidate = TestSupport.Head(repository);
            var packet = TestSupport.Packet() with
            {
                Repository = repository,
                Verification =
                [
                    "printf 'red-out'; printf 'red-error' >&2; printf 'generated' > generated.txt; exit 7",
                ],
            };
            var stage = new VerificationStage(new VerificationOperation(new GitProcess()));
            var pipeline = Pipeline.Start(stage, "red-verification").Build(stage);
            var result = await new PipelineRunner().RunAsync(
                pipeline,
                CadenceState.Create(packet, candidate, repository) with
                {
                    CandidateSha = candidate,
                },
                cancellationToken: TestContext.Current.CancellationToken
            );

            result.State.VerificationIndex.Should().Be(0);
            result.State.VerifiedCandidateSha.Should().BeNull();
            result.State.VerificationResults.Should().ContainSingle();
            result.State.VerificationResults[0].ExitCode.Should().Be(7);
            result.State.VerificationResults[0].Stdout.Should().Contain("red-out");
            result.State.VerificationResults[0].Stderr.Should().Contain("red-error");
            result
                .State.VerificationResults[0]
                .Stderr.Should()
                .Contain("modified the captured candidate");
            File.Exists(Path.Combine(repository, "generated.txt")).Should().BeFalse();
            var status = await new GitProcess().RunAsync(
                repository,
                ["status", "--porcelain"],
                TestContext.Current.CancellationToken
            );
            status.Stdout.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    private static VerificationResult Result(int exitCode, string output) =>
        new(0, "test", exitCode, output, "", TimeSpan.Zero, false);

    private static ReviewDecision AcceptedDecision() =>
        new(
            ReviewDecisionValue.Accept,
            TestSupport.Doctrine().Sha256,
            "Delivered",
            [new ReviewOutcomeAssessment("outcome-1", true, [TestSupport.FileEvidence()])],
            [],
            []
        );
}
