namespace Cadence;

internal static class VerificationResultFormatting
{
    private const int MaximumStreamCharacters = 2000;

    internal static string Format(IReadOnlyList<VerificationResult> results) =>
        string.Join(
            "\n",
            results.Select(result =>
                $"[{(result.ExitCode == 0 ? "PASS" : "FAIL")}] {result.Label}: {result.Command} "
                + $"(exit {result.ExitCode})\nstdout: {FormatStream(result.Stdout)}\nstderr: {FormatStream(result.Stderr)}"
            )
        );

    private static string FormatStream(string value) =>
        value.Length <= MaximumStreamCharacters
            ? value
            : $"[showing final {MaximumStreamCharacters} of {value.Length} characters]\n{value[^MaximumStreamCharacters..]}";
}
