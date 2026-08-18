using System.Text.Json;
using Tandem.Advanced;

namespace Cadence;

public static class ReviewerPolicies
{
    internal const string ChangedFilesToolName = "git_changed_files";

    public static AgentConversationDecision DiscardAfterDecision(
        AgentMessageContext<CadenceState> _,
        AgentMessageOutcome __
    ) => new(AgentConversationRetention.Discard);

    public static OutputAcceptancePolicy<CadenceState, ReviewDecision> RepositoryGrounded() =>
        observation =>
        {
            if (observation.Output.Decision == ReviewDecisionValue.NeedsHuman)
            {
                return [];
            }
            var state = observation.Context.State;
            var invocations = observation.ToolInvocations;
            var manifestIndex = FindManifest(invocations, state);
            var diffIndex = FindRepositoryDiff(invocations, state, manifestIndex);
            var evidenceProblem = ValidateVerificationEvidence(
                observation.Output,
                state,
                invocations
            );
            var verificationProblem = ValidateVerification(
                observation.Output,
                state,
                invocations,
                Math.Max(manifestIndex, diffIndex)
            );
            return
                manifestIndex >= 0
                && diffIndex >= 0
                && verificationProblem is null
                && evidenceProblem is null
                ? []
                :
                [
                    new StructuredOutputProblem(
                        "$grounding",
                        "Accept and RequestChanges require the latest git_changed_files invocation to be a completed exact-range manifest and the latest git_diff invocation to be a later completed repository-wide exact-range diff. Verification attempts must follow Git grounding in packet order; Accept requires each latest attempt to complete with exit code 0 without timeout or truncation, while RequestChanges may stop at the first complete runtime failure only when a Critical/High finding exactly reproduces its process evidence. Every VerificationCommand evidence reference must exactly match deterministic verification or a complete corresponding runtime rerun. "
                            + (
                                evidenceProblem
                                ?? verificationProblem
                                ?? "Git grounding is missing or has incorrect arguments/order. "
                            )
                            + "Pagination, changed-path inspection, and semantic use remain required Reviewer obligations. Return only the corrected doctrine-bound JSON decision."
                    ),
                ];
        };

    private static int FindManifest(
        IReadOnlyList<ToolInvocationObservation> invocations,
        CadenceState state
    ) =>
        FindLatestInvocation(invocations, ChangedFilesToolName) is var index
        && index >= 0
        && IsQualifyingManifest(invocations[index], state)
            ? index
            : -1;

    private static int FindRepositoryDiff(
        IReadOnlyList<ToolInvocationObservation> invocations,
        CadenceState state,
        int manifestIndex
    )
    {
        if (manifestIndex < 0)
        {
            return -1;
        }
        for (var index = invocations.Count - 1; index > manifestIndex; index--)
        {
            if (IsQualifyingRepositoryDiff(invocations[index], state))
            {
                return index;
            }
        }
        return -1;
    }

    private static bool IsQualifyingManifest(
        ToolInvocationObservation invocation,
        CadenceState state
    ) =>
        IsCompletedRead(invocation)
        && HasExactRange(invocation.Arguments, state)
        && IsFirstPage(invocation.Arguments);

    private static bool IsQualifyingRepositoryDiff(
        ToolInvocationObservation invocation,
        CadenceState state
    ) =>
        IsCompletedRead(invocation)
        && HasExactRange(invocation.Arguments, state)
        && IsFirstPage(invocation.Arguments)
        && IsOmittedOrNull(invocation.Arguments, "path");

    private static string? ValidateVerificationEvidence(
        ReviewDecision decision,
        CadenceState state,
        IReadOnlyList<ToolInvocationObservation> invocations
    )
    {
        foreach (var evidence in EnumerateEvidence(decision))
        {
            if (evidence.Kind != ReviewEvidenceKind.VerificationCommand)
            {
                continue;
            }
            var commandIndexes = FindCommandIndexes(state.Packet.Verification, evidence.Command)
                .ToArray();
            if (
                commandIndexes.Length == 0
                || !commandIndexes.Any(commandIndex =>
                    MatchesDeterministicVerification(evidence, state, commandIndex)
                    || MatchesRuntimeVerification(evidence, invocations, commandIndex)
                )
            )
            {
                return $"VerificationCommand evidence for '{evidence.Command}' is not authenticated by complete execution evidence. ";
            }
        }
        return null;
    }

    private static IEnumerable<int> FindCommandIndexes(
        IReadOnlyList<string> commands,
        string? command
    )
    {
        for (var index = 0; index < commands.Count; index++)
        {
            if (string.Equals(commands[index], command, StringComparison.Ordinal))
            {
                yield return index;
            }
        }
    }

    private static IEnumerable<ReviewEvidenceReference> EnumerateEvidence(
        ReviewDecision decision
    ) =>
        decision
            .Outcomes.SelectMany(outcome => outcome.Evidence)
            .Concat(decision.AcceptanceAssessments.SelectMany(assessment => assessment.Evidence))
            .Concat(decision.ConstraintAssessments.SelectMany(assessment => assessment.Evidence))
            .Concat(decision.Findings.SelectMany(finding => finding.Evidence));

    private static bool MatchesDeterministicVerification(
        ReviewEvidenceReference evidence,
        CadenceState state,
        int commandIndex
    ) =>
        state.VerificationResults.Any(result =>
            result.Index == commandIndex
            && string.Equals(result.Command, evidence.Command, StringComparison.Ordinal)
            && result.ExitCode == evidence.ExitCode
            && string.Equals(result.Stdout, evidence.Stdout, StringComparison.Ordinal)
            && string.Equals(result.Stderr, evidence.Stderr, StringComparison.Ordinal)
        );

    private static bool MatchesRuntimeVerification(
        ReviewEvidenceReference evidence,
        IReadOnlyList<ToolInvocationObservation> invocations,
        int commandIndex
    )
    {
        var invocationIndex = FindLatestInvocation(
            invocations,
            $"run_verification_{commandIndex + 1}"
        );
        return invocationIndex >= 0
            && invocations[invocationIndex] is var invocation
            && invocation.Status is ToolInvocationStatus.Completed or ToolInvocationStatus.Failed
            && invocation.Result is ToolResultEvidence.Process process
            && !process.TimedOut
            && !process.Truncated
            && process.ExitCode == evidence.ExitCode
            && string.Equals(process.Stdout, evidence.Stdout, StringComparison.Ordinal)
            && string.Equals(process.Stderr, evidence.Stderr, StringComparison.Ordinal);
    }

    private static string? ValidateVerification(
        ReviewDecision decision,
        CadenceState state,
        IReadOnlyList<ToolInvocationObservation> invocations,
        int groundingIndex
    )
    {
        if (groundingIndex < 0)
        {
            return "Verification cannot qualify before Git grounding. ";
        }

        var latest = state
            .Packet.Verification.Select(
                (_, index) =>
                    invocations
                        .Select((invocation, invocationIndex) => (invocation, invocationIndex))
                        .LastOrDefault(item =>
                            item.invocation.Name == $"run_verification_{index + 1}"
                        )
            )
            .ToArray();
        var previousIndex = groundingIndex;
        for (var commandIndex = 0; commandIndex < latest.Length; commandIndex++)
        {
            var (invocation, invocationIndex) = latest[commandIndex];
            if (invocation is null || invocationIndex <= previousIndex)
            {
                return $"Missing packet-ordered latest attempt for run_verification_{commandIndex + 1}. ";
            }
            previousIndex = invocationIndex;
            if (IsSuccessfulProcess(invocation))
            {
                continue;
            }
            if (
                decision.Decision == ReviewDecisionValue.RequestChanges
                && invocation.Status == ToolInvocationStatus.Failed
                && invocation.Result
                    is ToolResultEvidence.Process { TimedOut: false, Truncated: false } process
                && HasMatchingRedFinding(decision, state.Packet.Verification[commandIndex], process)
            )
            {
                return null;
            }
            return $"run_verification_{commandIndex + 1} has no qualifying current runtime result. ";
        }
        return null;
    }

    private static bool HasMatchingRedFinding(
        ReviewDecision decision,
        string command,
        ToolResultEvidence.Process process
    ) =>
        decision.Findings.Any(finding =>
            finding.Severity is ReviewFindingSeverity.Critical or ReviewFindingSeverity.High
            && finding.Evidence.Any(evidence =>
                evidence.Kind == ReviewEvidenceKind.VerificationCommand
                && string.Equals(evidence.Command, command, StringComparison.Ordinal)
                && evidence.ExitCode == process.ExitCode
                && string.Equals(evidence.Stdout, process.Stdout, StringComparison.Ordinal)
                && string.Equals(evidence.Stderr, process.Stderr, StringComparison.Ordinal)
            )
        );

    private static bool IsSuccessfulProcess(ToolInvocationObservation invocation) =>
        invocation.Status == ToolInvocationStatus.Completed
        && invocation.Result
            is ToolResultEvidence.Process { ExitCode: 0, TimedOut: false, Truncated: false };

    private static bool IsCompletedRead(ToolInvocationObservation invocation) =>
        invocation.Status == ToolInvocationStatus.Completed && invocation.Effect == ToolEffect.Read;

    private static bool HasExactRange(JsonElement arguments, CadenceState state) =>
        HasString(arguments, "baseSha", state.PinnedBaseSha)
        && HasString(arguments, "candidateSha", state.CandidateSha);

    private static bool HasString(JsonElement arguments, string name, string? expected) =>
        expected is not null
        && arguments.ValueKind == JsonValueKind.Object
        && arguments.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
        && string.Equals(value.GetString(), expected, StringComparison.Ordinal);

    private static bool IsFirstPage(JsonElement arguments) =>
        arguments.ValueKind == JsonValueKind.Object
        && (
            !arguments.TryGetProperty("startLine", out var value)
            || value.ValueKind == JsonValueKind.Null
            || value.ValueKind == JsonValueKind.Number
                && value.TryGetInt32(out var line)
                && line == 1
        );

    private static bool IsOmittedOrNull(JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object
        && (
            !arguments.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null
        );

    private static int FindLatestInvocation(
        IReadOnlyList<ToolInvocationObservation> invocations,
        string name
    )
    {
        for (var index = invocations.Count - 1; index >= 0; index--)
        {
            if (invocations[index].Name == name)
            {
                return index;
            }
        }
        return -1;
    }
}
