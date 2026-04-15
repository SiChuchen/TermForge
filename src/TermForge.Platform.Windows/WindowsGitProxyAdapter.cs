using System.Diagnostics;
using TermForge.Contracts;
using TermForge.Platform;

namespace TermForge.Platform.Windows;

public sealed class WindowsGitProxyAdapter : IGitProxyAdapter, IProxyTargetAdapter
{
    private static readonly HashSet<string> ManagedKeys = new(StringComparer.Ordinal)
    {
        "http.proxy",
        "https.proxy",
        "http.noProxy"
    };

    private readonly Func<string?> _resolveGitExecutable;
    private readonly Func<string, IReadOnlyList<string>, (int ExitCode, string StdOut, string StdErr)> _runGit;

    public WindowsGitProxyAdapter(
        Func<string?>? resolveGitExecutable = null,
        Func<string, IReadOnlyList<string>, (int ExitCode, string StdOut, string StdErr)>? runGit = null)
    {
        _resolveGitExecutable = resolveGitExecutable ?? ResolveGitExecutable;
        _runGit = runGit ?? RunGitProcess;
    }

    public bool IsAvailable()
    {
        return _resolveGitExecutable() is not null;
    }

    public GitProxySnapshot ReadCurrent()
    {
        var git = _resolveGitExecutable();
        if (git is null)
        {
            return new GitProxySnapshot(false, "global", string.Empty, string.Empty, string.Empty);
        }

        return new GitProxySnapshot(
            Available: true,
            Scope: "global",
            HttpProxy: ReadKey(git, "http.proxy"),
            HttpsProxy: ReadKey(git, "https.proxy"),
            NoProxy: ReadKey(git, "http.noProxy"));
    }

    public GitProxyPlan PlanEnable(string httpProxy, string httpsProxy, string noProxy)
    {
        var before = ReadCurrent();
        if (!before.Available)
        {
            throw new InvalidOperationException("git not available");
        }

        var desired = new GitProxySnapshot(true, "global", httpProxy, httpsProxy, noProxy);
        return BuildPlan("enable", before, desired);
    }

    public GitProxyPlan PlanDisable()
    {
        var before = ReadCurrent();
        if (!before.Available)
        {
            throw new InvalidOperationException("git not available");
        }

        var desired = new GitProxySnapshot(before.Available, "global", string.Empty, string.Empty, string.Empty);
        return BuildPlan("disable", before, desired);
    }

    public GitProxySnapshot Apply(GitProxyPlan plan)
    {
        ValidatePlan(plan);

        var git = _resolveGitExecutable() ?? throw new InvalidOperationException("git not available");
        foreach (var action in plan.Actions)
        {
            if (action.Action == "set")
            {
                ExecuteGit(git, new[] { "config", "--global", action.Key, action.After });
            }
            else if (action.Action == "unset")
            {
                ExecuteGit(git, new[] { "config", "--global", "--unset", action.Key }, ignoreFailure: true);
            }
        }

        return ReadCurrent();
    }

    public GitProxySnapshot Verify(GitProxySnapshot desired)
    {
        var current = ReadCurrent();
        if (current.HttpProxy != desired.HttpProxy ||
            current.HttpsProxy != desired.HttpsProxy ||
            current.NoProxy != desired.NoProxy)
        {
            throw new InvalidOperationException("git proxy verification failed");
        }

        return current;
    }

    public GitProxySnapshot Rollback(GitProxySnapshot before)
    {
        var plan = BuildPlan("rollback", ReadCurrent(), before);
        Apply(plan);
        return Verify(before);
    }

    private static ProxyConfigSnapshot MapToProxySnapshot(GitProxySnapshot git)
    {
        var enabled = !string.IsNullOrWhiteSpace(git.HttpProxy) || !string.IsNullOrWhiteSpace(git.HttpsProxy);
        return new ProxyConfigSnapshot(enabled, git.HttpProxy, git.HttpsProxy, git.NoProxy);
    }

    private static GitProxySnapshot MapToGitSnapshot(ProxyConfigSnapshot proxy)
    {
        return new GitProxySnapshot(true, "global", proxy.Http, proxy.Https, proxy.NoProxy);
    }

    // --- IProxyTargetAdapter explicit implementation ---

    string IProxyTargetAdapter.TargetName => "git";

    bool IProxyTargetAdapter.IsAvailable()
    {
        return IsAvailable();
    }

    ProxyConfigSnapshot IProxyTargetAdapter.ReadCurrent()
    {
        var git = ReadCurrent();
        return MapToProxySnapshot(git);
    }

    ProxyConfigSnapshot IProxyTargetAdapter.PlanEnable(string http, string https, string noProxy)
    {
        return new ProxyConfigSnapshot(true, http, https, noProxy);
    }

    ProxyConfigSnapshot IProxyTargetAdapter.PlanDisable()
    {
        return new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty);
    }

    ProxyConfigSnapshot IProxyTargetAdapter.Apply(ProxyConfigSnapshot desired)
    {
        var currentGit = ReadCurrent();
        var desiredGit = MapToGitSnapshot(desired);
        var plan = BuildPlan("enable", currentGit, desiredGit);

        var git = _resolveGitExecutable() ?? throw new InvalidOperationException("git not available");
        foreach (var action in plan.Actions)
        {
            if (action.Action == "set")
            {
                ExecuteGit(git, new[] { "config", "--global", action.Key, action.After });
            }
            else if (action.Action == "unset")
            {
                ExecuteGit(git, new[] { "config", "--global", "--unset", action.Key }, ignoreFailure: true);
            }
        }

        return MapToProxySnapshot(Verify(desiredGit));
    }

    ProxyConfigSnapshot IProxyTargetAdapter.Verify(ProxyConfigSnapshot desired)
    {
        var desiredGit = MapToGitSnapshot(desired);
        var result = Verify(desiredGit);
        return MapToProxySnapshot(result);
    }

    ProxyConfigSnapshot IProxyTargetAdapter.Rollback(ProxyConfigSnapshot before)
    {
        var beforeGit = MapToGitSnapshot(before);
        var result = Rollback(beforeGit);
        return MapToProxySnapshot(result);
    }

    private static GitProxyPlan BuildPlan(string mode, GitProxySnapshot before, GitProxySnapshot desired)
    {
        var actions = new List<GitProxyPlanAction>();
        AddAction(actions, "http.proxy", before.HttpProxy, desired.HttpProxy);
        AddAction(actions, "https.proxy", before.HttpsProxy, desired.HttpsProxy);
        AddAction(actions, "http.noProxy", before.NoProxy, desired.NoProxy);
        return new GitProxyPlan("git", mode, before, desired, actions);
    }

    private static void AddAction(List<GitProxyPlanAction> actions, string key, string before, string after)
    {
        if (before == after)
        {
            return;
        }

        var action = string.IsNullOrWhiteSpace(after) ? "unset" : "set";
        actions.Add(new GitProxyPlanAction(key, action, before, after));
    }

    private static void ValidatePlan(GitProxyPlan plan)
    {
        if (!string.Equals(plan.Target, "git", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("git proxy plan target must be 'git'");
        }

        foreach (var action in plan.Actions)
        {
            if (!string.Equals(action.Action, "set", StringComparison.Ordinal) &&
                !string.Equals(action.Action, "unset", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"unsupported git proxy action '{action.Action}'");
            }

            if (!ManagedKeys.Contains(action.Key))
            {
                throw new InvalidOperationException($"unsupported git proxy key '{action.Key}'");
            }
        }
    }

    private static string? ResolveGitExecutable()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var paths = path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (var entry in paths)
        {
            var candidate = Path.Combine(entry.Trim(), "git.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private string ReadKey(string git, string key)
    {
        var result = _runGit(git, new[] { "config", "--global", "--get", key });
        if (result.ExitCode == 0)
        {
            return result.StdOut.Trim();
        }

        if (result.ExitCode == 1 && string.IsNullOrWhiteSpace(result.StdErr))
        {
            return string.Empty;
        }

        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(result.StdErr)
                ? $"failed to read git config key '{key}'"
                : result.StdErr.Trim());
    }

    private string ExecuteGit(string git, IReadOnlyList<string> arguments, bool ignoreFailure = false)
    {
        var result = _runGit(git, arguments);
        if (result.ExitCode != 0 && !ignoreFailure)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.StdErr) ? "git command failed" : result.StdErr.Trim());
        }

        return result.StdOut;
    }

    private static (int ExitCode, string StdOut, string StdErr) RunGitProcess(string git, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo(git)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("failed to start git");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        return (process.ExitCode, stdout, stderr);
    }
}
