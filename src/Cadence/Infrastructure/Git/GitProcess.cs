using System.Diagnostics;

namespace Cadence.Git;

public sealed record GitResult(int ExitCode, string Stdout, string Stderr, bool TimedOut);

public sealed class GitProcess(string? gitPath = null, TimeSpan? timeout = null)
{
    private static readonly TimeSpan _defaultTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan _terminationTimeout = TimeSpan.FromSeconds(5);
    private readonly string _gitPath = gitPath ?? "git";
    private readonly TimeSpan _timeout = ValidateTimeout(timeout ?? _defaultTimeout);

    public async Task<GitResult> RunAsync(
        string? workingDirectory,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken
    )
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_timeout);

        var startInfo = new ProcessStartInfo
        {
            FileName = _gitPath,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        if (workingDirectory is not null)
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // The linked CTS fires for both caller cancellation and the internal
            // 2-min timeout. Only the internal timeout counts as TimedOut.
            timedOut = !cancellationToken.IsCancellationRequested;
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch { }
            try
            {
                await process
                    .WaitForExitAsync(CancellationToken.None)
                    .WaitAsync(_terminationTimeout, CancellationToken.None);
            }
            catch (TimeoutException) when (cancellationToken.IsCancellationRequested)
            {
                throw new OperationCanceledException(cancellationToken);
            }
            catch (TimeoutException exception)
            {
                throw new TimeoutException(
                    $"Git did not terminate within {_terminationTimeout.TotalSeconds:0} seconds.",
                    exception
                );
            }
            cancellationToken.ThrowIfCancellationRequested();
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return new GitResult(process.ExitCode, stdout, stderr, timedOut);
    }

    private static TimeSpan ValidateTimeout(TimeSpan timeout) =>
        timeout > TimeSpan.Zero ? timeout : throw new ArgumentOutOfRangeException(nameof(timeout));
}
