using Tandem.Advanced;

namespace Cadence;

public static class ReviewerPolicies
{
    public static AgentConversationDecision DiscardAfterDecision(
        AgentMessageContext<CadenceState> _,
        AgentMessageOutcome __
    ) => new(AgentConversationRetention.Discard);

    public static OutputAcceptancePolicy<CadenceState, ReviewDecision> ContractComplete() =>
        observation =>
        {
            var problems = new List<StructuredOutputProblem>();
            if (observation.Tools.All(tool => tool.Evidence != ToolEvidence.RepositoryInspection))
            {
                problems.Add(
                    new StructuredOutputProblem(
                        "$decision",
                        "Your role requires establishing whether the exact candidate completely satisfies the delivery contract. You have not examined any candidate repository evidence in this consultation, so you cannot yet have established that outcome. Examine the repository evidence needed to assess the candidate, then return the decision."
                    )
                );
            }
            if (observation.Output.Decision == ReviewDecisionValue.NeedsHuman)
            {
                return problems;
            }
            var requireSatisfied = observation.Output.Decision == ReviewDecisionValue.Accept;
            AddCoverage(
                problems,
                "$assessments",
                observation.Output.Assessments,
                DeliveryObligations.From(observation.Context.State).Select(x => x.Reference),
                requireSatisfied
            );
            if (
                observation.Output.Decision == ReviewDecisionValue.Accept
                && !observation.Context.State.HasCompleteSuccessfulVerification
            )
            {
                problems.Add(
                    new(
                        "$verification",
                        "Accept requires successful deterministic verification for the current candidate."
                    )
                );
            }
            return problems;
        };

    private static void AddCoverage(
        List<StructuredOutputProblem> problems,
        string path,
        IReadOnlyList<ReviewAssessment> assessments,
        IEnumerable<string> expected,
        bool requireSatisfied
    )
    {
        var expectedIds = expected.ToHashSet(StringComparer.Ordinal);
        var ids = assessments.Select(x => x.Id).ToArray();
        foreach (var duplicate in ids.GroupBy(x => x).Where(x => x.Count() > 1))
        {
            problems.Add(new(path, $"Duplicate obligation reference: {duplicate.Key}"));
        }
        foreach (
            var unknown in ids.Where(x => !expectedIds.Contains(x)).Distinct(StringComparer.Ordinal)
        )
        {
            problems.Add(new(path, $"Unknown obligation reference: {unknown}"));
        }
        foreach (var missing in expectedIds.Except(ids, StringComparer.Ordinal))
        {
            problems.Add(new(path, $"Missing obligation reference: {missing}"));
        }
        if (assessments.Any(x => string.IsNullOrWhiteSpace(x.Evidence)))
        {
            problems.Add(new(path, "Every obligation assessment requires evidence."));
        }
        if (requireSatisfied && assessments.Any(x => !x.Satisfied))
        {
            problems.Add(new(path, "Accept requires every obligation assessment to be satisfied."));
        }
    }
}
