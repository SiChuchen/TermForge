using System.Linq;
using TermForge.Contracts;
using TermForge.Core.Interfaces;
using TermForge.Platform;

namespace TermForge.Core.Services;

public sealed class ProxyWorkflowService
{
    private const string UnifiedSchemaVersion = "2026-04-13";
    private static readonly string[] CompositeApplyOrder = ["env", "git", "npm", "pip"];
    private static readonly string[] CompositeRollbackOrder = ["git", "env", "npm", "pip"];
    private readonly IClock _clock;
    private readonly IConfigStore _configStore;
    private readonly IGitProxyAdapter? _gitAdapter;
    private readonly IOperationLedger _ledger;
    private readonly IPlanStore _planStore;
    private readonly IPlatformEnvironmentAdapter _environmentAdapter;
    private readonly Dictionary<string, IProxyTargetAdapter> _targetAdapters;

    public ProxyWorkflowService(
        IConfigStore configStore,
        IPlanStore planStore,
        IOperationLedger ledger,
        IPlatformEnvironmentAdapter environmentAdapter,
        IClock clock,
        IGitProxyAdapter? gitAdapter = null,
        IProxyTargetAdapter? npmProxyAdapter = null,
        IProxyTargetAdapter? pipProxyAdapter = null)
    {
        _configStore = configStore;
        _planStore = planStore;
        _ledger = ledger;
        _environmentAdapter = environmentAdapter;
        _clock = clock;
        _gitAdapter = gitAdapter;
        _targetAdapters = new Dictionary<string, IProxyTargetAdapter>(StringComparer.OrdinalIgnoreCase);
        if (npmProxyAdapter is not null)
        {
            _targetAdapters["npm"] = npmProxyAdapter;
        }

        if (pipProxyAdapter is not null)
        {
            _targetAdapters["pip"] = pipProxyAdapter;
        }
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
        var flags = _configStore.GetProxyTargetFlags();
        var desired = NormalizeSnapshot(new ProxyConfigSnapshot(true, httpProxy, httpsProxy, noProxy));
        var plans = new List<CompositeTargetPlan>();

        if (flags.Env)
        {
            var envPlan = new ProxyPlanPayload(planId, "env", "enable", _environmentAdapter.ReadEnvironmentProxy(), desired);
            plans.Add(new CompositeTargetPlan("env", "proxy-plan", envPlan));
        }

        if (flags.Git && _gitAdapter is not null)
        {
            var gitPlan = _gitAdapter.PlanEnable(httpProxy, httpsProxy, noProxy);
            plans.Add(new CompositeTargetPlan("git", "git-proxy-plan", gitPlan));
        }

        foreach (var (target, adapter) in _targetAdapters)
        {
            bool isFlagEnabled = target switch { "npm" => flags.Npm, "pip" => flags.Pip, _ => false };
            if (isFlagEnabled && adapter.IsAvailable())
            {
                var before = adapter.ReadCurrent();
                var targetDesired = adapter.PlanEnable(httpProxy, httpsProxy, noProxy);
                plans.Add(new CompositeTargetPlan(target, $"{target}-proxy-plan", new TargetProxyPlanPayload(before, targetDesired)));
            }
        }

        var targets = plans.Select(p => p.Target).ToArray();
        var composite = new CompositeProxyPlan(targets, "enable", plans);
        var record = CreatePlanRecord(planId, "composite", "composite-proxy-plan", composite);
        _planStore.SavePlanRecord(record);
        return Envelope("proxy.plan", record);
    }

    public CommandEnvelope<PlanRecord> PlanCompositeDisable()
    {
        var planId = CreateId("plan");
        var flags = _configStore.GetProxyTargetFlags();
        var plans = new List<CompositeTargetPlan>();

        if (flags.Env)
        {
            var envPlan = new ProxyPlanPayload(planId, "env", "disable", _environmentAdapter.ReadEnvironmentProxy(), new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty));
            plans.Add(new CompositeTargetPlan("env", "proxy-plan", envPlan));
        }

        if (flags.Git && _gitAdapter is not null)
        {
            var gitPlan = _gitAdapter.PlanDisable();
            plans.Add(new CompositeTargetPlan("git", "git-proxy-plan", gitPlan));
        }

        foreach (var (target, adapter) in _targetAdapters)
        {
            bool isFlagEnabled = target switch { "npm" => flags.Npm, "pip" => flags.Pip, _ => false };
            if (isFlagEnabled && adapter.IsAvailable())
            {
                var before = adapter.ReadCurrent();
                var targetDesired = adapter.PlanDisable();
                plans.Add(new CompositeTargetPlan(target, $"{target}-proxy-plan", new TargetProxyPlanPayload(before, targetDesired)));
            }
        }

        var targets = plans.Select(p => p.Target).ToArray();
        var composite = new CompositeProxyPlan(targets, "disable", plans);
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
            "composite" => ApplyCompositePlan(plan),
            "npm" or "pip" => ApplyTargetPlan(plan),
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
            "composite" => RollbackCompositeChange(change),
            "npm" or "pip" => RollbackTargetChange(change),
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

    public CommandEnvelope<ProxyScanPayload> ScanTarget(string target)
    {
        var adapter = GetTargetAdapter(target);
        var config = adapter.ReadCurrent();
        var payload = new ProxyScanPayload(target, config);
        return Envelope("proxy.scan", payload);
    }

    public CommandEnvelope<PlanRecord> PlanTargetEnable(string target, string httpProxy, string httpsProxy, string noProxy)
    {
        var adapter = GetTargetAdapter(target);
        var before = adapter.ReadCurrent();
        var desired = adapter.PlanEnable(httpProxy, httpsProxy, noProxy);
        var planPayload = new TargetProxyPlanPayload(before, desired);
        var record = CreatePlanRecord(CreateId("plan"), target, "target-proxy-plan", planPayload);
        _planStore.SavePlanRecord(record);
        return Envelope("proxy.plan", record);
    }

    public CommandEnvelope<PlanRecord> PlanTargetDisable(string target)
    {
        var adapter = GetTargetAdapter(target);
        var before = adapter.ReadCurrent();
        var desired = adapter.PlanDisable();
        var planPayload = new TargetProxyPlanPayload(before, desired);
        var record = CreatePlanRecord(CreateId("plan"), target, "target-proxy-plan", planPayload);
        _planStore.SavePlanRecord(record);
        return Envelope("proxy.plan", record);
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

    private ChangeRecord ApplyCompositePlan(PlanRecord record)
    {
        var compositePlan = record.ToCompositeProxyPlan();
        var orderedPlans = OrderCompositePlans(compositePlan);
        var appliedChanges = new List<CompositeTargetChange>();

        foreach (var targetPlan in orderedPlans)
        {
            try
            {
                var targetChange = targetPlan.Target switch
                {
                    "env" => ApplyCompositeEnvironmentPlan(targetPlan.ToProxyPlanPayload()),
                    "git" => ApplyCompositeGitPlan(targetPlan.ToGitProxyPlan()),
                    "npm" or "pip" => ApplyCompositeTargetPlan(targetPlan.Target, targetPlan.GetPayload<TargetProxyPlanPayload>()),
                    _ => throw new InvalidOperationException($"Unsupported composite target: {targetPlan.Target}")
                };

                appliedChanges.Add(targetChange);
            }
            catch (Exception ex)
            {
                try
                {
                    CompensateCompositeChanges(appliedChanges);
                }
                catch (Exception compensationEx)
                {
                    throw new InvalidOperationException(
                        $"Composite apply failed at {targetPlan.Target}: {ex.Message}. Compensation rollback failed: {compensationEx.Message}",
                        compensationEx);
                }

                throw new InvalidOperationException($"Composite apply failed at {targetPlan.Target}: {ex.Message}", ex);
            }
        }

        var payload = new CompositeProxyChange(CompositeApplyOrder, compositePlan.Mode, appliedChanges, false, null);
        var change = CreateCompositeChangeRecord(record.PlanId, payload);
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

    private ChangeRecord RollbackCompositeChange(ChangeRecord change)
    {
        _ = _planStore.GetPlanRecord(change.PlanId) ?? throw new InvalidOperationException($"Plan not found: {change.PlanId}");
        var compositeChange = change.GetAfter<CompositeProxyChange>();
        var rollbackChanges = new List<CompositeTargetChange>();

        foreach (var target in CompositeRollbackOrder)
        {
            var targetChange = compositeChange.Changes.FirstOrDefault(entry => string.Equals(entry.Target, target, StringComparison.Ordinal));
            if (targetChange is null)
            {
                continue;
            }

            var rollbackChange = target switch
            {
                "env" => RollbackCompositeEnvironment(targetChange),
                "git" => RollbackCompositeGit(targetChange),
                "npm" or "pip" => RollbackCompositeTarget(targetChange),
                _ => throw new InvalidOperationException($"Unsupported composite target: {target}")
            };

            rollbackChanges.Add(rollbackChange);
        }

        var payload = new CompositeProxyChange(compositeChange.Targets, compositeChange.Mode, rollbackChanges, false, null);
        var rollback = CreateCompositeChangeRecord(change.PlanId, payload);
        _ledger.AppendChangeRecord(rollback);
        return rollback;
    }

    private IGitProxyAdapter GetGitAdapter()
    {
        return _gitAdapter ?? throw new InvalidOperationException("Git proxy adapter is not configured.");
    }

    private IProxyTargetAdapter GetTargetAdapter(string target)
    {
        return _targetAdapters.TryGetValue(target, out var adapter)
            ? adapter
            : throw new InvalidOperationException($"Target adapter is not configured: {target}");
    }

    private ChangeRecord ApplyTargetPlan(PlanRecord record)
    {
        var adapter = GetTargetAdapter(record.Target);
        var plan = record.GetPayload<TargetProxyPlanPayload>();
        var applied = adapter.Apply(plan.Desired);
        adapter.Verify(plan.Desired);

        var change = CreateChangeRecord(
            record.Target,
            record.PlanId,
            "target-proxy-apply",
            plan.Before,
            applied);

        _ledger.AppendChangeRecord(change);
        return change;
    }

    private ChangeRecord RollbackTargetChange(ChangeRecord change)
    {
        _ = _planStore.GetPlanRecord(change.PlanId) ?? throw new InvalidOperationException($"Plan not found: {change.PlanId}");
        var adapter = GetTargetAdapter(change.Target);
        var beforeRollback = adapter.ReadCurrent();
        var before = change.GetBefore<ProxyConfigSnapshot>();
        var reverted = adapter.Rollback(before);
        adapter.Verify(before);

        var rollback = CreateChangeRecord(
            change.Target,
            change.PlanId,
            "target-proxy-rollback",
            beforeRollback,
            reverted);

        _ledger.AppendChangeRecord(rollback);
        return rollback;
    }

    private PlanRecord CreatePlanRecord(string planId, string target, string payloadType, object payload)
    {
        return new PlanRecord(planId, target, UnifiedSchemaVersion, _clock.NowText(), payloadType, payload);
    }

    private ChangeRecord CreateChangeRecord(string target, string planId, string payloadType, object before, object after)
    {
        return new ChangeRecord(CreateId("change"), target, planId, UnifiedSchemaVersion, _clock.NowText(), payloadType, before, after);
    }

    private ChangeRecord CreateCompositeChangeRecord(string planId, CompositeProxyChange payload)
    {
        return CreateChangeRecord("composite", planId, "composite-proxy-change", payload, payload);
    }

    private static ProxyConfigSnapshot NormalizeSnapshot(ProxyConfigSnapshot snapshot)
    {
        var http = snapshot.Http.Trim();
        var https = string.IsNullOrWhiteSpace(snapshot.Https) ? http : snapshot.Https.Trim();
        var noProxy = snapshot.NoProxy.Trim();
        return new ProxyConfigSnapshot(snapshot.Enabled, http, https, noProxy);
    }

    private static IReadOnlyList<CompositeTargetPlan> OrderCompositePlans(CompositeProxyPlan compositePlan)
    {
        return CompositeApplyOrder
            .Select(target => compositePlan.Plans.FirstOrDefault(plan => string.Equals(plan.Target, target, StringComparison.Ordinal)))
            .Where(plan => plan is not null)
            .Cast<CompositeTargetPlan>()
            .ToArray();
    }

    private CompositeTargetChange ApplyCompositeEnvironmentPlan(ProxyPlanPayload plan)
    {
        try
        {
            _environmentAdapter.ApplyEnvironmentProxy(plan.Desired);
            var applied = NormalizeSnapshot(_environmentAdapter.ReadEnvironmentProxy());
            VerifyEnvironmentSnapshot(plan.Desired, applied, "apply");
            _configStore.WriteProxyConfig(applied);
            return new CompositeTargetChange("env", "proxy-config-snapshot", plan.Before, applied);
        }
        catch
        {
            RevertEnvironment(plan.Before);
            throw;
        }
    }

    private CompositeTargetChange ApplyCompositeGitPlan(GitProxyPlan plan)
    {
        var adapter = GetGitAdapter();

        try
        {
            adapter.Apply(plan);
            var applied = adapter.Verify(plan.Desired);
            return new CompositeTargetChange("git", "git-proxy-snapshot", plan.Before, applied);
        }
        catch
        {
            adapter.Rollback(plan.Before);
            adapter.Verify(plan.Before);
            throw;
        }
    }

    private CompositeTargetChange ApplyCompositeTargetPlan(string target, TargetProxyPlanPayload plan)
    {
        var adapter = GetTargetAdapter(target);

        try
        {
            var applied = adapter.Apply(plan.Desired);
            adapter.Verify(plan.Desired);
            return new CompositeTargetChange(target, "proxy-config-snapshot", plan.Before, applied);
        }
        catch
        {
            adapter.Rollback(plan.Before);
            adapter.Verify(plan.Before);
            throw;
        }
    }

    private void CompensateCompositeChanges(IReadOnlyList<CompositeTargetChange> appliedChanges)
    {
        for (var index = appliedChanges.Count - 1; index >= 0; index--)
        {
            var change = appliedChanges[index];
            _ = change.Target switch
            {
                "env" => RollbackCompositeEnvironment(change),
                "git" => RollbackCompositeGit(change),
                "npm" or "pip" => RollbackCompositeTarget(change),
                _ => throw new InvalidOperationException($"Unsupported composite target: {change.Target}")
            };
        }
    }

    private CompositeTargetChange RollbackCompositeEnvironment(CompositeTargetChange change)
    {
        var beforeRollback = NormalizeSnapshot(_environmentAdapter.ReadEnvironmentProxy());
        _environmentAdapter.ApplyEnvironmentProxy(change.ToProxyBeforeSnapshot());
        var reverted = NormalizeSnapshot(_environmentAdapter.ReadEnvironmentProxy());
        VerifyEnvironmentSnapshot(change.ToProxyBeforeSnapshot(), reverted, "rollback");
        _configStore.WriteProxyConfig(reverted);
        return new CompositeTargetChange("env", "proxy-config-snapshot", beforeRollback, reverted);
    }

    private CompositeTargetChange RollbackCompositeGit(CompositeTargetChange change)
    {
        var adapter = GetGitAdapter();
        var beforeRollback = adapter.ReadCurrent();
        var reverted = adapter.Rollback(change.ToGitBeforeSnapshot());
        var verified = adapter.Verify(change.ToGitBeforeSnapshot());
        return new CompositeTargetChange("git", "git-proxy-snapshot", beforeRollback, verified);
    }

    private CompositeTargetChange RollbackCompositeTarget(CompositeTargetChange change)
    {
        var adapter = GetTargetAdapter(change.Target);
        var beforeRollback = adapter.ReadCurrent();
        var before = change.GetBefore<ProxyConfigSnapshot>();
        var reverted = adapter.Rollback(before);
        adapter.Verify(before);
        return new CompositeTargetChange(change.Target, "proxy-config-snapshot", beforeRollback, reverted);
    }

    private void RevertEnvironment(ProxyConfigSnapshot snapshot)
    {
        _environmentAdapter.ApplyEnvironmentProxy(snapshot);
        var reverted = NormalizeSnapshot(_environmentAdapter.ReadEnvironmentProxy());
        VerifyEnvironmentSnapshot(snapshot, reverted, "revert");
        _configStore.WriteProxyConfig(reverted);
    }

    private static void VerifyEnvironmentSnapshot(ProxyConfigSnapshot desired, ProxyConfigSnapshot actual, string operation)
    {
        if (NormalizeSnapshot(desired) != NormalizeSnapshot(actual))
        {
            throw new InvalidOperationException($"env proxy verification failed after {operation}");
        }
    }
}
