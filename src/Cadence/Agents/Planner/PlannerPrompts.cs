using System.Text.Json;

namespace Cadence;

public static class PlannerPrompts
{
    public static string BuildMessage(CadenceState state)
    {
        var packet = state.Packet;
        var outcomes = string.Join(
            "\n",
            packet.Outcomes.Select(o => $"- [{o.Id}] {o.Description}")
        );
        var constraints =
            packet.Constraints.Count > 0
                ? string.Join("\n", packet.Constraints.Select(c => $"- {c}"))
                : "(none)";
        var request = state.ExecutorTransition is ExecutorTransition.PlannerRequested fact
            ? $"Question type: {fact.Request.QuestionType}\n"
                + $"Current slice: {fact.Request.CurrentSlice}\n"
                + $"Question: {fact.Request.Question}\n"
                + $"Proposed approach: {fact.Request.ProposedApproach}\n"
                + $"Evidence:\n{string.Join("\n", fact.Request.Evidence.Select(item => $"- {item}"))}\n"
                + RenderFailedInstruction(fact.Request.FailedInstruction)
            : "(no request provided)";
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
                safeNextAction = "Implement the approved approach through the inspected seam.",
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

            Outcomes:
            {outcomes}

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
        - Stop only when you cannot state a safe engineering next action after inspecting
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
        and one concrete SafeNextAction. Reorient preserves accepted constraints, authorizes the
        corrected approach for the current revision, and routes a fresh Executor that may continue
        without repeating the same approval cycle. For every other question type Reorient is
        invalid. Fail closed rather than using Reorient as a general retry.

        A Planner consultation is not evidence that prior obligations are closed. Active accepted
        constraints remain open until repository evidence proves closure. Only Proceed and
        ProceedWithConstraints replace them; Reorient authorizes its corrected approach while
        preserving them, and other non-authorizing decisions preserve them.

        If a prior Planner instruction failed, treat the failure as contradictory evidence. Address
        the failing command and observed result directly. Do not repeat the instruction without
        explaining why the prior attempt did not test it.

        State one concrete SafeNextAction for every response. Escalate only decisions genuinely
        owned by the Human. Stop only when no safe engineering next action can be stated after
        inspection; missing command output is not enough to stop.

        Include a direct rationale and the evidence you actually used. Proceed means no
        implementation obligations remain and Constraints must be empty.
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
