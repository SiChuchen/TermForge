using System;
using System.Collections.Generic;
using System.IO;
using TermForge.Contracts;
using TermForge.Core.Interfaces;
using TermForge.Platform;
using TermForge.Platform.Windows;
using Xunit;

namespace TermForge.Core.Tests;

public class ProxyWorkflowServiceTests
{
    [Fact]
    public void ProxyWorkflowService_plans_env_enable_operation()
    {
        var configStore = new FakeConfigStore();
        var planStore = new FakePlanStore();
        var ledger = new FakeOperationLedger();
        var environment = new FakePlatformEnvironmentAdapter(
            new ProxyConfigSnapshot(true, "http://env:8080", "http://env:8443", "env.local"));
        var clock = new FakeClock();

        var service = new TermForge.Core.Services.ProxyWorkflowService(configStore, planStore, ledger, environment, clock);

        var result = service.PlanEnable("http://127.0.0.1:7890", "http://127.0.0.1:7890", "127.0.0.1,localhost,::1");
        var payload = result.Payload.ToProxyPlanPayload();

        Assert.Equal("proxy.plan", result.Command);
        Assert.Equal("env", result.Payload.Target);
        Assert.True(payload.Desired.Enabled);
        Assert.Equal("http://env:8080", payload.Before.Http);
    }

    [Fact]
    public void ProxyWorkflowService_plans_env_disable_with_unified_record_contract()
    {
        var configStore = new FakeConfigStore();
        var planStore = new FakePlanStore();
        var ledger = new FakeOperationLedger();
        var environment = new FakePlatformEnvironmentAdapter(
            new ProxyConfigSnapshot(true, "http://env:8080", "http://env:8443", "env.local"));
        var clock = new FakeClock();

        var service = new TermForge.Core.Services.ProxyWorkflowService(configStore, planStore, ledger, environment, clock);

        var result = service.PlanDisable();
        var payload = result.Payload.ToProxyPlanPayload();

        Assert.Equal("proxy.plan", result.Command);
        Assert.Equal("2026-04-13", result.Payload.SchemaVersion);
        Assert.Equal("proxy-plan", result.Payload.PayloadType);
        Assert.Equal("env", result.Payload.Target);
        Assert.Equal("disable", payload.Mode);
        Assert.False(payload.Desired.Enabled);
        Assert.Equal("http://env:8080", payload.Before.Http);
    }

    [Fact]
    public void ProxyWorkflowService_apply_and_rollback_use_live_environment_state()
    {
        var configStore = new FakeConfigStore();
        var planStore = new FakePlanStore();
        var ledger = new FakeOperationLedger();
        var environment = new FakePlatformEnvironmentAdapter(
            new ProxyConfigSnapshot(true, "http://before:8080", "http://before:8443", "before.local"));
        var clock = new FakeClock();
        var service = new TermForge.Core.Services.ProxyWorkflowService(configStore, planStore, ledger, environment, clock);

        var plan = service.PlanEnable("http://target:7890", "", "127.0.0.1");
        var apply = service.Apply(plan.Payload.PlanId);
        var afterApply = environment.Current;
        var rollback = service.Rollback(apply.Payload.ChangeId);
        var applyAfter = apply.Payload.GetAfter<ProxyConfigSnapshot>();
        var rollbackAfter = rollback.Payload.GetAfter<ProxyConfigSnapshot>();

        Assert.Equal("http://target:7890", afterApply.Http);
        Assert.Equal("http://target:7890", applyAfter.Https);
        Assert.Equal("http://before:8080", rollbackAfter.Http);
        Assert.Equal("http://before:8443", rollbackAfter.Https);
    }

    [Fact]
    public void ProxyWorkflowService_generates_unique_ids_with_same_clock_tick()
    {
        var configStore = new FakeConfigStore();
        var planStore = new FakePlanStore();
        var ledger = new FakeOperationLedger();
        var environment = new FakePlatformEnvironmentAdapter();
        var clock = new FakeClock { Value = "2026-04-11T00:00:00Z" };
        var service = new TermForge.Core.Services.ProxyWorkflowService(configStore, planStore, ledger, environment, clock);

        var first = service.PlanEnable("http://one", "", "");
        var second = service.PlanEnable("http://two", "", "");

        Assert.NotEqual(first.Payload.PlanId, second.Payload.PlanId);
    }

    [Fact]
    public void ProxyWorkflowService_GitProxy_can_plan_git_enable_operation()
    {
        var configStore = new FakeConfigStore();
        var planStore = new FakePlanStore();
        var ledger = new FakeOperationLedger();
        var environment = new FakePlatformEnvironmentAdapter();
        var clock = new FakeClock();
        var gitAdapter = new FakeGitProxyAdapter(
            new GitProxySnapshot(true, "global", "", "", ""));

        var service = new TermForge.Core.Services.ProxyWorkflowService(
            configStore,
            planStore,
            ledger,
            environment,
            clock,
            gitAdapter);

        var result = service.PlanGitEnable(
            "http://127.0.0.1:7890",
            "http://127.0.0.1:7890",
            "127.0.0.1,localhost,::1");
        var payload = result.Payload.GetPayload<GitProxyPlan>();

        Assert.Equal("proxy.plan", result.Command);
        Assert.Equal("git", result.Payload.Target);
        Assert.Equal("git", payload.Target);
    }

    [Fact]
    public void ProxyWorkflowService_GitProxy_can_plan_git_disable_operation()
    {
        var configStore = new FakeConfigStore();
        var planStore = new FakePlanStore();
        var ledger = new FakeOperationLedger();
        var environment = new FakePlatformEnvironmentAdapter();
        var clock = new FakeClock();
        var gitAdapter = new FakeGitProxyAdapter(
            new GitProxySnapshot(
                true,
                "global",
                "http://127.0.0.1:7890",
                "http://127.0.0.1:7890",
                "127.0.0.1,localhost,::1"));

        var service = new TermForge.Core.Services.ProxyWorkflowService(
            configStore,
            planStore,
            ledger,
            environment,
            clock,
            gitAdapter);

        var result = service.PlanGitDisable();
        var payload = result.Payload.GetPayload<GitProxyPlan>();

        Assert.Equal("proxy.plan", result.Command);
        Assert.Equal("git", result.Payload.Target);
        Assert.Equal("disable", payload.Mode);
        Assert.Equal(3, payload.Actions.Count);
    }

    [Fact]
    public void ProxyWorkflowService_plans_composite_env_git_enable_in_fixed_order()
    {
        var service = new TermForge.Core.Services.ProxyWorkflowService(
            new FakeConfigStore(),
            new FakePlanStore(),
            new FakeOperationLedger(),
            new FakePlatformEnvironmentAdapter(
                new ProxyConfigSnapshot(true, "http://env:8080", "http://env:8443", "env.local")),
            new FakeClock(),
            new FakeGitProxyAdapter(new GitProxySnapshot(true, "global", "", "", "")));

        var result = service.PlanCompositeEnable(
            "http://127.0.0.1:7890",
            "http://127.0.0.1:7890",
            "127.0.0.1,localhost,::1");

        var payload = result.Payload.GetPayload<CompositeProxyPlan>();

        Assert.Equal("composite", result.Payload.Target);
        Assert.Equal(new[] { "env", "git" }, payload.Targets);
        Assert.Collection(
            payload.Plans,
            plan => Assert.Equal("env", plan.Target),
            plan => Assert.Equal("git", plan.Target));
    }

    [Fact]
    public void ProxyWorkflowService_compensates_env_when_git_apply_fails_in_composite_apply()
    {
        var configStore = new FakeConfigStore();
        var planStore = new FakePlanStore();
        var ledger = new FakeOperationLedger();
        var environment = new FakePlatformEnvironmentAdapter(
            new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty));
        var gitAdapter = new InjectableGitProxyAdapter(new GitProxySnapshot(true, "global", "", "", ""))
        {
            ThrowOnApply = true
        };
        var service = new TermForge.Core.Services.ProxyWorkflowService(
            configStore,
            planStore,
            ledger,
            environment,
            new FakeClock(),
            gitAdapter);

        var plan = service.PlanCompositeEnable(
            "http://127.0.0.1:7890",
            "http://127.0.0.1:7890",
            "127.0.0.1,localhost,::1");

        var error = Assert.Throws<InvalidOperationException>(() => service.Apply(plan.Payload.PlanId));

        Assert.Contains("git", error.Message);
        Assert.False(environment.Current.Enabled);
        Assert.Equal(0, ledger.Count);
    }

    [Fact]
    public void ProxyWorkflowService_applies_composite_env_git_successfully()
    {
        var configStore = new FakeConfigStore();
        var planStore = new FakePlanStore();
        var ledger = new FakeOperationLedger();
        var environment = new FakePlatformEnvironmentAdapter(
            new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty));
        var gitAdapter = new InjectableGitProxyAdapter(new GitProxySnapshot(true, "global", "", "", ""));
        var service = new TermForge.Core.Services.ProxyWorkflowService(
            configStore,
            planStore,
            ledger,
            environment,
            new FakeClock(),
            gitAdapter);

        var plan = service.PlanCompositeEnable(
            "http://127.0.0.1:7890",
            "http://127.0.0.1:7890",
            "127.0.0.1,localhost,::1");

        var apply = service.Apply(plan.Payload.PlanId);
        var payload = apply.Payload.GetAfter<CompositeProxyChange>();

        Assert.Equal("composite", apply.Payload.Target);
        Assert.Equal("composite-proxy-change", apply.Payload.PayloadType);
        Assert.False(payload.RollbackTriggered);
        Assert.Null(payload.FailureTarget);
        Assert.Equal(new[] { "env", "git" }, payload.Targets);
        Assert.Collection(
            payload.Changes,
            change => Assert.Equal("env", change.Target),
            change => Assert.Equal("git", change.Target));
        Assert.True(environment.Current.Enabled);
        Assert.Equal("http://127.0.0.1:7890", gitAdapter.Current.HttpProxy);
        Assert.Equal(1, ledger.Count);
    }

    [Fact]
    public void ProxyWorkflowService_rolls_back_composite_change_in_reverse_order()
    {
        var configStore = new FakeConfigStore();
        var planStore = new FakePlanStore();
        var ledger = new FakeOperationLedger();
        var environment = new FakePlatformEnvironmentAdapter(
            new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty));
        var gitAdapter = new InjectableGitProxyAdapter(new GitProxySnapshot(true, "global", "", "", ""));
        var service = new TermForge.Core.Services.ProxyWorkflowService(
            configStore,
            planStore,
            ledger,
            environment,
            new FakeClock(),
            gitAdapter);

        var plan = service.PlanCompositeEnable(
            "http://127.0.0.1:7890",
            "http://127.0.0.1:7890",
            "127.0.0.1,localhost,::1");

        var apply = service.Apply(plan.Payload.PlanId);
        var rollback = service.Rollback(apply.Payload.ChangeId);
        var payload = rollback.Payload.GetAfter<CompositeProxyChange>();

        Assert.Equal("composite", rollback.Payload.Target);
        Assert.Equal("composite-proxy-change", rollback.Payload.PayloadType);
        Assert.Equal(new[] { "env", "git" }, payload.Targets);
        Assert.Collection(
            payload.Changes,
            change => Assert.Equal("git", change.Target),
            change => Assert.Equal("env", change.Target));
        Assert.False(environment.Current.Enabled);
        Assert.Equal(string.Empty, gitAdapter.Current.HttpProxy);
        Assert.Equal(2, ledger.Count);
    }

    [Fact]
    public void ProxyWorkflowService_applies_and_rolls_back_reloaded_composite_records_from_json_store()
    {
        var root = Path.Combine(Path.GetTempPath(), "termforge-composite-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var configStore = new FakeConfigStore();
            var environment = new FakePlatformEnvironmentAdapter(
                new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty));
            var gitAdapter = new InjectableGitProxyAdapter(new GitProxySnapshot(true, "global", "", "", ""));
            var clock = new FakeClock();
            var planStorePath = Path.Combine(root, "plans.json");
            var ledgerPath = Path.Combine(root, "ledger.json");

            var writerService = new TermForge.Core.Services.ProxyWorkflowService(
                configStore,
                new JsonPlanStore(planStorePath),
                new JsonOperationLedger(ledgerPath),
                environment,
                clock,
                gitAdapter);

            var plan = writerService.PlanCompositeEnable(
                "http://127.0.0.1:7890",
                "http://127.0.0.1:7890",
                "127.0.0.1,localhost,::1");

            var readerService = new TermForge.Core.Services.ProxyWorkflowService(
                configStore,
                new JsonPlanStore(planStorePath),
                new JsonOperationLedger(ledgerPath),
                environment,
                clock,
                gitAdapter);

            var apply = readerService.Apply(plan.Payload.PlanId);
            var applyPayload = apply.Payload.GetAfter<CompositeProxyChange>();

            var rollbackService = new TermForge.Core.Services.ProxyWorkflowService(
                configStore,
                new JsonPlanStore(planStorePath),
                new JsonOperationLedger(ledgerPath),
                environment,
                clock,
                gitAdapter);

            var rollback = rollbackService.Rollback(apply.Payload.ChangeId);
            var rollbackPayload = rollback.Payload.GetAfter<CompositeProxyChange>();

            Assert.Equal("composite", apply.Payload.Target);
            Assert.Collection(
                applyPayload.Changes,
                change => Assert.Equal("env", change.Target),
                change => Assert.Equal("git", change.Target));
            Assert.Equal("composite", rollback.Payload.Target);
            Assert.Collection(
                rollbackPayload.Changes,
                change => Assert.Equal("git", change.Target),
                change => Assert.Equal("env", change.Target));
            Assert.False(environment.Current.Enabled);
            Assert.Equal(string.Empty, gitAdapter.Current.HttpProxy);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ProxyWorkflowService_can_apply_and_rollback_git_through_store_records()
    {
        var configStore = new FakeConfigStore();
        var planStore = new FakePlanStore();
        var ledger = new FakeOperationLedger();
        var environment = new FakePlatformEnvironmentAdapter();
        var clock = new FakeClock();
        var gitAdapter = new FakeGitProxyAdapter(
            new GitProxySnapshot(true, "global", "", "", ""));

        var service = new TermForge.Core.Services.ProxyWorkflowService(
            configStore,
            planStore,
            ledger,
            environment,
            clock,
            gitAdapter);

        var planEnvelope = service.PlanGitEnable(
            "http://127.0.0.1:7890",
            "http://127.0.0.1:7890",
            "127.0.0.1,localhost,::1");

        var applied = service.Apply(planEnvelope.Payload.PlanId);
        var rolledBack = service.Rollback(applied.Payload.ChangeId);
        var appliedSnapshot = applied.Payload.GetAfter<GitProxySnapshot>();
        var rolledBackSnapshot = rolledBack.Payload.GetAfter<GitProxySnapshot>();

        Assert.Equal("proxy.apply", applied.Command);
        Assert.Equal("proxy.rollback", rolledBack.Command);
        Assert.Equal("git", applied.Payload.Target);
        Assert.Equal("git", rolledBack.Payload.Target);
        Assert.Equal("http://127.0.0.1:7890", appliedSnapshot.HttpProxy);
        Assert.Equal(string.Empty, rolledBackSnapshot.HttpProxy);
    }

    [Fact]
    public void ProxyWorkflowService_GitProxy_apply_and_rollback_use_adapter_state()
    {
        var configStore = new FakeConfigStore();
        var planStore = new FakePlanStore();
        var ledger = new FakeOperationLedger();
        var environment = new FakePlatformEnvironmentAdapter();
        var clock = new FakeClock();
        var gitAdapter = new FakeGitProxyAdapter(
            new GitProxySnapshot(
                true,
                "global",
                "http://before:8080",
                "http://before:8443",
                "before.local"));

        var service = new TermForge.Core.Services.ProxyWorkflowService(
            configStore,
            planStore,
            ledger,
            environment,
            clock,
            gitAdapter);

        var plan = service.PlanGitEnable(
            "http://target:7890",
            "http://target:7891",
            "127.0.0.1");
        var gitPlan = plan.Payload.GetPayload<GitProxyPlan>();

        var apply = service.ApplyGit(gitPlan);
        var rollback = service.RollbackGit(gitPlan.Before);

        Assert.Equal("proxy.apply", apply.Command);
        Assert.Equal("http://target:7890", apply.Payload.HttpProxy);
        Assert.Equal("http://target:7891", apply.Payload.HttpsProxy);
        Assert.Equal("proxy.rollback", rollback.Command);
        Assert.Equal("http://before:8080", rollback.Payload.HttpProxy);
        Assert.Equal("http://before:8443", rollback.Payload.HttpsProxy);
    }
}

internal sealed class FakePlanStore : IPlanStore
{
    private readonly Dictionary<string, PlanRecord> _plans = new();

    public PlanRecord? GetPlanRecord(string planId)
    {
        return _plans.TryGetValue(planId, out var plan) ? plan : null;
    }

    public void SavePlanRecord(PlanRecord plan)
    {
        _plans[plan.PlanId] = plan;
    }
}

internal sealed class FakeOperationLedger : IOperationLedger
{
    private readonly Dictionary<string, ChangeRecord> _changes = new();

    public int Count => _changes.Count;

    public ChangeRecord? GetChangeRecord(string changeId)
    {
        return _changes.TryGetValue(changeId, out var change) ? change : null;
    }

    public void AppendChangeRecord(ChangeRecord change)
    {
        _changes[change.ChangeId] = change;
    }
}

internal sealed class FakePlatformEnvironmentAdapter : IPlatformEnvironmentAdapter
{
    public FakePlatformEnvironmentAdapter()
        : this(new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty))
    {
    }

    public FakePlatformEnvironmentAdapter(ProxyConfigSnapshot initial)
    {
        Current = initial;
    }

    public ProxyConfigSnapshot Current { get; private set; }

    public ProxyConfigSnapshot ReadEnvironmentProxy()
    {
        return Current;
    }

    public void ApplyEnvironmentProxy(ProxyConfigSnapshot snapshot)
    {
        Current = snapshot;
    }
}

internal sealed class FakeClock : IClock
{
    public string Value { get; set; } = "2026-04-11T00:00:00Z";

    public string NowText()
    {
        return Value;
    }
}

internal sealed class InjectableGitProxyAdapter : IGitProxyAdapter
{
    public InjectableGitProxyAdapter(GitProxySnapshot current)
    {
        Current = current;
    }

    public GitProxySnapshot Current { get; private set; }

    public bool ThrowOnApply { get; set; }

    public bool ThrowOnVerify { get; set; }

    public bool IsAvailable()
    {
        return Current.Available;
    }

    public GitProxySnapshot ReadCurrent()
    {
        return Current;
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
        if (ThrowOnApply)
        {
            throw new InvalidOperationException("git apply failed");
        }

        Current = plan.Desired;
        return Current;
    }

    public GitProxySnapshot Verify(GitProxySnapshot desired)
    {
        if (ThrowOnVerify)
        {
            throw new InvalidOperationException("git verify failed");
        }

        if (Current.HttpProxy != desired.HttpProxy ||
            Current.HttpsProxy != desired.HttpsProxy ||
            Current.NoProxy != desired.NoProxy)
        {
            throw new InvalidOperationException("git proxy verification failed");
        }

        return Current;
    }

    public GitProxySnapshot Rollback(GitProxySnapshot before)
    {
        Current = before;
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
