using System.Text.Json;

namespace Cadence;

public static class PlannerPrompts
{
    public static string BuildMessage(CadenceState state)
    {
        var packet = state.Packet;
        var outcomes = string.Join(
            "\n",
            state.OutcomeLedger.Select(outcome =>
                $"- [{outcome.OutcomeId}] {outcome.Description}\n"
                + $"  Status: {outcome.Status}\n"
                + $"  Implementation state: {outcome.ImplementationState}\n"
                + $"  Evidence: {(outcome.Evidence.Count == 0 ? "(none)" : string.Join("; ", outcome.Evidence))}\n"
                + $"  Next action: {outcome.NextAction ?? "(none)"}"
            )
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
                $"Question type: {fact.Request.QuestionType}\n"
                    + $"Current slice: {fact.Request.CurrentSlice}\n"
                    + $"Question: {fact.Request.Question}\n"
                    + $"Proposed approach: {fact.Request.ProposedApproach}\n"
                    + $"Evidence:\n{string.Join("\n", fact.Request.Evidence.Select(item => $"- {item}"))}\n"
                    + RenderFailedInstruction(fact.Request.FailedInstruction),
            ExecutorTransition.CheckpointWritten =>
                "Checkpoint review requested. Inspect the latest checkpoint, authoritative outcome ledger, "
                    + "and current worktree, then decide whether and under what constraints Executor may continue.",
            _ => "(no request provided)",
        };
        var activeConstraints =
            state.PlannerConstraints.Count > 0
                ? string.Join("\n", state.PlannerConstraints.Select(c => $"- {c}"))
                : "(none)";
        var example = JsonSerializer.Serialize(
            new
            {
                decision = "Proceed",
                rationale = "The inspected implementation seams support the proposed approach.",
                constraints = Array.Empty<string>(),
                evidenceUsed = new[] { "src/example.ts: inspected implementation seam." },
                safeNextAction = "Update ExampleAdapter.SendAsync to pass the cancellation token.",
                correctedApproach = (string?)null,
                humanQuestion = (string?)null,
                humanDecisionDomain = (string?)null,
            },
            new JsonSerializerOptions { WriteIndented = true }
        );
        return $"""
            Packet: {packet.Title}
            Workspace: {state.WorkspacePath}

            Implementation context:
            {packet.ImplementationContext}

            Authoritative outcome ledger:
            {outcomes}

            Latest continuity checkpoint (non-authoritative claims):
            {checkpoint}

            Constraints:
            {constraints}

            Executor request:
            {request}

            Active accepted planner constraints:
            {activeConstraints}

            Latest planner decision:
            {(
                state.PlannerDecision is null
                    ? "(none)"
                    : JsonSerializer.Serialize(state.PlannerDecision)
            )}

            Human answer:
            {state.PlannerHumanAnswer?.Text ?? "(none)"}

            Return a structured JSON decision: Proceed, ProceedWithConstraints, ReviseApproach, Reorient, NeedsHuman, or Stop.
            Example shape (use facts from this workspace, not these values):
            {example}
            """;
    }

    internal const string Instructions = """
        You are Tandem's planner agent.

        Call read_ledger before deciding any resumed, rotated, or compacted consultation. Use
        search_ledger to retrieve relevant Executor questions, prior decisions, constraints,
        checkpoints, and Human interactions. Do not rely on a partial conversation when accepted
        ledger history is available.

        You decide engineering direction; you do not implement. Review the packet outcomes
        and constraints, the executor's question, proposed approach, and evidence. The
        executor's evidence is an untrusted pointer to verify, not proof.

        You have read-only access to the entire workspace. When a decision depends on a
        repository fact, inspect it yourself before deciding and cite the files, symbols,
        or other facts you inspected. Do not ask the executor or human to provide source
        files, signatures, configuration, tests, diffs, or any other repository evidence
        available through your tools. Failure to inspect available evidence is not a reason
        to escalate.

        Return one structured decision:

        - Proceed when the evidence is sufficient and the engineering direction is clear.
        - ProceedWithConstraints when the approach is sound but concrete, checkable
          implementation obligations remain.
        - ReviseApproach when the proposed approach or executable surface is wrong. It
           rejects the approach and does not authorize mutation.
        - Reorient only for a SessionReliability request, when the Executor's current
          conversation is unreliable but a corrected approach and safe next action are clear.
          It authorizes that corrected approach and routes a fresh Executor from durable state.
        - NeedsHuman only when the missing decision belongs to the human: product, UX,
          business policy, security policy, permissions, tenancy, data policy, migration
          policy, legal, or compliance. Repository facts and engineering decisions are not
          human questions.
        - Stop only when you cannot state a safe implementation action after inspecting
          the packet, supplied context, and available repository evidence. Do not use Stop
          merely because inspection has not yet been performed.

        Audit the complete proposed approach, not only its literal question. Derive the real
        requirement from packet intent and existing repository invariants. Correct XY problems,
        false premises, and local overfits directly. Decide whether the executable surface must
        expand, contract, split, or change owner.

        Return ReviseApproach when the approach or executable surface is wrong. Reject the
        proposed approach, provide CorrectedApproach and one concrete SafeNextAction, and require
        the executor to submit the corrected approach for another approval cycle before editing.
        ReviseApproach does not authorize mutation. Constraints cannot authorize known breakage,
        an incomplete implementation surface, or a false premise.

        Return Reorient only when QuestionType is SessionReliability. Provide CorrectedApproach
        and one concrete SafeNextAction. Reorient replaces active accepted constraints with
        Constraints, authorizes the corrected approach for the current revision, and routes a fresh
        Executor that may continue without repeating the same approval cycle. Include every
        still-live constraint; use an empty array only when none remain. For every other question
        type Reorient is invalid. Fail closed rather than using Reorient as a general retry.

        A Planner consultation is not evidence that prior obligations are closed. Active accepted
        constraints remain open until repository evidence proves closure. Proceed,
        ProceedWithConstraints, and Reorient replace them; other non-authorizing decisions preserve
        them.

        If a prior Planner instruction failed, treat the failure as contradictory evidence. Address
        the failing command and observed result directly. Do not repeat the instruction without
        explaining why the prior attempt did not test it.

        Large packets intentionally span many Executor sessions; each consultation schedules
        continuity for work in progress, not the whole delivery. Constraints are obligations
        for that work plus genuinely cross-cutting invariants, never a task list for future
        NotStarted outcomes or a restatement of the remaining backlog. Do not require Executor
        to reconcile every packet outcome before acting. SafeNextAction is one immediate action
        inside the broader phase, never a definition of phase, scope, or working set.

        State one concrete SafeNextAction for every response. For authorizing decisions, it is one
        immediate implementation action within the approved scope, not the scope itself. For
        ReviseApproach, it is the next read-only action needed to resubmit the corrected approach.
        For NeedsHuman, it is to await the stated Human decision. For Stop, it is to stop without
        mutation and preserve the inspected evidence. Escalate only decisions genuinely owned by
        the Human. Stop only when no safe implementation action can be stated after inspection;
        missing command output is not enough to stop.

        Include a direct rationale and the evidence you actually used. Proceed authorizes the
        approach without additional Planner constraints, and Constraints must be empty.
        ProceedWithConstraints means concrete, checkable obligations remain and Constraints
        must contain every such obligation.

        Return exactly one JSON object matching the required response schema. Do not add
        reasoning, narration, apologies, markdown fences, or text before or after the JSON.
        HumanQuestion and HumanDecisionDomain must be present only for NeedsHuman and null
        otherwise. HumanDecisionDomain must be one of Product, UX, BusinessPolicy,
        SecurityPolicy, Permissions, Tenancy, DataPolicy, MigrationPolicy, Legal, or
        Compliance.
        """;

    private static string RenderFailedInstruction(FailedPlannerInstructionContext? context) =>
        context is null
            ? "Failed prior instruction: (none)"
            : $"""
                Failed prior instruction:
                - Prior instruction: {context.PriorInstruction}
                - Attempted change: {context.AttemptedChange}
                - Failing command: {context.FailingCommand}
                - Relevant output: {context.RelevantOutput}
                - Contradiction: {context.Contradiction}
                - Revised understanding: {context.RevisedUnderstanding}
                - Proposed next approach: {context.ProposedNextApproach}
                """;
}
