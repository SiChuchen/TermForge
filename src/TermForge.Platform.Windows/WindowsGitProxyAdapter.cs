using System.Diagnostics;
using TermForge.Contracts;
using TermForge.Platform;

namespace TermForge.Platform.Windows;

public sealed class WindowsGitProxyAdapter : IGitProxyAdapter
{
    public bool IsAvailable()
    {
        return ResolveGitExecutable() is not null;
    }

    public GitProxySnapshot ReadCurrent()
    {
        var git = ResolveGitExecutable();
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
        var desired = new GitProxySnapshot(true, "global", httpProxy, httpsProxy, noProxy);
        return BuildPlan("enable", before, desired);
    }

    public GitProxyPlan PlanDisable()
    {
        var before = ReadCurrent();
        var desired = new GitProxySnapshot(before.Available, "global", string.Empty, string.Empty, string.Empty);
        return BuildPlan("disable", before, desired);
    }

    public GitProxySnapshot Apply(GitProxyPlan plan)
    {
        var git = ResolveGitExecutable() ?? throw new InvalidOperationException("git not available");
        foreach (var action in plan.Actions)
        {
            if (action.Action == "set")
            {
                RunGit(git, $"config --global {action.Key} {EscapeValue(action.After)}");
            }
            else if (action.Action == "unset")
            {
                RunGit(git, $"config --global --unset {action.Key}", ignoreFailure: true);
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

    private static string ReadKey(string git, string key)
    {
        return RunGit(git, $"config --global --get {key}", ignoreFailure: true).Trim();
    }

    private static string RunGit(string git, string arguments, bool ignoreFailure = false)
    {
        var startInfo = new ProcessStartInfo(git, arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("failed to start git");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0 && !ignoreFailure)
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? "git command failed" : stderr.Trim());
        }

        return stdout;
    }

    private static string EscapeValue(string value)
    {
        return value.Contains(' ') ? $"\"{value}\"" : value;
    }
}
