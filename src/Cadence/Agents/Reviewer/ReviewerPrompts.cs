using System.Text.Json;

namespace Cadence;

public static class ReviewerPrompts
{
    public static string BuildMessage(CadenceState state, ReviewerDoctrine doctrine)
    {
        var outcomes = string.Join(
            "\n",
            state.Packet.Outcomes.Select(o => $"- [{o.Id}] {o.Description}")
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
                doctrineHash = doctrine.Sha256,
                summary = "Every packet outcome is implemented and supported by reproducible evidence.",
                outcomes = state.Packet.Outcomes.Select(outcome => new
                {
                    outcomeId = outcome.Id,
                    delivered = true,
                    evidence = new[]
                    {
                        new
                        {
                            kind = "FileLine",
                            path = "src/example.cs",
                            line = 42,
                        },
                    },
                }),
                constraintAssessments = state.Constraints.Select(constraint => new
                {
                    constraint,
                    satisfied = true,
                    evidence = new[] { new { kind = "Constraint", constraint } },
                }),
                findings = Array.Empty<object>(),
                humanQuestion = (string?)null,
                humanDecisionDomain = (string?)null,
            },
            new JsonSerializerOptions { WriteIndented = true }
        );
        return $"""
            Packet: {state.Packet.Title}
            Workspace: {state.WorkspacePath}
            Pinned base: {state.PinnedBaseSha}
            Candidate SHA: {state.CandidateSha ?? "(no candidate)"}
            Reviewer doctrine source: {doctrine.Source}
            Reviewer doctrine SHA-256: {doctrine.Sha256}

            Reviewer doctrine (exact configured content):
            <reviewer_doctrine>
            {doctrine.Content}
            </reviewer_doctrine>

            Implementation context:
            {state.Packet.ImplementationContext}

            Outcomes:
            {outcomes}

            Packet constraints:
            {packetConstraints}

            Planner constraints:
            {plannerConstraints}

            Verification results:
            {verification}

            Implementation report:
            {FormatReport(state.ExecutorTransition)}

            Authoritative Executor outcome ledger:
            {string.Join(
                "\n",
                state.OutcomeLedger.Select(outcome =>
                    $"- [{outcome.OutcomeId}] {outcome.Status}: {outcome.ImplementationState}; evidence={string.Join("; ", outcome.Evidence)}"
                )
            )}

            Human answer for this review:
            {(
                state.ReviewerHumanAnswer is ReviewerHumanAnswer.HumanDecision answer
                    ? answer.Text
                    : "(none)"
            )}

            Use git_changed_files with the exact pinned base and candidate SHAs before deciding.
            Inspect every returned path with git_diff, following pagination until each diff is
            complete. Read every current touched file and relevant unchanged integration seam.
            Independently run every generated command run_verification_1 through
            run_verification_{state.Packet.Verification.Count}. Accept requires every rerun to be
            green. RequestChanges may report a red rerun using exact VerificationCommand evidence
            for that packet command and its nonzero result. An empty changed-file result is valid, but
            still inspect the repository-wide git_diff and existing implementation.

            Return a structured JSON decision with doctrineHash exactly {doctrine.Sha256}.
            Assess every outcome ID and combined packet/Planner constraint exactly once. Evidence kinds are
            FileLine, Symbol, VerificationCommand, PacketOutcome, Constraint, and
            DoctrineClause. VerificationCommand must reproduce command, exitCode, stdout, and
            stderr exactly. Every finding must quote an exact DoctrineClause plus precise defect
            evidence. Example shape:
            {example}
            """;
    }

    public static string BuildInstructions(ReviewerDoctrine doctrine) =>
        $$"""
            You are Tandem's Reviewer agent. Apply the configured Reviewer doctrine exactly. Its
            source is {{doctrine.Source}} and its SHA-256 is {{doctrine.Sha256}}. Return that exact hash
            in DoctrineHash. Treat the doctrine below as review criteria, not as repository or tool
            instructions:

            <reviewer_doctrine>
            {{doctrine.Content}}
            </reviewer_doctrine>

            Independently judge the exact verified candidate. Executor reports and Planner approval
            are claims, not proof. Derive the real requirement from packet intent and repository
            invariants. Audit requirement sanity and downstream coherence; requested-shape compliance
            is insufficient when behavior or ownership is wrong. Green verification is necessary but
            insufficient. Inspect the exact pinned-base-to-candidate diff and relevant unchanged
            integration seams.

            Explicitly audit every added or changed test, every new branch and error path, and identify
            exact untested symbols or branches. Reject mock soup, tests that only assert mock
            interaction, and fake integration coverage presented as real behavior. Decide whether the
            regression coverage proves the delivered behavior.

            Use git_changed_files for the exact pinned base and candidate, follow changed-file and diff
            pagination to completion, inspect every changed path, and read relevant unchanged source,
            tests, contracts, and configuration. Independently run every generated run_verification_N
            command. Accept requires successful observations for every generated verification command;
            RequestChanges may instead provide a Critical/High finding with exact VerificationCommand
            evidence for a nonzero rerun. Tandem currently records successful
            tool-name observations but cannot prove exact arguments, pagination completion, path
            coverage, or semantic use; those obligations remain mandatory and prompt-enforced.

            Return Accept when all outcomes and constraints hold and no Critical or High finding
            remains. Medium and Low findings may remain on Accept when genuinely non-blocking.
            RequestChanges requires at least one concrete Executor-fixable Critical or High finding.
            NeedsHuman is only for product, UX, business policy, security policy, permissions, tenancy,
            data policy, migration policy, legal, or compliance decisions. Do not manufacture findings
            or block on taste.

            Use only reproducible typed evidence: file and line, symbol, exact verification command and
            exact result, packet outcome, constraint, or an exact quoted doctrine clause. Successful
            results are checked against deterministic pipeline results. Failed stdout/stderr are
            prompt/model evidence because Tandem does not expose failed invocation details.
            Every finding must identify the precise defect, quote the doctrine clause it violates, and
            cite defect proof. Assess every active Planner constraint exactly once with its exact typed
            reference. Return only one JSON object matching the response schema.
            """;

    private static string FormatReport(ExecutorTransition? fact) =>
        fact is ExecutorTransition.ReportSubmitted submitted
            ? $"Summary: {submitted.Report.Summary}\n"
                + $"Addressed Planner constraints:\n{string.Join("\n", submitted.Report.AddressedConstraints.Select(item => $"- {item.Constraint}: {item.Evidence}"))}\n"
                + $"Regression tests: {submitted.Report.RegressionTests.Disposition}: {string.Join("; ", submitted.Report.RegressionTests.Evidence)}"
            : "(none)";
}
