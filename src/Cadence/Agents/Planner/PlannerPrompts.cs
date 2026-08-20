using System.Text.Json;

namespace Cadence;

public static class PlannerPrompts
{
    public static string BuildMessage(CadenceState state)
    {
        var packet = state.Packet;
        var outcomes = string.Join(
            "\n",
            packet.Outcomes.Select(outcome => $"- [{outcome.Id}] {outcome.Description}")
        );
        var checkpoint = state.LatestCheckpoint is { } value
            ? $"Summary: {value.Summary}\n"
                + $"Uncertainties: {string.Join("; ", value.Uncertainties)}\n"
                + $"Next action: {value.NextAction}"
            : "(none)";
        var constraints =
            packet.Constraints.Count > 0
                ? string.Join("\n", packet.Constraints.Select(c => $"- {c}"))
                : "(none)";
        var request = state.ExecutorTransition switch
        {
            ExecutorTransition.PlannerRequested fact =>
                $"Current slice: {fact.Request.CurrentSlice}\n"
                    + $"Question: {fact.Request.Question}\n"
                    + $"Proposed approach: {fact.Request.ProposedApproach}\n"
                    + $"Evidence:\n{string.Join("\n", fact.Request.Evidence.Select(item => $"- {item}"))}",
            ExecutorTransition.CheckpointWritten =>
                "Checkpoint review requested. Inspect the latest checkpoint, packet outcomes, "
                    + "and current worktree, then decide whether and under what constraints Executor may continue.",
            _ => "(no request provided)",
        };
        var activeConstraints =
            state.PlannerConstraints.Count > 0
                ? string.Join("\n", state.PlannerConstraints.Select(c => $"- {c}"))
                : "(none)";
        var verification =
            state.VerificationResults.Count > 0
                ? VerificationResultFormatting.Format(state.VerificationResults)
                : "(no verification results yet)";
        return $"""
            Packet: {packet.Title}
            Workspace: {state.WorkspacePath}

            Implementation context:
            {packet.ImplementationContext}

            Packet outcomes:
            {outcomes}

            Latest continuity checkpoint (non-authoritative claims):
            {checkpoint}

            Constraints:
            {constraints}

            Executor request:
            {request}

            Active accepted planner constraints:
            {activeConstraints}

            Verification results:
            {verification}

            Latest planner decision:
            {(
                state.PlannerDecision is null
                    ? "(none)"
                    : JsonSerializer.Serialize(state.PlannerDecision)
            )}

            Human answer:
            {state.PlannerHumanAnswer?.Text ?? "(none)"}

            Return a structured decision: Proceed, ReviseApproach, NeedsHuman, or Stop.
            """;
    }

    internal const string Instructions = """
        You are Tandem's Planner. Decide engineering direction; do not implement. Inspect material
        repository facts with your read-only tools before deciding rather than trusting supplied
        claims.

        At a checkpoint, assess the cumulative work and delivery trajectory against the whole packet,
        not only the proposed next action. Correct wrong assumptions, regressions, scope drift,
        unnecessary complexity, or lost invariants before deciding what should happen next.

        Return Proceed when the approach is safe, ReviseApproach with a corrected approach when it
        is not, NeedsHuman only for a genuinely Human-owned product or policy decision, or Stop when
        no safe action exists. Proceed may impose concrete constraints. Every decision must include
        a concise rationale, facts actually used, and one immediate SafeNextAction. Return exactly
        one structured decision.
        """;
}
