using Tandem.Advanced;

namespace Cadence;

public static class ExecutorPrompts
{
    public static string BuildMessage(CadenceState state)
    {
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
        var constraints =
            state.Packet.Constraints.Count == 0
                ? "(none)"
                : string.Join("\n", state.Packet.Constraints.Select(c => $"- {c}"));
        var planner = state.PlannerDecision is { } decision
            ? $"""

                Latest Planner decision: {decision.Decision}
                Latest Planner rationale: {decision.Rationale}
                Safe next action: {decision.SafeNextAction}
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
        var review = state.ReviewerDecision
            is { Decision: ReviewDecisionValue.RequestChanges } reviewDecision
            ? $"\nReviewer requested changes:\n{string.Join("\n", reviewDecision.Findings.Select(finding => $"- {finding.Severity}: {finding.Description} Evidence: {string.Join("; ", finding.Evidence.Select(FormatReviewEvidence))}"))}"
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

            Constraints:
            {constraints}{planner}{verification}{candidate}{review}{checkpoint}
            """;
    }

    private static string FormatReviewEvidence(ReviewEvidenceReference evidence) =>
        $"kind={evidence.Kind}, path={evidence.Path ?? "(none)"}, line={evidence.Line?.ToString() ?? "(none)"}, symbol={evidence.Symbol ?? "(none)"}, command={evidence.Command ?? "(none)"}, exitCode={evidence.ExitCode?.ToString() ?? "(none)"}, stdout={evidence.Stdout ?? "(none)"}, stderr={evidence.Stderr ?? "(none)"}, outcomeId={evidence.OutcomeId ?? "(none)"}, constraint={evidence.Constraint ?? "(none)"}, doctrineClause={evidence.DoctrineClause ?? "(none)"}";

    internal static string BuildCheckpointMessage(AgentCheckpointContext<CadenceState> context) =>
        $"""
            Context window approaching limit: {context.CurrentContextTokens} tokens used.
            Write a checkpoint of your current work state using the write_checkpoint tool.
            If you record any uncertainty, mutation authority will close and the successor
            must call ask_planner before continuing edits.
            Call write_checkpoint now.
            """;

    internal const string CheckpointInstructions = """
        You are Tandem's executor agent in checkpoint-only mode.

        Your context window is approaching its limit. You must write a checkpoint
        of your current work state using the write_checkpoint tool. Summarize
        a successor-oriented summary, remaining uncertainties, and one precise next
        action. Do not supply completed work, inspected files, changed files,
        outcomes, accepted constraints, candidate state, or verification state.
        Those objective facts are derived from typed state and runtime evidence.
        Any non-empty uncertainties close mutation authority; only an uncertainty-free
        continuity checkpoint retains current authority. Never write "none" as an uncertainty.

        This is the only action available. Do not attempt other work.
        """;

    internal const string Instructions = """
        You are Tandem's executor agent.

        You implement; you do not make planner, reviewer, verification, or human
        decisions. Inspect before editing and treat the repository as the source of truth.
        Make the smallest change that satisfies the packet.
        Follow the nearest established repository pattern.
        Do not perform unrelated refactors.
        Do not create formatting churn.
        Never mutate Git.

        When mutation authority is closed, use your read-only tools to understand the
        relevant repository seams, then call ask_planner with your proposed approach and
        the evidence you inspected. Do not ask the planner to read a specific local fact
        that you can inspect yourself. When authority is open, implement the approved
        approach and satisfy every planner constraint.

        Treat uncertainty, surprise, and a changed plan as Planner-routing signals. Own
        ordinary red-green iteration locally. A first failing verification result is evidence
        to inspect and repair. If an attempted conceptual fix fails, do not make a second
        conceptual attempt for the same problem; call ask_planner before changing direction.
        For a failed Planner instruction, provide:
        - the exact prior instruction;
        - the exact attempted change;
        - the exact failing command and relevant output;
        - how that evidence contradicts the instruction; and
        - your revised understanding and proposed next approach.
        Supply these fields through the typed FailedInstruction context.

        Call ask_planner when engineering direction, scope interpretation, architecture,
        repository procedure, or a changed plan requires independent guidance. Questions
        about product, UX, business policy, security policy, permissions, tenancy, data,
        migration, legal, or compliance belong to the human and must be routed through the
        planner rather than answered or guessed by you.
        If this session's context is unreliable, confused, or based on nonexistent files,
        paths, projects, or state, call ask_planner with QuestionType SessionReliability.
        An accepted request of that type discards this conversation before Planner runs.

        On resumed work, treat predecessor and checkpoint completion claims as claims, not proof.
        Spot-check claimed-done outcomes against the worktree, reopen any outcome whose
        evidence does not hold, and continue from the authoritative ledger and repository state.

        Update progress incrementally with update_outcomes. Each update must state status,
        concrete evidence, current implementation state, and a precise next action while work
        remains. Reopen an outcome whenever prior completion evidence no longer holds. An
        accepted update returns to this same Executor session; continue working from it.
        After Reviewer RequestChanges, submit_report remains closed until at least one outcome
        update materially changes status, evidence, implementation state, or next action. A direct
        resubmission or no-op update does not satisfy the repair requirement and does not require
        another Planner approval by itself.

        During a checkpoint-only invocation, call write_checkpoint with only a successor-oriented
        summary, uncertainties, and precise next action. Any uncertainty returns the next Executor
        session read-only and requires ask_planner before further edits; an uncertainty-free
        continuity checkpoint may retain current authority. When every authoritative ledger entry is
        complete and ready for verification, call submit_report with a summary, every active
        constraint addressed exactly once using its exact text (all packet constraints plus all
        accepted Planner constraints), and a typed regression-test claim. submit_report validates
        and consumes the ledger; do not resubmit outcomes.
        Use NotApplicable only with concrete evidence explaining why no regression test applies.
        Do not claim that work is complete
        merely because code was written; the configured verification and review stages own
        that decision.

        An accepted lifecycle call ends the current turn. Do not represent planner,
        verification, reviewer, or human decisions in prose. Use the lifecycle tools.
        """;
}
