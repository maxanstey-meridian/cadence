using FluentAssertions;

namespace Cadence.Tests;

public sealed class OutcomeLedgerTests
{
    [Fact]
    public void State_initializes_one_authoritative_entry_per_packet_outcome()
    {
        var packet = TestSupport.Packet() with
        {
            Outcomes =
            [
                new PacketOutcome("first", "First objective"),
                new PacketOutcome("second", "Second objective"),
            ],
        };

        var state = CadenceState.Create(packet, "base", "/workspace");

        state.OutcomeLedger.Select(entry => entry.OutcomeId).Should().Equal("first", "second");
        state.OutcomeLedger.Should().OnlyContain(entry => entry.Status == OutcomeStatus.NotStarted);
    }

    [Fact]
    public async Task Validator_rejects_the_entire_partial_update_when_any_entry_is_unknown_or_invalid()
    {
        var request = new UpdateOutcomesRequest([
            Complete("outcome-1"),
            Complete("unknown") with
            {
                NextAction = "Complete statuses cannot have next actions.",
            },
        ]);

        var result = await new UpdateOutcomesRequestValidator(TestSupport.State()).ValidateAsync(
            request,
            TestContext.Current.CancellationToken
        );

        result.IsValid.Should().BeFalse();
        result
            .Errors.Should()
            .Contain(error => error.ErrorCode == "update_outcomes.outcome.unknown");
        result
            .Errors.Should()
            .Contain(error => error.ErrorCode == "outcome_update.next_action.forbidden");
    }

    [Fact]
    public void Partial_updates_preserve_other_entries_and_can_reopen_completed_work()
    {
        var packet = TestSupport.Packet() with
        {
            Outcomes =
            [
                new PacketOutcome("first", "First objective"),
                new PacketOutcome("second", "Second objective"),
            ],
        };
        var state = CadenceState
            .Create(packet, "base", "/workspace")
            .RecordOutcomeUpdates(new UpdateOutcomesRequest([Complete("first")]))
            .RecordOutcomeUpdates(
                new UpdateOutcomesRequest([
                    new OutcomeUpdate(
                        "first",
                        OutcomeStatus.InProgress,
                        ["A worktree spot-check disproved the prior claim."],
                        "The implementation is incomplete.",
                        "Repair the missing branch."
                    ),
                ])
            );

        state
            .OutcomeLedger.Single(entry => entry.OutcomeId == "first")
            .Status.Should()
            .Be(OutcomeStatus.InProgress);
        state
            .OutcomeLedger.Single(entry => entry.OutcomeId == "second")
            .Status.Should()
            .Be(OutcomeStatus.NotStarted);
    }

    [Fact]
    public void Accepted_progress_update_invalidates_downstream_candidate_facts()
    {
        var state = TestSupport.State() with
        {
            CandidateSha = "candidate",
            VerifiedCandidateSha = "candidate",
            VerificationIndex = 1,
            VerificationResults =
            [
                new VerificationResult(0, "test", 0, "ok", "", TimeSpan.Zero, false),
            ],
            ReviewerDecision = new ReviewDecision(
                ReviewDecisionValue.Accept,
                TestSupport.Doctrine().Sha256,
                "Accepted",
                [
                    new ReviewOutcomeAssessment(
                        "outcome-1",
                        true,
                        [TestSupport.FileEvidence("src/a.cs")]
                    ),
                ],
                [],
                []
            ),
            ReviewerCandidateSha = "candidate",
        };

        state = state.RecordOutcomeUpdates(new UpdateOutcomesRequest([Complete("outcome-1")]));

        state.CandidateSha.Should().BeNull();
        state.VerifiedCandidateSha.Should().BeNull();
        state.VerificationResults.Should().BeEmpty();
        state.ReviewerDecision.Should().BeNull();
        state.ExecutorTransition.Should().BeOfType<ExecutorTransition.OutcomeLedgerUpdated>();
    }

    [Fact]
    public void Executor_prompt_preserves_operational_scar_tissue_and_separates_checkpoint_claims()
    {
        var state = TestSupport.State() with
        {
            LatestCheckpoint = new WriteCheckpointRequest(
                "A predecessor claimed completion.",
                ["The worktree was not spot-checked."],
                "Verify the claim."
            ),
        };
        var prompt = ExecutorPrompts.Instructions + ExecutorPrompts.BuildMessage(state);

        prompt
            .Should()
            .ContainAll(
                "smallest change",
                "nearest established repository pattern",
                "unrelated refactors",
                "formatting churn",
                "exact prior instruction",
                "exact attempted change",
                "exact failing command",
                "claims, not proof",
                "reopen any outcome",
                "Delivery roadmap (authoritative outcome ledger)",
                "Non-authoritative continuity checkpoint"
            );
        typeof(WriteCheckpointRequest)
            .GetProperties()
            .Select(property => property.Name)
            .Should()
            .Equal("Summary", "Uncertainties", "NextAction");
    }

    private static OutcomeUpdate Complete(string id) =>
        new(id, OutcomeStatus.Complete, ["src/a.cs: implemented"], "Implemented.", null);
}
