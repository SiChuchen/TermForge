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
        var environment = new FakePlatformEnvironmentAdapter();
        var clock = new FakeClock();

        var service = new TermForge.Core.Services.ProxyWorkflowService(configStore, planStore, ledger, environment, clock);

        var result = service.PlanEnable("http://127.0.0.1:7890", "http://127.0.0.1:7890", "127.0.0.1,localhost,::1");

        Assert.Equal("proxy.plan", result.Command);
        Assert.Equal("env", result.Payload.Target);
        Assert.True(result.Payload.Desired.Enabled);
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
    public ProxyConfigSnapshot Current { get; private set; } = new(false, string.Empty, string.Empty, string.Empty);

    public ProxyConfigSnapshot ReadEnvironmentProxy()
    {
        return Current;
    }

    public ProxyConfigSnapshot ApplyEnvironmentProxy(ProxyConfigSnapshot snapshot)
    {
        Current = snapshot;
        return Current;
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
