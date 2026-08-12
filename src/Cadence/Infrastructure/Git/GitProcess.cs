using Tandem.Advanced;

namespace Cadence.Git;

public sealed record GitResult(int ExitCode, string Stdout, string Stderr, bool TimedOut);

public sealed class GitProcess(
    string? gitPath = null,
    TimeSpan? timeout = null,
    int maximumOutputBytesPerStream = 16 * 1024 * 1024
)
{
    private static readonly TimeSpan _defaultTimeout = TimeSpan.FromMinutes(2);
    private readonly string _gitPath = gitPath ?? "git";
    private readonly TimeSpan _timeout = ValidateTimeout(timeout ?? _defaultTimeout);
    private readonly int _maximumOutputBytesPerStream = maximumOutputBytesPerStream;

    public async Task<GitResult> RunAsync(
        string? workingDirectory,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken
    )
    {
        var result = await LocalProcess.RunAsync(
            new LocalProcessRequest(
                _gitPath,
                ["-c", "core.fsmonitor=false", .. args],
                workingDirectory,
                _timeout,
                _maximumOutputBytesPerStream,
                new Dictionary<string, string>
                {
                    ["GIT_PAGER"] = "cat",
                    ["GIT_TERMINAL_PROMPT"] = "0",
                    ["GIT_OPTIONAL_LOCKS"] = "0",
                }
            ),
            cancellationToken
        );
        if (result.StdoutTruncated || result.StderrTruncated)
        {
            throw new InvalidOperationException(
                "Git output exceeded the complete-output capture limit."
            );
        }

        return new GitResult(result.ExitCode, result.Stdout, result.Stderr, result.TimedOut);
    }

    private static TimeSpan ValidateTimeout(TimeSpan timeout) =>
        timeout > TimeSpan.Zero ? timeout : throw new ArgumentOutOfRangeException(nameof(timeout));
}
