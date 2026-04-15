using TermForge.Contracts;
using TermForge.Platform;
using Xunit;
using System.Diagnostics;
using System.Reflection;

namespace TermForge.Core.Tests;

public class GitProxyAdapterTests
{
    [Fact]
    public void GitProxySnapshot_defaults_to_global_scope_shape()
    {
        var snapshot = new GitProxySnapshot(
            Available: true,
            Scope: "global",
            HttpProxy: "http://127.0.0.1:7890",
            HttpsProxy: "http://127.0.0.1:7890",
            NoProxy: "127.0.0.1,localhost,::1");

        Assert.True(snapshot.Available);
        Assert.Equal("global", snapshot.Scope);
        Assert.Equal("http://127.0.0.1:7890", snapshot.HttpProxy);
    }

    [Fact]
    public void GitProxyPlan_enable_only_emits_changed_actions()
    {
        var adapter = new FakeGitProxyAdapter(
            new GitProxySnapshot(
                Available: true,
                Scope: "global",
                HttpProxy: "",
                HttpsProxy: "",
                NoProxy: ""));

        var plan = adapter.PlanEnable(
            "http://127.0.0.1:7890",
            "http://127.0.0.1:7890",
            "127.0.0.1,localhost,::1");

        Assert.Equal("git", plan.Target);
        Assert.Equal("enable", plan.Mode);
        Assert.Equal(3, plan.Actions.Count);
    }

    [Fact]
    public void WindowsGitProxyAdapter_read_current_treats_missing_key_as_empty()
    {
        var adapter = WindowsGitProxyAdapterTestHarness.CreateWindowsAdapter(
            () => "git.exe",
            (_, args) =>
            {
                Assert.Equal(new[] { "config", "--global", "--get" }, args.Take(3).ToArray());
                return args[3] switch
                {
                    "http.proxy" => (1, "", ""),
                    "https.proxy" => (0, "http://127.0.0.1:7890", ""),
                    "http.noProxy" => (0, "localhost,127.0.0.1", ""),
                    _ => throw new InvalidOperationException("unexpected key")
                };
            });

        var snapshot = adapter.ReadCurrent();

        Assert.Equal(string.Empty, snapshot.HttpProxy);
        Assert.Equal("http://127.0.0.1:7890", snapshot.HttpsProxy);
        Assert.Equal("localhost,127.0.0.1", snapshot.NoProxy);
    }

    [Fact]
    public void WindowsGitProxyAdapter_read_current_throws_when_git_config_is_broken()
    {
        var adapter = WindowsGitProxyAdapterTestHarness.CreateWindowsAdapter(
            () => "git.exe",
            (_, args) => args[3] switch
            {
                "http.proxy" => (128, "", "fatal: bad config line 1 in file .gitconfig"),
                _ => throw new InvalidOperationException("unexpected key")
            });

        Action act = () => adapter.ReadCurrent();
        var error = Assert.Throws<InvalidOperationException>(act);

        Assert.Contains("bad config", error.Message);
    }

    [Theory]
    [InlineData("env", "set", "http.proxy")]
    [InlineData("git", "append", "http.proxy")]
    [InlineData("git", "set", "credential.helper")]
    public void WindowsGitProxyAdapter_apply_rejects_invalid_plan_shapes(string target, string action, string key)
    {
        var adapter = WindowsGitProxyAdapterTestHarness.CreateWindowsAdapter(
            () => "git.exe",
            (_, _) => (0, "", ""));

        var plan = new GitProxyPlan(
            target,
            "enable",
            new GitProxySnapshot(true, "global", "", "", ""),
            new GitProxySnapshot(true, "global", "http://127.0.0.1:7890", "", ""),
            new[] { new GitProxyPlanAction(key, action, "", "http://127.0.0.1:7890") });

        Action act = () => adapter.Apply(plan);
        Assert.Throws<InvalidOperationException>(act);
    }

    [Fact]
    public void WindowsGitProxyAdapter_apply_passes_value_as_single_argv_token()
    {
        var seenCalls = new List<IReadOnlyList<string>>();
        var adapter = WindowsGitProxyAdapterTestHarness.CreateWindowsAdapter(
            () => "git.exe",
            (_, args) =>
            {
                seenCalls.Add(args.ToArray());
                return args[2] == "--get" ? (1, "", "") : (0, "", "");
            });

        var plan = new GitProxyPlan(
            "git",
            "enable",
            new GitProxySnapshot(true, "global", "", "", ""),
            new GitProxySnapshot(true, "global", "http://proxy host:7890", "", ""),
            new[] { new GitProxyPlanAction("http.proxy", "set", "", "http://proxy host:7890") });

        adapter.Apply(plan);

        Assert.Contains(
            seenCalls,
            args => args.SequenceEqual(new[] { "config", "--global", "http.proxy", "http://proxy host:7890" }));
    }

    [Fact]
    public void WindowsGitProxyAdapter_plan_enable_throws_when_git_is_unavailable()
    {
        var adapter = WindowsGitProxyAdapterTestHarness.CreateWindowsAdapter(
            () => null,
            (_, _) => throw new InvalidOperationException("should not run"));

        Action act = () => adapter.PlanEnable(
            "http://127.0.0.1:7890",
            "http://127.0.0.1:7890",
            "127.0.0.1,localhost,::1");

        var error = Assert.Throws<InvalidOperationException>(act);
        Assert.Equal("git not available", error.Message);
    }

    [Fact]
    public void IProxyTargetAdapter_TargetName_returns_git()
    {
        var adapter = WindowsGitProxyAdapterTestHarness.CreateWindowsAdapterAsProxyTarget(
            () => "git.exe",
            (_, _) => (0, "", ""));
        Assert.Equal("git", adapter.TargetName);
    }

    [Fact]
    public void IProxyTargetAdapter_ReadCurrent_maps_git_snapshot()
    {
        var adapter = WindowsGitProxyAdapterTestHarness.CreateWindowsAdapterAsProxyTarget(
            () => "git.exe",
            (_, args) =>
            {
                Assert.Equal(new[] { "config", "--global", "--get" }, args.Take(3).ToArray());
                return args[3] switch
                {
                    "http.proxy" => (0, "http://127.0.0.1:7890", ""),
                    "https.proxy" => (0, "http://127.0.0.1:7890", ""),
                    "http.noProxy" => (0, "localhost,127.0.0.1", ""),
                    _ => throw new InvalidOperationException("unexpected key")
                };
            });
        var result = adapter.ReadCurrent();
        Assert.True(result.Enabled);
        Assert.Equal("http://127.0.0.1:7890", result.Http);
        Assert.Equal("http://127.0.0.1:7890", result.Https);
        Assert.Equal("localhost,127.0.0.1", result.NoProxy);
    }

    [Fact]
    public void IProxyTargetAdapter_Apply_writes_proxy_via_snapshot()
    {
        var seenCalls = new List<IReadOnlyList<string>>();
        var state = new Dictionary<string, string>();
        var adapter = WindowsGitProxyAdapterTestHarness.CreateWindowsAdapterAsProxyTarget(
            () => "git.exe",
            (_, args) =>
            {
                seenCalls.Add(args.ToArray());
                if (args[2] == "--get")
                {
                    return state.TryGetValue(args[3], out var val) ? (0, val, "") : (1, "", "");
                }
                if (args[2] == "--unset")
                {
                    state.Remove(args[3]);
                    return (0, "", "");
                }
                // "set": config --global <key> <value>
                state[args[2]] = args[3];
                return (0, "", "");
            });
        var desired = new ProxyConfigSnapshot(true, "http://proxy:8080", "http://proxy:8443", "localhost");
        var result = adapter.Apply(desired);
        Assert.True(result.Enabled);
        Assert.Equal("http://proxy:8080", result.Http);
        Assert.Contains(seenCalls, args => args.SequenceEqual(new[] { "config", "--global", "http.proxy", "http://proxy:8080" }));
    }

    [Fact]
    public void IProxyTargetAdapter_Verify_passes_on_match()
    {
        var adapter = WindowsGitProxyAdapterTestHarness.CreateWindowsAdapterAsProxyTarget(
            () => "git.exe",
            (_, args) => args[2] == "--get"
                ? args[3] switch
                {
                    "http.proxy" => (0, "http://127.0.0.1:7890", ""),
                    "https.proxy" => (0, "http://127.0.0.1:7890", ""),
                    "http.noProxy" => (1, "", ""),
                    _ => throw new InvalidOperationException("unexpected key")
                }
                : (0, "", ""));
        var desired = new ProxyConfigSnapshot(true, "http://127.0.0.1:7890", "http://127.0.0.1:7890", "");
        var result = adapter.Verify(desired);
        Assert.True(result.Enabled);
        Assert.Equal("http://127.0.0.1:7890", result.Http);
    }

    [Fact]
    public void IProxyTargetAdapter_Rollback_restores_state()
    {
        var state = new Dictionary<string, string> { ["http.proxy"] = "http://after:9999" };
        var adapter = WindowsGitProxyAdapterTestHarness.CreateWindowsAdapterAsProxyTarget(
            () => "git.exe",
            (_, args) =>
            {
                if (args[2] == "--get")
                    return state.TryGetValue(args[3], out var val) ? (0, val, "") : (1, "", "");
                if (args[2] == "--unset")
                {
                    state.Remove(args[3]);
                    return (0, "", "");
                }
                // "set": config --global <key> <value>
                state[args[2]] = args[3];
                return (0, "", "");
            });
        var before = new ProxyConfigSnapshot(true, "http://before:8080", "", "");
        var result = adapter.Rollback(before);
        Assert.Equal("http://before:8080", result.Http);
    }

    [Fact]
    public void IProxyTargetAdapter_PlanEnable_returns_snapshot()
    {
        var adapter = WindowsGitProxyAdapterTestHarness.CreateWindowsAdapterAsProxyTarget(
            () => "git.exe",
            (_, _) => throw new InvalidOperationException("PlanEnable should not invoke git"));
        var result = adapter.PlanEnable("http://p:8080", "http://p:8443", "noproxy");
        Assert.True(result.Enabled);
        Assert.Equal("http://p:8080", result.Http);
        Assert.Equal("http://p:8443", result.Https);
        Assert.Equal("noproxy", result.NoProxy);
    }

    [Fact]
    public void IProxyTargetAdapter_PlanDisable_returns_disabled()
    {
        var adapter = WindowsGitProxyAdapterTestHarness.CreateWindowsAdapterAsProxyTarget(
            () => "git.exe",
            (_, _) => throw new InvalidOperationException("PlanDisable should not invoke git"));
        var result = adapter.PlanDisable();
        Assert.False(result.Enabled);
        Assert.Equal(string.Empty, result.Http);
        Assert.Equal(string.Empty, result.Https);
        Assert.Equal(string.Empty, result.NoProxy);
    }
}

internal sealed class FakeGitProxyAdapter : IGitProxyAdapter
{
    private GitProxySnapshot _current;

    public FakeGitProxyAdapter(GitProxySnapshot current)
    {
        _current = current;
    }

    public bool IsAvailable()
    {
        return _current.Available;
    }

    public GitProxySnapshot ReadCurrent()
    {
        return _current;
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
        _current = plan.Desired;
        return _current;
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
        _current = before;
        return before;
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
}

internal static class WindowsGitProxyAdapterTestHarness
{
    public static IGitProxyAdapter CreateWindowsAdapter(
        Func<string?> resolveGitExecutable,
        Func<string, IReadOnlyList<string>, (int ExitCode, string StdOut, string StdErr)> runGit)
    {
        BuildWindowsAssembly();

        var assembly = Assembly.LoadFrom(GetWindowsAssemblyPath());
        var adapterType = assembly.GetType("TermForge.Platform.Windows.WindowsGitProxyAdapter", throwOnError: true)!;
        var constructor = adapterType.GetConstructors()
            .Single(ctor => ctor.GetParameters().Length == 2);

        return (IGitProxyAdapter)constructor.Invoke(new object[]
        {
            resolveGitExecutable,
            runGit
        });
    }

    public static IProxyTargetAdapter CreateWindowsAdapterAsProxyTarget(
        Func<string?> resolveGitExecutable,
        Func<string, IReadOnlyList<string>, (int ExitCode, string StdOut, string StdErr)> runGit)
    {
        BuildWindowsAssembly();

        var assembly = Assembly.LoadFrom(GetWindowsAssemblyPath());
        var adapterType = assembly.GetType("TermForge.Platform.Windows.WindowsGitProxyAdapter", throwOnError: true)!;
        var constructor = adapterType.GetConstructors()
            .Single(ctor => ctor.GetParameters().Length == 2);

        return (IProxyTargetAdapter)constructor.Invoke(new object[]
        {
            resolveGitExecutable,
            runGit
        });
    }

    private static void BuildWindowsAssembly()
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("build");
        startInfo.ArgumentList.Add(GetWindowsProjectPath());
        startInfo.ArgumentList.Add("--nologo");

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("failed to start dotnet build");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"failed to build windows adapter assembly{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}".Trim());
        }
    }

    private static string GetWindowsProjectPath()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "TermForge.Platform.Windows", "TermForge.Platform.Windows.csproj"));
    }

    private static string GetWindowsAssemblyPath()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "TermForge.Platform.Windows", "bin", "Debug", "net8.0", "TermForge.Platform.Windows.dll"));
    }
}
