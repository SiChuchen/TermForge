using TermForge.Contracts;
using TermForge.Platform;
using Xunit;

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
