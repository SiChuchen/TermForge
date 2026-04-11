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

        Assert.Equal("proxy.plan", result.Command);
        Assert.Equal("env", result.Payload.Target);
        Assert.True(result.Payload.Desired.Enabled);
        Assert.Equal("http://env:8080", result.Payload.Before.Http);
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

        Assert.Equal("http://target:7890", afterApply.Http);
        Assert.Equal("http://target:7890", apply.Payload.After.Https);
        Assert.Equal("http://before:8080", rollback.Payload.After.Http);
        Assert.Equal("http://before:8443", rollback.Payload.After.Https);
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
}

internal sealed class FakePlanStore : IPlanStore
{
    private readonly Dictionary<string, ProxyPlanPayload> _plans = new();

    public ProxyPlanPayload? GetPlan(string planId)
    {
        return _plans.TryGetValue(planId, out var plan) ? plan : null;
    }

    public void SavePlan(ProxyPlanPayload plan)
    {
        _plans[plan.PlanId] = plan;
    }
}

internal sealed class FakeOperationLedger : IOperationLedger
{
    private readonly Dictionary<string, ProxyApplyPayload> _changes = new();

    public ProxyApplyPayload? GetChange(string changeId)
    {
        return _changes.TryGetValue(changeId, out var change) ? change : null;
    }

    public void AppendChange(ProxyApplyPayload change)
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
