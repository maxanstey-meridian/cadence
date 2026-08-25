using Cadence.Git;
using FluentAssertions;

namespace Cadence.Tests;

public sealed class ProcessAdoptionTests
{
    [Fact(Timeout = 5_000)]
    public async Task Git_process_preserves_arguments_working_directory_and_nonzero_result()
    {
        var repository = TestSupport.CreateGitRepository();
        try
        {
            var process = new GitProcess();
            var write = await process.RunAsync(
                repository,
                ["config", "--local", "cadence.argument", "value with spaces"],
                TestContext.Current.CancellationToken
            );
            var read = await process.RunAsync(
                repository,
                ["config", "--local", "--get", "cadence.argument"],
                TestContext.Current.CancellationToken
            );
            var root = await process.RunAsync(
                repository,
                ["rev-parse", "--show-toplevel"],
                TestContext.Current.CancellationToken
            );
            var nonzero = await process.RunAsync(
                repository,
                ["rev-parse", "--verify", "missing ref with spaces"],
                TestContext.Current.CancellationToken
            );

            write.ExitCode.Should().Be(0);
            read.Stdout.Trim().Should().Be("value with spaces");
            root.Stdout.Trim().Should().EndWith(Path.GetFileName(repository));
            nonzero.ExitCode.Should().NotBe(0);
            nonzero.TimedOut.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Fact(Timeout = 5_000)]
    public async Task Git_process_fails_closed_when_output_is_truncated()
    {
        var repository = TestSupport.CreateGitRepository();
        try
        {
            var process = new GitProcess(maximumOutputBytesPerStream: 8);

            var act = () =>
                process.RunAsync(
                    repository,
                    ["status", "--porcelain=v2", "--branch"],
                    TestContext.Current.CancellationToken
                );

            await act.Should()
                .ThrowAsync<InvalidOperationException>()
                .WithMessage("*capture limit*");
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    [Fact(Timeout = 5_000)]
    public async Task Git_process_maps_timeout_to_minus_one_timed_out_result()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        var executable = CreateSleepingExecutable();
        var process = new GitProcess(executable, TimeSpan.FromMilliseconds(100));
        try
        {
            var result = await process.RunAsync(null, [], TestContext.Current.CancellationToken);

            result.ExitCode.Should().Be(-1);
            result.TimedOut.Should().BeTrue();
        }
        finally
        {
            File.Delete(executable);
        }
    }

    [Fact(Timeout = 5_000)]
    public async Task Git_process_propagates_caller_cancellation()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        var executable = CreateSleepingExecutable();
        var process = new GitProcess(executable, TimeSpan.FromSeconds(10));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        try
        {
            var act = () => process.RunAsync(null, [], cancellation.Token);

            await act.Should().ThrowAsync<OperationCanceledException>();
        }
        finally
        {
            File.Delete(executable);
        }
    }

    [Fact]
    public void Verification_maps_commands_to_the_platform_shell()
    {
        var (fileName, arguments) = VerificationOperation.BuildProcessStart("printf cadence");

        if (OperatingSystem.IsMacOS())
        {
            fileName.Should().Be("/bin/zsh");
            arguments.Should().Equal("-lc", "printf cadence");
        }
        else if (OperatingSystem.IsLinux())
        {
            fileName.Should().Be("/bin/bash");
            arguments.Should().Equal("-lc", "printf cadence");
        }
        else
        {
            fileName.Should().Be("cmd.exe");
            arguments.Should().Equal("/d", "/s", "/c", "printf cadence");
        }
    }

    [Fact(Timeout = 5_000)]
    public async Task Verification_timeout_is_a_failed_result_with_deterministic_evidence()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        var result = await RunVerificationAsync("sleep 5", TimeSpan.FromMilliseconds(100));

        result.ExitCode.Should().Be(-1);
        result.TimedOut.Should().BeTrue();
        result.Stderr.Should().Contain("Command timed out after 0.1 seconds.");
    }

    [Fact]
    public async Task Verification_truncation_preserves_success_with_incomplete_evidence()
    {
        var result = await RunVerificationAsync(
            "printf 'output-longer-than-bound'",
            outputBound: 8
        );

        result.ExitCode.Should().Be(0);
        result.TimedOut.Should().BeFalse();
        result.Stdout.Should().Be("output-l");
        result.Stderr.Should().Contain("output was truncated at the capture limit");
    }

    private static async Task<VerificationResult> RunVerificationAsync(
        string command,
        TimeSpan? timeout = null,
        int outputBound = 16 * 1024 * 1024
    )
    {
        var repository = TestSupport.CreateGitRepository();
        try
        {
            var candidate = TestSupport.Head(repository);
            var packet = TestSupport.Packet() with
            {
                Repository = repository,
                Verification = [new PacketCommand("test", command)],
            };
            var stage = new VerificationStage(
                new VerificationOperation(new GitProcess(), timeout, outputBound)
            );
            var pipeline = Pipeline.Start(stage, "process-adoption").Build(stage);
            var run = await new PipelineRunner().RunAsync(
                pipeline,
                CadenceState.Create(packet, candidate, repository) with
                {
                    CandidateSha = candidate,
                },
                cancellationToken: TestContext.Current.CancellationToken
            );

            return run.State.VerificationResults.Should().ContainSingle().Subject;
        }
        finally
        {
            Directory.Delete(repository, recursive: true);
        }
    }

    private static string CreateSleepingExecutable()
    {
        if (OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException();
        }
        var path = Path.Combine(Path.GetTempPath(), $"cadence-sleep-{Guid.NewGuid():N}");
        File.WriteAllText(path, "#!/bin/sh\nsleep 5\n");
        File.SetUnixFileMode(
            path,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
        );
        return path;
    }
}
