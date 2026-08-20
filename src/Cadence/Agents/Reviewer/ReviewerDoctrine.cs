using System.Text.Json;
using System.Text.Json.Serialization;

namespace Cadence;

public sealed record ReviewerDoctrineClause(string Id, string Text);

public sealed class ReviewerDoctrine
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private ReviewerDoctrine(IReadOnlyList<ReviewerDoctrineClause> clauses) => Clauses = clauses;

    public IReadOnlyList<ReviewerDoctrineClause> Clauses { get; }

    public static ReviewerDoctrine Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var source = Path.GetFullPath(path);
        try
        {
            var document = JsonSerializer.Deserialize<DoctrineDocument>(
                File.ReadAllText(source),
                _jsonOptions
            );
            if (document?.Clauses is not { Count: > 0 } clauses)
            {
                throw new InvalidOperationException(
                    "Reviewer doctrine requires at least one clause."
                );
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var clause in clauses)
            {
                if (
                    clause is null
                    || string.IsNullOrWhiteSpace(clause.Id)
                    || string.IsNullOrWhiteSpace(clause.Text)
                )
                {
                    throw new InvalidOperationException(
                        "Reviewer doctrine clauses require nonblank id and text values."
                    );
                }
                if (!ids.Add(clause.Id))
                {
                    throw new InvalidOperationException(
                        $"Reviewer doctrine clause id '{clause.Id}' is duplicated."
                    );
                }
            }

            return new ReviewerDoctrine(
                Array.AsReadOnly(
                    clauses
                        .Select(clause => new ReviewerDoctrineClause(clause!.Id!, clause.Text!))
                        .ToArray()
                )
            );
        }
        catch (Exception exception)
            when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new InvalidOperationException(
                $"Reviewer doctrine could not be loaded from '{source}'.",
                exception
            );
        }
    }

    private sealed record DoctrineDocument(
        [property: JsonPropertyName("clauses")] List<DoctrineClauseDocument?>? Clauses
    );

    private sealed record DoctrineClauseDocument(
        [property: JsonPropertyName("id")] string? Id,
        [property: JsonPropertyName("text")] string? Text
    );
}
