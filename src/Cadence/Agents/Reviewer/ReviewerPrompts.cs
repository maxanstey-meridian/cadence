using System.Text.Json;

namespace Cadence;

public static class ReviewerPrompts
{
    public static string BuildMessage(CadenceState state)
    {
        var outcomes = string.Join(
            "\n",
            state.Packet.Outcomes.Select(o => $"- [{o.Id}] {o.Description}")
        );
        var acceptance = string.Join(
            "\n",
            state.Packet.Acceptance.Select(criterion =>
                $"- [{criterion.Id}] outcome={criterion.OutcomeId}: {criterion.Requirement}"
            )
        );
        var packetConstraints =
            state.Packet.Constraints.Count > 0
                ? string.Join("\n", state.Packet.Constraints.Select(c => $"- {c}"))
                : "(none)";
        var plannerConstraints =
            state.PlannerConstraints.Count > 0
                ? string.Join("\n", state.PlannerConstraints.Select(c => $"- {c}"))
                : "(none)";
        var verification =
            state.VerificationResults.Count > 0
                ? VerificationResultFormatting.Format(state.VerificationResults)
                : "(no verification commands)";
        var example = JsonSerializer.Serialize(
            new
            {
                decision = "Accept",
                summary = "The candidate satisfies the delivery contract.",
                findings = Array.Empty<ReviewFinding>(),
                humanQuestion = (string?)null,
                humanDecisionDomain = (string?)null,
            },
            new JsonSerializerOptions(TandemJson.CreateTypedContract()) { WriteIndented = true }
        );
        return $"""
            Packet: {state.Packet.Title}
            Workspace: {state.WorkspacePath}
            Pinned base: {state.PinnedBaseSha}
            Candidate SHA: {state.CandidateSha ?? "(no candidate)"}

            Implementation context:
            {state.Packet.ImplementationContext}

            Outcomes:
            {outcomes}

            Acceptance criteria:
            {acceptance}

            Packet constraints:
            {packetConstraints}

            Planner constraints:
            {plannerConstraints}

            Verification results:
            {verification}

            Prior authoritative Reviewer findings for this repair round:
            {FormatActiveFindings(state.ActiveReviewFindings)}

            Implementation report claims (non-authoritative; independently inspect the candidate):
            {FormatReport(state.ExecutorTransition)}

            Human answer for this review:
            {(
                state.ReviewerHumanAnswer is ReviewerHumanAnswer.HumanDecision answer
                    ? answer.Text
                    : "(none)"
            )}

            Consider every outcome, acceptance criterion, and constraint. Every finding must describe
            a concrete defect and give its precise repository location. Return one value shaped like:
            {example}
            """;
    }

    public static string BuildInstructions(ReviewerDoctrine doctrine) =>
        $$"""
            You are Tandem's Reviewer. Independently inspect the exact pinned-base-to-candidate
            changes and relevant integration seams. Do not trust Executor or Planner claims and do
            not implement repairs. Apply the operator-authored doctrine in its listed order:

            <reviewer_doctrine>
            {{string.Join("\n", doctrine.Clauses.Select(clause => $"[{clause.Id}] {clause.Text}"))}}
            </reviewer_doctrine>

            Review the delivered behavior and its ownership, not merely requested shape or green tests.
            Reject parallel replacement paths, speculative compatibility, provenance theatre, unearned
            abstractions, and hardening without an explicit requirement at a real boundary. Do not
            overcorrect by removing correctness boundaries the packet still requires.

            Accept only when the candidate satisfies every outcome, acceptance criterion, and
            constraint with no material finding. RequestChanges requires a concrete Executor-fixable
            Critical or High finding with a precise repository location. NeedsHuman is only for a
            genuinely Human-owned product or policy decision. Deterministic verification is supplied
            by Cadence; review it but do not rerun it. Return exactly one structured decision.
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
