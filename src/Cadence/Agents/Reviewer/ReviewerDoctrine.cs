using System.Security.Cryptography;
using System.Text;

namespace Cadence;

public sealed class ReviewerDoctrine
{
    private ReviewerDoctrine(string source, string sha256, string content)
    {
        Source = source;
        Sha256 = sha256;
        Content = content;
    }

    public string Source { get; }
    public string Sha256 { get; }
    public string Content { get; }

    public static ReviewerDoctrine Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var source = Path.GetFullPath(path);
        if (!File.Exists(source))
        {
            throw new InvalidOperationException($"Reviewer doctrine not found: {source}");
        }

        var bytes = File.ReadAllBytes(source);
        var content = new UTF8Encoding(false, true).GetString(bytes);
        if (content.Length > 0 && content[0] == '\uFEFF')
        {
            content = content[1..];
        }
        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException($"Reviewer doctrine is blank: {source}");
        }

        return new ReviewerDoctrine(
            source,
            Convert.ToHexStringLower(SHA256.HashData(bytes)),
            content
        );
    }
}
