namespace Cadence;

public static class ReviewerPrompts
{
    public static string BuildMessage(CadenceState state)
    {
        var contract = DeliveryContractRenderer.Render(state);
        var verification =
            state.VerificationResults.Count > 0
                ? VerificationResultFormatting.Format(state.VerificationResults)
                : "(no verification commands)";
        return $"""
            Packet: {state.Packet.Title}
            Workspace: {state.WorkspacePath}
            Current mechanical pinned base: {state.PinnedBaseSha}
            Current mechanical candidate SHA: {state.CandidateSha ?? "(no candidate)"}

            Operator recovery instruction:
            {state.OperatorInstruction ?? "(none)"}

            Implementation context:
            {state.Packet.ImplementationContext}

            {contract}

            Current mechanical verification results bound to the candidate:
            {verification}

            Current repair findings recorded by the workflow:
            {FormatActiveFindings(state.ActiveReviewFindings)}

            Executor handoff notes (unverified):
            {FormatReport(state.ExecutorTransition)}

            Human answer for this review (authoritative only for the requested Human-owned decision):
            {(
                state.ReviewerHumanAnswer is ReviewerHumanAnswer.HumanDecision answer
                    ? answer.Text
                    : "(none)"
            )}

            Required review outcome:
            Determine whether the exact candidate repository state completely satisfies every outcome,
            acceptance criterion, and constraint above and contains no blocking defect within the
            delivery scope. Return one structured decision matching the review contract.
            """;
    }

    public static string BuildInstructions(ReviewerDoctrine doctrine) =>
        $$"""
            You are Tandem's Reviewer, an independent code-review agent responsible for deciding whether
            the exact candidate should be accepted.

            Review the candidate as a production change against the complete packet. Inspect the repository
            state necessary to reach that decision, including relevant unchanged code. Look for concrete
            counterexamples to claimed completion before accepting. Do not implement repairs.

            The repository is the subject of the review. The packet defines the required result, and
            mechanical verification records whether configured commands passed. Implementation reports,
            ledger entries, prior decisions, and participant claims do not establish that the candidate is
            correct or complete.

            For an absence or removal requirement, inspect the candidate scope in which the prohibited
            concept could remain. A diff of selected changed files is not enough.

            Apply the operator-authored doctrine in its listed order:

            <reviewer_doctrine>
            {{string.Join("\n", doctrine.Clauses.Select(clause => $"[{clause.Id}] {clause.Text}"))}}
            </reviewer_doctrine>

            Unwarranted machinery is a defect only when the packet, active constraints, repository
            invariants, or a concrete implementation boundary does not justify it and it causes a
            correctness, contract, ownership, dependency, or maintenance problem. Architectural preference
            alone is not a blocking finding, and required correctness boundaries must remain intact.

            Accept means every obligation is established satisfied and no Critical or High finding
            remains. RequestChanges means at least one concrete Executor-fixable Critical or High defect
            prevents the candidate from satisfying the delivery contract. NeedsHuman means completion
            depends on a genuinely Human-owned product or policy decision. Every non-Human decision must
            assess every obligation exactly once by ID and record each established defect at a precise
            repository location.

            Accept requires complete green post-capture verification bound to the exact candidate. A Human
            answer resolves only its requested Human-owned decision and does not waive unrelated obligations.
            Return exactly one structured decision.
            """;

    private static string FormatActiveFindings(IReadOnlyList<ReviewFinding> findings) =>
        findings.Count == 0
            ? "(none)"
            : string.Join(
                "\n",
                findings.Select(finding =>
                    $"- {finding.Severity}: {finding.Description} Location: {finding.Location}"
                )
            );

    private static string FormatReport(ExecutorTransition? fact) =>
        fact is ExecutorTransition.ReportSubmitted submitted
            ? $"Summary: {submitted.Report.Summary}"
            : "(none)";
}
