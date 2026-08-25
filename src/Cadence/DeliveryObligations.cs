namespace Cadence;

public enum DeliveryObligationKind
{
    Outcome,
    AcceptanceCriterion,
    PacketConstraint,
    PlannerConstraint,
}

public sealed record DeliveryObligation(
    string Reference,
    DeliveryObligationKind Kind,
    string LocalId,
    string Requirement,
    string? LinkedOutcomeId = null
);

public static class DeliveryObligations
{
    public static IReadOnlyList<DeliveryObligation> From(CadenceState state) =>
        state
            .Packet.Outcomes.Select(x => new DeliveryObligation(
                $"outcome:{x.Id}",
                DeliveryObligationKind.Outcome,
                x.Id,
                x.Description
            ))
            .Concat(
                state.Packet.Acceptance.Select(x => new DeliveryObligation(
                    $"acceptance:{x.Id}",
                    DeliveryObligationKind.AcceptanceCriterion,
                    x.Id,
                    x.Requirement,
                    x.OutcomeId
                ))
            )
            .Concat(
                state.Packet.Constraints.Select(x => new DeliveryObligation(
                    $"packet-constraint:{x.Id}",
                    DeliveryObligationKind.PacketConstraint,
                    x.Id,
                    x.Requirement
                ))
            )
            .Concat(
                state.PlannerConstraints.Select(x => new DeliveryObligation(
                    $"planner-constraint:{x.Id}",
                    DeliveryObligationKind.PlannerConstraint,
                    x.Id,
                    x.Requirement
                ))
            )
            .ToArray();
}

public static class DeliveryContractRenderer
{
    public static string Render(CadenceState state)
    {
        var catalog = DeliveryObligations.From(state);
        return $"""
            Delivery contract

            Outcomes
            {RenderKind(catalog, DeliveryObligationKind.Outcome)}

            Acceptance criteria
            {RenderAcceptance(catalog)}

            Packet constraints
            {RenderKind(catalog, DeliveryObligationKind.PacketConstraint)}

            Active Planner constraints
            {RenderKind(catalog, DeliveryObligationKind.PlannerConstraint)}

            Use the bracketed references above exactly when a capability or structured result requests an obligation ID.
            """;
    }

    private static string RenderKind(
        IReadOnlyList<DeliveryObligation> catalog,
        DeliveryObligationKind kind
    )
    {
        var entries = catalog.Where(x => x.Kind == kind).ToArray();
        return entries.Length == 0
            ? "(none)"
            : string.Join("\n", entries.Select(x => $"- [{x.Reference}] {x.Requirement}"));
    }

    private static string RenderAcceptance(IReadOnlyList<DeliveryObligation> catalog)
    {
        var entries = catalog
            .Where(x => x.Kind == DeliveryObligationKind.AcceptanceCriterion)
            .ToArray();
        return entries.Length == 0
            ? "(none)"
            : string.Join(
                "\n",
                entries.Select(x =>
                    $"- [{x.Reference}] for [outcome:{x.LinkedOutcomeId}]: {x.Requirement}"
                )
            );
    }
}
