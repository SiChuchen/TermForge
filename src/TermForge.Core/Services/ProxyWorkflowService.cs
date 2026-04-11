using TermForge.Contracts;
using TermForge.Core.Interfaces;
using TermForge.Platform;

namespace TermForge.Core.Services;

public sealed class ProxyWorkflowService
{
    private readonly IClock _clock;
    private readonly IConfigStore _configStore;
    private readonly IOperationLedger _ledger;
    private readonly IPlanStore _planStore;
    private readonly IPlatformEnvironmentAdapter _environmentAdapter;

    public ProxyWorkflowService(
        IConfigStore configStore,
        IPlanStore planStore,
        IOperationLedger ledger,
        IPlatformEnvironmentAdapter environmentAdapter,
        IClock clock)
    {
        _configStore = configStore;
        _planStore = planStore;
        _ledger = ledger;
        _environmentAdapter = environmentAdapter;
        _clock = clock;
    }

    public CommandEnvelope<ProxyScanPayload> Scan()
    {
        var payload = new ProxyScanPayload("env", _environmentAdapter.ReadEnvironmentProxy());
        return Envelope("proxy.scan", payload);
    }

    public CommandEnvelope<ProxyPlanPayload> PlanEnable(string httpProxy, string httpsProxy, string noProxy)
    {
        var current = _environmentAdapter.ReadEnvironmentProxy();
        var desired = NormalizeSnapshot(new ProxyConfigSnapshot(true, httpProxy, httpsProxy, noProxy));
        var payload = new ProxyPlanPayload(CreateId("plan"), "env", "enable", current, desired);
        _planStore.SavePlan(payload);
        return Envelope("proxy.plan", payload);
    }

    public CommandEnvelope<ProxyApplyPayload> Apply(string planId)
    {
        var plan = _planStore.GetPlan(planId) ?? throw new InvalidOperationException($"Plan not found: {planId}");
        _environmentAdapter.ApplyEnvironmentProxy(plan.Desired);
        var applied = NormalizeSnapshot(_environmentAdapter.ReadEnvironmentProxy());
        _configStore.WriteProxyConfig(applied);

        var payload = new ProxyApplyPayload(CreateId("change"), plan.PlanId, plan.Target, applied);
        _ledger.AppendChange(payload);
        return Envelope("proxy.apply", payload);
    }

    public CommandEnvelope<ProxyApplyPayload> Rollback(string changeId)
    {
        var change = _ledger.GetChange(changeId) ?? throw new InvalidOperationException($"Change not found: {changeId}");
        var plan = _planStore.GetPlan(change.PlanId) ?? throw new InvalidOperationException($"Plan not found: {change.PlanId}");
        _environmentAdapter.ApplyEnvironmentProxy(plan.Before);
        var reverted = NormalizeSnapshot(_environmentAdapter.ReadEnvironmentProxy());
        _configStore.WriteProxyConfig(reverted);

        var payload = new ProxyApplyPayload(CreateId("change"), plan.PlanId, change.Target, reverted);
        _ledger.AppendChange(payload);
        return Envelope("proxy.rollback", payload);
    }

    private CommandEnvelope<TPayload> Envelope<TPayload>(string command, TPayload payload)
    {
        return new CommandEnvelope<TPayload>(
            Command: command,
            Status: "PASS",
            GeneratedAt: _clock.NowText(),
            Warnings: [],
            Errors: [],
            Payload: payload);
    }

    private string CreateId(string prefix)
    {
        return $"{prefix}-{_clock.NowText()}-{Guid.NewGuid():N}";
    }

    private static ProxyConfigSnapshot NormalizeSnapshot(ProxyConfigSnapshot snapshot)
    {
        var http = snapshot.Http.Trim();
        var https = string.IsNullOrWhiteSpace(snapshot.Https) ? http : snapshot.Https.Trim();
        var noProxy = snapshot.NoProxy.Trim();
        return new ProxyConfigSnapshot(snapshot.Enabled, http, https, noProxy);
    }
}
