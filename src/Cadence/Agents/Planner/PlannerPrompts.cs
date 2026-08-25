using System.Text.Json;

namespace Cadence;

public static class PlannerPrompts
{
    public static string BuildMessage(CadenceState state)
    {
        var packet = state.Packet;
        var contract = DeliveryContractRenderer.Render(state);
        var checkpoint = state.LatestCheckpoint is { } value
            ? $"Summary: {value.Summary}\n"
                + $"Uncertainties: {string.Join("; ", value.Uncertainties)}\n"
                + $"Next action: {value.NextAction}"
            : "(none)";
        var request = state.ExecutorTransition switch
        {
            ExecutorTransition.PlannerRequested fact =>
                $"Current slice (Executor claim): {fact.Request.CurrentSlice}\n"
                    + $"Question: {fact.Request.Question}\n"
                    + $"Proposed approach: {fact.Request.ProposedApproach}\n"
                    + $"Executor-reported evidence (claims, not established Planner facts):\n{string.Join("\n", fact.Request.Evidence.Select(item => $"- {item}"))}",
            ExecutorTransition.CheckpointWritten =>
                "Checkpoint review requested. Determine whether the current engineering direction "
                    + "remains sufficient for complete packet delivery and whether authorization may continue.",
            _ => "(no request provided)",
        };
        var verification =
            state.VerificationResults.Count > 0
                ? VerificationResultFormatting.Format(state.VerificationResults)
                : "(no verification results yet)";
        return $"""
            Packet: {packet.Title}
            Workspace: {state.WorkspacePath}

            Operator recovery instruction:
            {state.OperatorInstruction ?? "(none)"}

            Implementation context:
            {packet.ImplementationContext}

            {contract}

            Latest continuity checkpoint (unverified):
            {checkpoint}

            Executor request (unverified proposal):
            {request}

            Current recorded verification results:
            {verification}

            Latest accepted Planner decision:
            {(
                state.PlannerDecision is null
                    ? "(none)"
                    : JsonSerializer.Serialize(state.PlannerDecision)
            )}

            Human answer (authoritative only for the requested Human-owned decision):
            {state.PlannerHumanAnswer?.Text ?? "(none)"}

            Return a structured decision: Proceed, ReviseApproach, NeedsHuman, or Stop.
            """;
    }

    internal const string Instructions = """
        You are Tandem's Planner, the engineering agent responsible for deciding whether the Executor's
        proposed direction can produce the complete required repository change.

        Inspect repository facts when needed. Approve the direction only if it accounts for the complete
        change, its affected consumers, repository invariants, and active constraints. Correct it when it
        does not. Evaluate the proposed direction against the packet and current repository. Executor
        claims and ledger entries explain what was intended or attempted; establish material repository
        facts independently before relying on them. Do not implement the change.

        <executor_authority>
        Executor mutation authority is invocation-scoped. While authority is closed, the Executor
        retains read-only repository tools but mutation tools are not visible. An Executor request
        made while unauthorized describes intended mutations that Proceed will enable.

        Do not interpret the absence of mutation tools as a permanent capability gap. Proceed
        authorizes the presented approach subject to any constraints you impose, opens Executor
        mutation authority, and returns control to Executor.
        </executor_authority>

        At a checkpoint, the same outcome applies to the cumulative delivery: authorization remains
        warranted only while the engineering direction can still produce the complete required candidate.

        Treat complexity as unnecessary when an approach introduces abstractions, generalized
        machinery, compatibility paths, state, indirection, dependencies, architectural layers, or
        other implementation machinery without warrant in the packet, active constraints,
        established repository invariants, or a concrete implementation boundary. Identify the
        concrete machinery and the absence of a condition that warrants it.

        Proceed means the proposed direction is sufficient for complete delivery. ReviseApproach means
        a corrected sufficient direction is established. NeedsHuman is only for a genuinely Human-owned
        product or policy decision. Stop means no safe direction can satisfy the delivery contract.
        Proceed may impose concrete constraints. Each new constraint requires a concise stable local ID
        and its requirement. Author the local ID without the `planner-constraint:` prefix; Cadence adds
        that namespace when the constraint becomes part of the delivery contract.

        Every decision must include a concise rationale and the material repository facts actually
        used. SafeNextAction records the immediate lifecycle consequence or continuity context of the
        decision. It does not prescribe a local task sequence, define the implementation scope, or
        substitute for the complete approved or corrected direction.

        A Human answer resolves the explicit Human-owned decision for which it was requested. It does
        not establish unrelated repository facts or replace unrelated packet requirements,
        constraints, or lifecycle decisions. Return exactly one structured decision.
        """;
}
