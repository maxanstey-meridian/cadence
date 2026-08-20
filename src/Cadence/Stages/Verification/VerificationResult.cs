namespace Cadence;

public sealed record VerificationResult(
    int Index,
    string Label,
    string Command,
    int ExitCode,
    string Stdout,
    string Stderr,
    TimeSpan Elapsed,
    bool TimedOut
);
