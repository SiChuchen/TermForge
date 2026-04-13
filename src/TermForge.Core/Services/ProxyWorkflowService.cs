using TermForge.Contracts;
using TermForge.Core.Interfaces;
using TermForge.Platform;

namespace TermForge.Core.Services;

public sealed class ProxyWorkflowService
{
    private const string UnifiedSchemaVersion = "2026-04-13";
    private readonly IClock _clock;
    private readonly IConfigStore _configStore;
    private readonly IGitProxyAdapter? _gitAdapter;
    private readonly IOperationLedger _ledger;
    private readonly IPlanStore _planStore;
    private readonly IPlatformEnvironmentAdapter _environmentAdapter;

    public ProxyWorkflowService(
        IConfigStore configStore,
        IPlanStore planStore,
        IOperationLedger ledger,
        IPlatformEnvironmentAdapter environmentAdapter,
        IClock clock,
        IGitProxyAdapter? gitAdapter = null)
    {
        _configStore = configStore;
        _planStore = planStore;
        _ledger = ledger;
        _environmentAdapter = environmentAdapter;
        _clock = clock;
        _gitAdapter = gitAdapter;
    }

    public CommandEnvelope<ProxyScanPayload> Scan()
    {
        var payload = new ProxyScanPayload("env", _environmentAdapter.ReadEnvironmentProxy());
        return Envelope("proxy.scan", payload);
    }

    public CommandEnvelope<PlanRecord> PlanEnable(string httpProxy, string httpsProxy, string noProxy)
    {
        var current = _environmentAdapter.ReadEnvironmentProxy();
        var desired = NormalizeSnapshot(new ProxyConfigSnapshot(true, httpProxy, httpsProxy, noProxy));
        var payload = new ProxyPlanPayload(CreateId("plan"), "env", "enable", current, desired);
        var record = CreatePlanRecord(payload.PlanId, payload.Target, "proxy-plan", payload);
        _planStore.SavePlanRecord(record);
        return Envelope("proxy.plan", record);
    }

    public CommandEnvelope<PlanRecord> PlanDisable()
    {
        var current = _environmentAdapter.ReadEnvironmentProxy();
        var desired = new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty);
        var payload = new ProxyPlanPayload(CreateId("plan"), "env", "disable", current, desired);
        var record = CreatePlanRecord(payload.PlanId, payload.Target, "proxy-plan", payload);
        _planStore.SavePlanRecord(record);
        return Envelope("proxy.plan", record);
    }

    public CommandEnvelope<PlanRecord> PlanCompositeEnable(string httpProxy, string httpsProxy, string noProxy)
    {
        var planId = CreateId("plan");
        var envPlan = new ProxyPlanPayload(
            planId,
            "env",
            "enable",
            _environmentAdapter.ReadEnvironmentProxy(),
            NormalizeSnapshot(new ProxyConfigSnapshot(true, httpProxy, httpsProxy, noProxy)));
        var gitPlan = GetGitAdapter().PlanEnable(httpProxy, httpsProxy, noProxy);
        var composite = new CompositeProxyPlan(
            ["env", "git"],
            "enable",
            [
                new CompositeTargetPlan("env", "proxy-plan", envPlan),
                new CompositeTargetPlan("git", "git-proxy-plan", gitPlan)
            ]);
        var record = CreatePlanRecord(planId, "composite", "composite-proxy-plan", composite);
        _planStore.SavePlanRecord(record);
        return Envelope("proxy.plan", record);
    }

    public CommandEnvelope<ChangeRecord> Apply(string planId)
    {
        var plan = _planStore.GetPlanRecord(planId) ?? throw new InvalidOperationException($"Plan not found: {planId}");
        var change = plan.Target switch
        {
            "env" => ApplyEnvironmentPlan(plan),
            "git" => ApplyGitPlan(plan),
            _ => throw new InvalidOperationException($"Unsupported plan target: {plan.Target}")
        };

        return Envelope("proxy.apply", change);
    }

    public CommandEnvelope<ChangeRecord> Rollback(string changeId)
    {
        var change = _ledger.GetChangeRecord(changeId) ?? throw new InvalidOperationException($"Change not found: {changeId}");
        var rollback = change.Target switch
        {
            "env" => RollbackEnvironmentChange(change),
            "git" => RollbackGitChange(change),
            _ => throw new InvalidOperationException($"Unsupported change target: {change.Target}")
        };

        return Envelope("proxy.rollback", rollback);
    }

    public CommandEnvelope<PlanRecord> PlanGitEnable(string httpProxy, string httpsProxy, string noProxy)
    {
        var payload = GetGitAdapter().PlanEnable(httpProxy, httpsProxy, noProxy);
        var record = CreatePlanRecord(CreateId("plan"), payload.Target, "git-proxy-plan", payload);
        _planStore.SavePlanRecord(record);
        return Envelope("proxy.plan", record);
    }

    public CommandEnvelope<PlanRecord> PlanGitDisable()
    {
        var payload = GetGitAdapter().PlanDisable();
        var record = CreatePlanRecord(CreateId("plan"), payload.Target, "git-proxy-plan", payload);
        _planStore.SavePlanRecord(record);
        return Envelope("proxy.plan", record);
    }

    public CommandEnvelope<GitProxySnapshot> ApplyGit(GitProxyPlan plan)
    {
        var adapter = GetGitAdapter();
        adapter.Apply(plan);
        var payload = adapter.Verify(plan.Desired);
        return Envelope("proxy.apply", payload);
    }

    public CommandEnvelope<GitProxySnapshot> RollbackGit(GitProxySnapshot before)
    {
        var payload = GetGitAdapter().Rollback(before);
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

    private ChangeRecord ApplyEnvironmentPlan(PlanRecord record)
    {
        var plan = record.ToProxyPlanPayload();
        _environmentAdapter.ApplyEnvironmentProxy(plan.Desired);
        var applied = NormalizeSnapshot(_environmentAdapter.ReadEnvironmentProxy());
        _configStore.WriteProxyConfig(applied);

        var change = CreateChangeRecord(
            plan.Target,
            plan.PlanId,
            "proxy-apply",
            plan.Before,
            applied);

        _ledger.AppendChangeRecord(change);
        return change;
    }

    private ChangeRecord ApplyGitPlan(PlanRecord record)
    {
        var adapter = GetGitAdapter();
        var plan = record.ToGitProxyPlan();
        adapter.Apply(plan);
        var applied = adapter.Verify(plan.Desired);

        var change = CreateChangeRecord(
            plan.Target,
            record.PlanId,
            "git-proxy-apply",
            plan.Before,
            applied);

        _ledger.AppendChangeRecord(change);
        return change;
    }

    private ChangeRecord RollbackEnvironmentChange(ChangeRecord change)
    {
        _ = _planStore.GetPlanRecord(change.PlanId) ?? throw new InvalidOperationException($"Plan not found: {change.PlanId}");
        var beforeRollback = NormalizeSnapshot(_environmentAdapter.ReadEnvironmentProxy());
        _environmentAdapter.ApplyEnvironmentProxy(change.GetBefore<ProxyConfigSnapshot>());
        var reverted = NormalizeSnapshot(_environmentAdapter.ReadEnvironmentProxy());
        _configStore.WriteProxyConfig(reverted);

        var rollback = CreateChangeRecord(
            change.Target,
            change.PlanId,
            "proxy-rollback",
            beforeRollback,
            reverted);

        _ledger.AppendChangeRecord(rollback);
        return rollback;
    }

    private ChangeRecord RollbackGitChange(ChangeRecord change)
    {
        _ = _planStore.GetPlanRecord(change.PlanId) ?? throw new InvalidOperationException($"Plan not found: {change.PlanId}");
        var adapter = GetGitAdapter();
        var beforeRollback = adapter.ReadCurrent();
        var reverted = adapter.Rollback(change.GetBefore<GitProxySnapshot>());

        var rollback = CreateChangeRecord(
            change.Target,
            change.PlanId,
            "git-proxy-rollback",
            beforeRollback,
            reverted);

        _ledger.AppendChangeRecord(rollback);
        return rollback;
    }

    private IGitProxyAdapter GetGitAdapter()
    {
        return _gitAdapter ?? throw new InvalidOperationException("Git proxy adapter is not configured.");
    }

    private PlanRecord CreatePlanRecord(string planId, string target, string payloadType, object payload)
    {
        return new PlanRecord(planId, target, UnifiedSchemaVersion, _clock.NowText(), payloadType, payload);
    }

    private ChangeRecord CreateChangeRecord(string target, string planId, string payloadType, object before, object after)
    {
        return new ChangeRecord(CreateId("change"), target, planId, UnifiedSchemaVersion, _clock.NowText(), payloadType, before, after);
    }

    private static ProxyConfigSnapshot NormalizeSnapshot(ProxyConfigSnapshot snapshot)
    {
        var http = snapshot.Http.Trim();
        var https = string.IsNullOrWhiteSpace(snapshot.Https) ? http : snapshot.Https.Trim();
        var noProxy = snapshot.NoProxy.Trim();
        return new ProxyConfigSnapshot(snapshot.Enabled, http, https, noProxy);
    }
}
