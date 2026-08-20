using Tandem.Advanced;

namespace Cadence;

public static class ExecutorPrompts
{
    public static string BuildMessage(CadenceState state)
    {
        var outcomes = string.Join(
            "\n",
            state.Packet.Outcomes.Select(outcome => $"- [{outcome.Id}] {outcome.Description}")
        );
        var acceptance = string.Join(
            "\n",
            state.Packet.Acceptance.Select(criterion =>
                $"- [{criterion.Id}] outcome={criterion.OutcomeId}: {criterion.Requirement}"
            )
        );
        var constraints =
            state.Packet.Constraints.Count == 0
                ? "(none)"
                : string.Join("\n", state.Packet.Constraints.Select(c => $"- {c}"));
        var planner = state.PlannerDecision is { } decision
            ? $"""

                Latest Planner decision: {decision.Decision}
                Latest Planner rationale: {decision.Rationale}
                Safe next action (one immediate action, not a scope or working set): {decision.SafeNextAction}
                Corrected approach: {decision.CorrectedApproach ?? "(none)"}
                Active accepted Planner constraints:
                {string.Join(
                    "\n",
                    state.PlannerConstraints.Count > 0
                        ? state.PlannerConstraints.Select(c => $"- {c}")
                        : ["(none)"]
                )}
                """
            : "";
        var verification =
            state.VerificationResults.Count > 0
                ? $"\nLatest verification failure (if any):\n{VerificationResultFormatting.Format(state.VerificationResults)}"
                : "";
        var candidate = state.CandidateSha is { } sha ? $"\nCurrent candidate SHA: {sha}" : "";
        var activeReviewFindings =
            state.ActiveReviewFindings.Count > 0
                ? $"\n\nActive Reviewer findings requiring repair:\n{string.Join("\n", state.ActiveReviewFindings.Select(finding => $"- {finding.Severity}: {finding.Description} Location: {finding.Location}"))}"
                : "\n\nAuthoritative active Reviewer findings:\n(none; submit an empty reviewRepairClaims array)";
        var unchangedCandidate = state.ExecutorTransition
            is ExecutorTransition.CandidateUnchanged unchanged
            ? $"\nCandidate not captured: {unchanged.Explanation}"
            : "";
        var checkpoint = state.LatestCheckpoint is { } value
            ? $"\nNon-authoritative continuity checkpoint (claims only; verify before relying on them):\n"
                + $"Summary: {value.Summary}\n"
                + $"Uncertainties: {string.Join("; ", value.Uncertainties)}\n"
                + $"Next: {value.NextAction}"
            : "";
        return $"""
            Packet: {state.Packet.Title}
            Workspace: {state.WorkspacePath}
            Pinned base: {state.PinnedBaseSha}
            Mutation authorized: {state.MutationAuthorized}

            Implementation context:
            {state.Packet.ImplementationContext}

            Authoritative objective ledger:
            {outcomes}

            Authoritative acceptance criteria (every criterion must be addressed exactly once with concrete evidence in submit_report):
            {acceptance}

            Constraints:
            {constraints}{activeReviewFindings}{planner}{verification}{candidate}{unchangedCandidate}{checkpoint}
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

    internal const string Instructions = """
        You are Tandem's Executor. Implement the packet in the supplied workspace when mutation
        authority is open. When it is closed, inspect enough to propose the next concrete approach
        and call ask_planner. Treat the current authority value and available tools as authoritative.

        Own ordinary engineering judgment and deterministic gate repair. Consult Planner when
        consequential direction remains unclear after bounded investigation, genuine blockage
        remains, or two attempts at the same problem have failed and the approach needs correction;
        do not thrash or seek reassurance for routine decisions.

        Do not make Planner, Reviewer, verification, or Human decisions. In checkpoint-only mode,
        call write_checkpoint and stop. When implementation is complete, call submit_report with
        the required claims. An accepted lifecycle call ends the turn.
        """;
}
