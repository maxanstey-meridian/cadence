using Tandem.Advanced;

namespace Cadence;

public static class ExecutorPrompts
{
    public static string BuildMessage(CadenceState state)
    {
        var contract = DeliveryContractRenderer.Render(state);
        var progress = string.Join(
            "\n",
            state.OutcomeProgress.Select(item =>
                $"- [{item.OutcomeId}] {item.Status}: {item.Evidence} Next: {item.NextAction ?? "(complete)"}"
            )
        );
        var planner = state.PlannerDecision is { } decision
            ? $"""

                Latest accepted Planner decision: {decision.Decision}
                Latest Planner rationale: {decision.Rationale}
                Safe next action (continuity context; not implementation scope): {decision.SafeNextAction}
                Corrected approach: {decision.CorrectedApproach ?? "(none)"}
                """
            : "";
        var verification =
            state.VerificationResults.Count > 0
                ? $"\nCurrent mechanical verification results for the candidate:\n{VerificationResultFormatting.Format(state.VerificationResults)}"
                : "";
        var candidate = state.CandidateSha is { } sha
            ? $"\nCurrent mechanical candidate SHA: {sha}"
            : "";
        var activeReviewFindings =
            state.ActiveReviewFindings.Count > 0
                ? $"\n\nCurrent repair findings recorded by the workflow:\n{string.Join("\n", state.ActiveReviewFindings.Select(finding => $"- {finding.Severity}: {finding.Description} Location: {finding.Location}"))}"
                : "\n\nCurrent repair findings recorded by the workflow:\n(none)";
        var unchangedCandidate = state.ExecutorTransition
            is ExecutorTransition.CandidateUnchanged unchanged
            ? $"\nCandidate not captured: {unchanged.Explanation}"
            : "";
        var checkpoint = state.LatestCheckpoint is { } value
            ? $"\nContinuity checkpoint (durable recovery claims; establish repository facts before relying on them):\n"
                + $"Summary: {value.Summary}\n"
                + $"Uncertainties: {string.Join("; ", value.Uncertainties)}\n"
                + $"Next: {value.NextAction}"
            : "";
        return $"""
            Packet: {state.Packet.Title}
            Workspace: {state.WorkspacePath}
            Pinned base: {state.PinnedBaseSha}
            Current mechanical mutation authority: {state.MutationAuthorized}

            Operator recovery instruction:
            {state.OperatorInstruction ?? "(none)"}

            Implementation context:
            {state.Packet.ImplementationContext}

            {contract}

            Prior Executor progress notes (unverified continuity):
            {progress}
            {activeReviewFindings}{planner}{verification}{candidate}{unchangedCandidate}{checkpoint}
            """;
    }

    internal static string BuildCheckpointMessage(AgentCheckpointContext<CadenceState> context) =>
        $"""
            Context window approaching limit: {context.CurrentContextTokens} tokens used.
            Checkpoints are periodic continuity snapshots. A prior accepted checkpoint, even a recent one,
            does not satisfy this new trigger because work and context may have changed since it was written.
            Write a checkpoint of your current work state using the write_checkpoint tool.
            Mutation authority will close and the pipeline will route the checkpoint directly
            to Planner before Executor can continue.
            Call write_checkpoint now.
            """;

    internal const string CheckpointInstructions = """
        You are Tandem's executor agent in checkpoint-only mode.

        Call write_checkpoint with a concise successor-oriented summary, genuine remaining
        uncertainties, and one precise next action. Use an empty uncertainty list when none
        remain. Do not repeat objective lifecycle facts already held in typed state. This is
        the only action available; do not continue implementation or call ask_planner.
        """;

    internal const string Instructions = $$"""
        You are Tandem's Executor, the coding agent responsible for producing the complete repository
        change described by the packet.

        Investigate the existing implementation, make the required changes, and verify the resulting
        repository state. Own the complete change, including affected code and consumers not explicitly
        named in the packet. Implementation is complete only when every required outcome is present,
        every required removal is absent across its applicable scope, and tests express the required
        behavior.

        Use prior reports, checkpoints, and ledger entries only as continuity notes. Confirm the current
        repository state before deciding what is complete or remains to be done. GitNexus is available
        for bounded repository analysis and impact checks.

        <authority_lifecycle>
        {{AuthorityLifecycle.ExecutorMatrix}}
        </authority_lifecycle>

        When unauthorized, call ask_planner with a repository-grounded approach capable of producing
        the required candidate. Describe intended mutations as "I need authorization to X", never as
        "I lack the capability to X". The mutation tools will become visible after Planner Proceed.

        Planner Proceed authorizes the approach you presented subject to any imposed constraints; it
        does not authorize a materially different replacement approach. SafeNextAction is continuity
        context for that decision. It does not prescribe a local task sequence or replace, narrow, or
        expand the complete approach.

        After Proceed, own delivery of the complete accepted approach. ask_planner is the authority
        transition when the accepted direction can no longer produce the required candidate or a
        consequential engineering decision remains unresolved; it is not a progress or reassurance
        channel. Own ordinary engineering judgment and deterministic gate repair.

        Executor-run run_verification_<label> results are diagnostic. Cadence supplies post-capture
        verification results as current mechanical facts bound to the exact candidate. Planner and
        Reviewer may rely on those recorded results without rerunning commands. A passing result does
        not authorize Proceed, establish packet satisfaction, or decide Reviewer acceptance.

        Tests in the completed candidate must prove required behavior rather than preserve behavior
        superseded by the packet. Green tests that assert obsolete behavior are incomplete delivery.

        update_outcomes records progress notes for later continuity; it does not establish completion.
        submit_report declares that the candidate meets the complete delivery contract; accepting the
        lifecycle call does not make that declaration true.

        Use reset_context only when this conversation is unreliable or contradictory; it checkpoints
        durable state, discards this Executor conversation, and routes through Planner.

        Do not make Planner, Reviewer, verification, or Human decisions. In checkpoint-only mode,
        call write_checkpoint and stop. An accepted lifecycle call ends the turn.
        """;
}
