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
            var requiredRepositoryTools = new[] { ChangedFilesToolName, "git_diff" };
            var missing = requiredRepositoryTools
                .Where(required => observation.Tools.All(tool => tool.Name != required))
                .ToArray();
            var missingCommands = RequiredVerificationTools(observation.Context.State)
                .Where(required =>
                    observation.Tools.All(tool => tool.Name != required)
                    && !HasReportedRedRerun(observation.Output, observation.Context.State, required)
                )
                .ToArray();
            return missing.Length == 0 && missingCommands.Length == 0
                ? []
                :
                [
                    new StructuredOutputProblem(
                        "$grounding",
                        "Accept and RequestChanges require successful git_changed_files and git_diff observations. Each generated run_verification_N must either succeed, or RequestChanges must report its exact nonzero result as blocker evidence. "
                            + $"Missing required observations: {string.Join(", ", missing.Concat(missingCommands))}. "
                            + $"Observed: {string.Join(", ", observation.Tools.Select(tool => tool.Name))}. "
                            + "Use the exact candidate range, complete pagination and path inspection, then return only the corrected doctrine-bound JSON decision."
                    ),
                ];
        };

    private static IEnumerable<string> RequiredVerificationTools(CadenceState state)
    {
        for (var index = 1; index <= state.Packet.Verification.Count; index++)
        {
            yield return $"run_verification_{index}";
        }
    }

    private static bool HasReportedRedRerun(
        ReviewDecision decision,
        CadenceState state,
        string toolName
    )
    {
        if (decision.Decision != ReviewDecisionValue.RequestChanges)
        {
            return false;
        }
        var index = int.Parse(toolName["run_verification_".Length..]) - 1;
        var command = state.Packet.Verification[index];
        return decision.Findings.Any(finding =>
            finding.Severity is ReviewFindingSeverity.Critical or ReviewFindingSeverity.High
            && finding.Evidence.Any(evidence =>
                evidence.Kind == ReviewEvidenceKind.VerificationCommand
                && string.Equals(evidence.Command, command, StringComparison.Ordinal)
                && evidence.ExitCode is not (null or 0)
            )
        );
    }
}
