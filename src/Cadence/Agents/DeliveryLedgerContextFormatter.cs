using System.Text;

namespace Cadence;

internal static class CadenceLedgerContextFormatter
{
    private const int CharacterBudget = 8_000;

    public static string Format(CadenceLedgerContext context)
    {
        var text = new StringBuilder("<durable-cadence-context>\n");
        Append(
            text,
            "Outcomes",
            context.Outcomes?.Outcomes.Select(outcome =>
                $"[{outcome.Id}] status={outcome.Status}; implementation={outcome.ImplementationState}; evidence={string.Join("; ", outcome.Evidence)}; next={outcome.NextAction ?? "(none)"}"
            )
        );
        if (context.LatestCheckpoint is { } checkpoint)
        {
            Append(
                text,
                "Latest checkpoint",
                [
                    $"Summary: {checkpoint.Summary}",
                    $"Changed files: {string.Join("; ", checkpoint.ChangedFiles)}",
                    $"Uncertainties: {string.Join("; ", checkpoint.Uncertainties)}",
                    $"Next action: {checkpoint.NextAction}",
                ]
            );
        }
        if (context.Report is { } report)
        {
            Append(
                text,
                "Accepted report",
                [
                    report.Summary,
                    $"Regression tests: {report.RegressionTests.Disposition}; evidence={string.Join("; ", report.RegressionTests.Evidence)}",
                ]
            );
        }
        Append(text, "Active accepted Planner constraints", context.ActivePlannerConstraints);
        Append(
            text,
            "Recent Planner decisions",
            context.PlannerDecisions.Select(decision =>
                $"{decision.Decision}: {decision.Rationale}; safe next action={decision.SafeNextAction}"
            )
        );
        Append(
            text,
            "Verification",
            context.VerificationResults.Select(result =>
                $"{result.Command}: exit={result.ExitCode}; stderr={result.Stderr}"
            )
        );
        Append(
            text,
            "Reviews",
            context.Reviews.Select(decision => $"{decision.Decision}: {decision.Summary}")
        );
        Append(
            text,
            "Human answers",
            context.HumanAnswers.Select(record => $"{record.InteractionId}: {record.Answer}")
        );
        text.Append("</durable-cadence-context>");
        if (text.Length <= CharacterBudget)
        {
            return text.ToString();
        }
        const string marker = "\n[durable context truncated]\n</durable-cadence-context>";
        return text.ToString(0, CharacterBudget - marker.Length) + marker;
    }

    private static void Append(StringBuilder text, string heading, IEnumerable<string>? values)
    {
        var materialized = values?.ToArray() ?? [];
        if (materialized.Length == 0)
        {
            return;
        }
        text.AppendLine($"{heading}:");
        foreach (var value in materialized)
        {
            text.AppendLine($"- {value}");
        }
    }
}
