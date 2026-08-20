using System.Text.Json.Serialization;

namespace Cadence;

public sealed record PlannerHumanQuestion(
    string Question,
    string Reason,
    HumanDecisionDomain Domain
);

public sealed record PlannerHumanAnswer(string Text);

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ReviewerHumanRequest.HumanDecision), "human-decision")]
[JsonDerivedType(typeof(ReviewerHumanRequest.RepairCap), "repair-cap")]
public abstract record ReviewerHumanRequest(string Question, string Reason)
{
    public sealed record HumanDecision(string Question, string Reason, HumanDecisionDomain Domain)
        : ReviewerHumanRequest(Question, Reason);

    public sealed record RepairCap(string Question, string Reason)
        : ReviewerHumanRequest(Question, Reason);
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(ReviewerHumanAnswer.HumanDecision), "human-decision")]
[JsonDerivedType(typeof(ReviewerHumanAnswer.ContinueRepairs), "continue-repairs")]
[JsonDerivedType(typeof(ReviewerHumanAnswer.Stop), "stop")]
public abstract record ReviewerHumanAnswer
{
    public sealed record HumanDecision(string Text) : ReviewerHumanAnswer;

    public sealed record ContinueRepairs : ReviewerHumanAnswer;

    public sealed record Stop : ReviewerHumanAnswer;
}
