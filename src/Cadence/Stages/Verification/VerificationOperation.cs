using System.Diagnostics;
using System.Text.Json;
using Cadence.Git;
using Tandem.Advanced;

namespace Cadence;

public sealed class VerificationOperation(
    GitProcess git,
    ICadenceRecordSink records,
    TimeSpan? commandTimeout = null
)
{
    private static readonly TimeSpan _terminationTimeout = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _commandTimeout = commandTimeout ?? TimeSpan.FromMinutes(10);

    public async ValueTask<OperationResult<CadenceState>> ExecuteAsync(
        PipelineOperationContext<CadenceState> context,
        CancellationToken cancellationToken
    )
    {
        var blockSw = Stopwatch.StartNew();
        var ctx = context.State;
        var commands = ctx.Packet.Verification;

        if (ctx.VerificationIndex >= commands.Count)
        {
            var allPassed = ctx.VerificationResults.All(r => r.ExitCode == 0);
            var finalKind = allPassed ? OutcomeKinds.CommandPassed : OutcomeKinds.CommandFailed;
            blockSw.Stop();
            return new OperationResult<CadenceState>(
                ctx,
                new OperationOutcome(
                    finalKind,
                    CadenceIds.Verify,
                    "All verification commands complete",
                    JsonSerializer.SerializeToElement(new { }),
                    blockSw.Elapsed
                )
            );
        }

        var command = commands[ctx.VerificationIndex];
        var result = await RunCommandAsync(
            ctx.VerificationIndex,
            command,
            ctx.WorkspacePath,
            cancellationToken
        );
        result = await RejectCandidateMutationAsync(result, ctx, cancellationToken);
        var output = string.Join(
            Environment.NewLine,
            new[] { result.Stdout, result.Stderr }.Where(value => !string.IsNullOrEmpty(value))
        );
        await context.ObserveCommandOutputAsync(
            CadenceIds.Verify,
            command,
            output,
            result.ExitCode,
            cancellationToken
        );
        await records.AcceptVerificationResultAsync(
            $"{context.RunId:N}--{CadenceIds.Verify}--{ctx.VerificationResults.Count + 1}",
            ctx.CandidateSha
                ?? throw new InvalidOperationException(
                    "Verification requires a captured candidate."
                ),
            result,
            cancellationToken
        );

        var results = ctx.VerificationResults.Append(result).ToList();
        var passed = result.ExitCode == 0;
        var newIndex = passed ? ctx.VerificationIndex + 1 : ctx.VerificationIndex;

        var updatedContext = ctx with
        {
            VerificationIndex = newIndex,
            VerificationResults = results,
            VerifiedCandidateSha = passed && newIndex == commands.Count ? ctx.CandidateSha : null,
        };

        var kind = passed ? OutcomeKinds.CommandPassed : OutcomeKinds.CommandFailed;
        var payload = JsonSerializer.SerializeToElement(
            new
            {
                index = result.Index,
                exitCode = result.ExitCode,
                elapsedMs = result.Elapsed.TotalMilliseconds,
            }
        );

        blockSw.Stop();
        return new OperationResult<CadenceState>(
            updatedContext,
            new OperationOutcome(
                kind,
                CadenceIds.Verify,
                passed ? "Command passed" : "Command failed",
                payload,
                blockSw.Elapsed
            )
        );
    }

    private async Task<VerificationResult> RunCommandAsync(
        int index,
        string command,
        string workspacePath,
        CancellationToken cancellationToken
    )
    {
        var (fileName, args) = BuildProcessStart(command);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_commandTimeout);

        var sw = Stopwatch.StartNew();
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workspacePath,
            },
            EnableRaisingEvents = true,
        };

        foreach (var arg in args)
        {
            process.StartInfo.ArgumentList.Add(arg);
        }

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
                    $"Verification command did not terminate within {_terminationTimeout.TotalSeconds:0} seconds.",
                    exception
                );
            }
        }

        sw.Stop();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        cancellationToken.ThrowIfCancellationRequested();

        if (timedOut)
        {
            stderr = string.Join(
                Environment.NewLine,
                new[]
                {
                    stderr,
                    $"Command timed out after {_commandTimeout.TotalSeconds:0.###} seconds.",
                }.Where(value => !string.IsNullOrWhiteSpace(value))
            );
        }

        return new VerificationResult(
            index,
            command,
            timedOut ? -1 : process.ExitCode,
            stdout,
            stderr,
            sw.Elapsed,
            timedOut
        );
    }

    private async Task<VerificationResult> RejectCandidateMutationAsync(
        VerificationResult result,
        CadenceState state,
        CancellationToken cancellationToken
    )
    {
        var head = await git.RunAsync(
            state.WorkspacePath,
            ["rev-parse", "HEAD"],
            cancellationToken
        );
        var status = await git.RunAsync(
            state.WorkspacePath,
            ["status", "--porcelain"],
            cancellationToken
        );
        var candidateUnchanged =
            head.ExitCode == 0
            && status.ExitCode == 0
            && string.Equals(
                head.Stdout.Trim(),
                state.CandidateSha,
                StringComparison.OrdinalIgnoreCase
            )
            && string.IsNullOrWhiteSpace(status.Stdout);
        if (candidateUnchanged)
        {
            return result;
        }

        var evidence = string.Join(
            Environment.NewLine,
            new[]
            {
                result.Stderr,
                "Verification modified the captured candidate. Verification commands must be read-only.",
                head.ExitCode == 0
                    ? $"HEAD: {head.Stdout.Trim()}"
                    : $"git rev-parse failed: {head.Stderr}",
                status.ExitCode == 0 ? status.Stdout : $"git status failed: {status.Stderr}",
            }.Where(value => !string.IsNullOrWhiteSpace(value))
        );
        await RestoreCandidateAsync(state, cancellationToken);
        return result with
        {
            ExitCode = result.ExitCode == 0 ? -1 : result.ExitCode,
            Stderr = evidence,
        };
    }

    private async Task RestoreCandidateAsync(
        CadenceState state,
        CancellationToken cancellationToken
    )
    {
        var candidateSha =
            state.CandidateSha
            ?? throw new InvalidOperationException("Verification requires a captured candidate.");
        var reset = await git.RunAsync(
            state.WorkspacePath,
            ["reset", "--hard", candidateSha],
            cancellationToken
        );
        var clean = await git.RunAsync(state.WorkspacePath, ["clean", "-ffd"], cancellationToken);
        var head = await git.RunAsync(
            state.WorkspacePath,
            ["rev-parse", "HEAD"],
            cancellationToken
        );
        var status = await git.RunAsync(
            state.WorkspacePath,
            ["status", "--porcelain"],
            cancellationToken
        );
        if (
            reset.ExitCode != 0
            || reset.TimedOut
            || clean.ExitCode != 0
            || clean.TimedOut
            || head.ExitCode != 0
            || head.TimedOut
            || status.ExitCode != 0
            || status.TimedOut
            || !string.Equals(head.Stdout.Trim(), candidateSha, StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(status.Stdout)
        )
        {
            throw new InvalidOperationException(
                "Verification modified the candidate and the isolated workspace could not be restored."
            );
        }
    }

    private static (string FileName, string[] Args) BuildProcessStart(string command)
    {
        if (OperatingSystem.IsMacOS())
        {
            return ("/bin/zsh", ["-lc", command]);
        }

        if (OperatingSystem.IsLinux())
        {
            return ("/bin/bash", ["-lc", command]);
        }

        return ("cmd.exe", ["/d", "/s", "/c", command]);
    }
}
