using System.Collections.Generic;
using TermForge.Contracts;
using TermForge.Core.Interfaces;
using TermForge.Platform;
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
