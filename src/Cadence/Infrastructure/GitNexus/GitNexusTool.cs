using System.ComponentModel;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using Tandem.Advanced;

namespace Cadence;

internal static class GitNexusTool
{
    private static readonly Regex _runIdPattern = new("^[a-f0-9]{32}$", RegexOptions.Compiled);
    internal const string Name = "gitnexus";
    private static readonly HashSet<string> _allowedSubcommands = new(
        [
            "analyze",
            "augment",
            "check",
            "context",
            "cypher",
            "detect-changes",
            "detect_changes",
            "doctor",
            "impact",
            "list",
            "query",
            "status",
            "trace",
        ],
        StringComparer.Ordinal
    );
    private static readonly HashSet<string> _repositoryScopedSubcommands = new(
        [
            "check",
            "context",
            "cypher",
            "detect-changes",
            "detect_changes",
            "impact",
            "query",
            "trace",
        ],
        StringComparer.Ordinal
    );

    internal static AgentWorkspaceTool Registration { get; } =
        AgentWorkspaceTool.Define(
            Name,
            workspacePath =>
                AIFunctionFactory.Create(
                    new GitNexusRepository(workspacePath).RunAsync,
                    Name,
                    "Run an allowed GitNexus repository-analysis command in the current workspace. Use status/analyze to prepare an index, impact before editing symbols, and detect-changes before reporting completion."
                ),
            ToolEffect.ProcessExecution,
            ToolEvidence.RepositoryInspection
        );

    internal static void Validate(string subcommand, IReadOnlyList<string>? arguments)
    {
        if (!_allowedSubcommands.Contains(subcommand))
        {
            throw new ArgumentException(
                $"GitNexus subcommand '{subcommand}' is not available in Cadence.",
                nameof(subcommand)
            );
        }
        if (arguments is { Count: > 64 })
        {
            throw new ArgumentException(
                "GitNexus accepts at most 64 arguments per invocation.",
                nameof(arguments)
            );
        }
        if (arguments?.Any(argument => argument.Length > 4096 || argument.Contains('\0')) == true)
        {
            throw new ArgumentException(
                "GitNexus arguments must be bounded text without null characters.",
                nameof(arguments)
            );
        }
        if (arguments?.Any(IsIdentityOverride) == true)
        {
            throw new ArgumentException(
                "Cadence owns GitNexus repository identity; --repo, -r, --name, and --allow-duplicate-name are not accepted.",
                nameof(arguments)
            );
        }
    }

    internal static IReadOnlyList<string> BuildArguments(
        string workspacePath,
        string subcommand,
        IReadOnlyList<string>? arguments
    )
    {
        Validate(subcommand, arguments);
        var repositoryAlias = RepositoryAlias(workspacePath);
        var result = new List<string> { subcommand };
        if (subcommand == "analyze")
        {
            result.Add(Path.GetFullPath(workspacePath));
            result.Add("--name");
            result.Add(repositoryAlias);
            if (arguments?.Contains("--index-only", StringComparer.Ordinal) != true)
            {
                result.Add("--index-only");
            }
        }
        if (arguments is not null)
        {
            result.AddRange(arguments);
        }
        if (_repositoryScopedSubcommands.Contains(subcommand))
        {
            result.Add("--repo");
            result.Add(repositoryAlias);
        }
        return result;
    }

    internal static string RepositoryAlias(string workspacePath)
    {
        var workspace = new DirectoryInfo(Path.GetFullPath(workspacePath));
        var runId = workspace.Parent?.Name;
        if (runId is null || !_runIdPattern.IsMatch(runId))
        {
            throw new ArgumentException(
                $"Cadence workspace '{workspacePath}' is not nested beneath a 32-character run UUID.",
                nameof(workspacePath)
            );
        }
        return $"cadence-{runId}";
    }

    private static bool IsIdentityOverride(string argument) =>
        argument is "--repo" or "-r"
        || argument is "--name" or "--allow-duplicate-name"
        || argument.StartsWith("--repo=", StringComparison.Ordinal)
        || argument.StartsWith("-r=", StringComparison.Ordinal)
        || argument.StartsWith("--name=", StringComparison.Ordinal);
}

internal sealed class GitNexusRepository(
    string workspacePath,
    Func<ProcessStartInfo, CancellationToken, Task<GitNexusProcessResult>>? execute = null
)
{
    private static readonly TimeSpan _timeout = TimeSpan.FromMinutes(30);
    private const int MaximumOutputCharacters = 128 * 1024;
    private readonly string _workspacePath = Path.GetFullPath(workspacePath);
    private readonly Func<
        ProcessStartInfo,
        CancellationToken,
        Task<GitNexusProcessResult>
    > _execute = execute ?? ExecuteProcessAsync;

    internal async Task<string> RunAsync(
        [Description(
            "Allowed GitNexus subcommand, for example status, analyze, impact, or detect-changes."
        )]
            string subcommand,
        [Description("Arguments passed directly to GitNexus without shell interpretation.")]
            string[]? arguments = null,
        CancellationToken cancellationToken = default
    )
    {
        var result = await _execute(
            BuildStartInfo(_workspacePath, subcommand, arguments ?? []),
            cancellationToken
        );
        if (result.ExitCode != 0 && subcommand != "analyze" && IsRepositoryMissing(result))
        {
            var analyze = await _execute(
                BuildStartInfo(_workspacePath, "analyze", []),
                cancellationToken
            );
            EnsureSucceeded("analyze", analyze);
            result = await _execute(
                BuildStartInfo(_workspacePath, subcommand, arguments ?? []),
                cancellationToken
            );
        }
        EnsureSucceeded(subcommand, result);
        return FormatResult(result);
    }

    private static async Task<GitNexusProcessResult> ExecuteProcessAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken
    )
    {
        using var process = new Process { StartInfo = startInfo };
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"GitNexus '{startInfo.ArgumentList[0]}' exceeded {_timeout}."
            );
        }
        return new GitNexusProcessResult(process.ExitCode, await stdout, await stderr);
    }

    internal static ProcessStartInfo BuildStartInfo(
        string workspacePath,
        string subcommand,
        IReadOnlyList<string> arguments
    )
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "gitnexus",
            WorkingDirectory = Path.GetFullPath(workspacePath),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in GitNexusTool.BuildArguments(workspacePath, subcommand, arguments))
        {
            startInfo.ArgumentList.Add(argument);
        }
        return startInfo;
    }

    private static bool IsRepositoryMissing(GitNexusProcessResult result)
    {
        var output = $"{result.Stdout}\n{result.Stderr}";
        return output.Contains("Repository", StringComparison.OrdinalIgnoreCase)
            && output.Contains("not found", StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureSucceeded(string subcommand, GitNexusProcessResult result)
    {
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"GitNexus '{subcommand}' failed.\n{FormatResult(result)}"
            );
        }
    }

    private static string FormatResult(GitNexusProcessResult result)
    {
        var output =
            $"exitCode: {result.ExitCode}\nstdout:\n{result.Stdout}\nstderr:\n{result.Stderr}";
        return output.Length <= MaximumOutputCharacters
            ? output
            : string.Concat(
                output.AsSpan(0, MaximumOutputCharacters),
                "\n[...truncated by Cadence...]"
            );
    }
}

internal sealed record GitNexusProcessResult(int ExitCode, string Stdout, string Stderr);
