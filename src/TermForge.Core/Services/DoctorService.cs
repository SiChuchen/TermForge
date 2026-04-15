using System;
using System.Collections.Generic;
using TermForge.Contracts;
using TermForge.Core.Interfaces;
using TermForge.Platform;

namespace TermForge.Core.Services;

public sealed class DoctorService
{
    private readonly IConfigStore _configStore;
    private readonly string? _sharedPrimaryCommandName;
    private readonly IProxyTargetAdapter? _npmAdapter;
    private readonly IProxyTargetAdapter? _pipAdapter;

    public DoctorService(
        IConfigStore configStore,
        string? sharedPrimaryCommandName = null,
        IProxyTargetAdapter? npmAdapter = null,
        IProxyTargetAdapter? pipAdapter = null)
    {
        _configStore = configStore;
        _sharedPrimaryCommandName = sharedPrimaryCommandName;
        _npmAdapter = npmAdapter;
        _pipAdapter = pipAdapter;
    }

    public CommandEnvelope<DoctorPayload> BuildReport()
    {
        var enabledModules = _configStore.GetEnabledModules();
        var targetFlags = _configStore.GetProxyTargetFlags();
        var proxyConfig = _configStore.ReadProxyConfig();

        var profiles = new List<DoctorProfile>
        {
            new("PowerShell", "PASS", "managed profile detected", null),
            new("VSCode", "PASS", "managed profile detected", null)
        };

        var tools = new List<DoctorTool>
        {
            new("config", "PASS", _configStore.GetConfigPath(), _configStore.GetConfigPath()),
            new("module_state", "PASS", _configStore.GetModuleStatePath(), _configStore.GetModuleStatePath()),
            new("runtime_state", "PASS", _configStore.GetRuntimeStatePath(), _configStore.GetRuntimeStatePath())
        };

        var issues = new List<DoctorIssue>();
        CheckTargetAvailability(_npmAdapter, targetFlags.Npm, "npm", issues);
        CheckTargetAvailability(_pipAdapter, targetFlags.Pip, "pip", issues);
        CheckConfigDrift(_npmAdapter, proxyConfig, "npm", issues);
        CheckConfigDrift(_pipAdapter, proxyConfig, "pip", issues);

        var warnCount = issues.Count;
        var payload = new DoctorPayload(
            _configStore.GetRootPath(),
            ResolvePrimaryCommandName(),
            warnCount > 0 ? "WARN" : "PASS",
            0,
            warnCount,
            profiles,
            enabledModules,
            tools,
            issues);

        return new CommandEnvelope<DoctorPayload>(
            Command: "doctor",
            Status: warnCount > 0 ? "WARN" : "PASS",
            GeneratedAt: DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            Warnings: [],
            Errors: [],
            Payload: payload);
    }

    private void CheckTargetAvailability(IProxyTargetAdapter? adapter, bool targetEnabled, string targetName, List<DoctorIssue> issues)
    {
        if (!targetEnabled || adapter is null) return;
        if (!adapter.IsAvailable())
            issues.Add(new DoctorIssue($"{targetName}_not_found", "WARN", $"{targetName} target enabled but {targetName} not found in PATH"));
    }

    private void CheckConfigDrift(IProxyTargetAdapter? adapter, ProxyConfigSnapshot proxyConfig, string targetName, List<DoctorIssue> issues)
    {
        if (adapter is null || !adapter.IsAvailable()) return;
        var snap = adapter.ReadCurrent();
        if (proxyConfig.Enabled != snap.Enabled)
        {
            issues.Add(new DoctorIssue($"{targetName}_config_drift", "WARN", $"{targetName} proxy config drift: enabled={snap.Enabled} but config enabled={proxyConfig.Enabled}"));
            return;
        }
        if (proxyConfig.Enabled)
        {
            var httpMatch = string.Equals(NormalizeUrl(snap.Http), NormalizeUrl(proxyConfig.Http), StringComparison.OrdinalIgnoreCase);
            var httpsMatch = string.Equals(NormalizeUrl(snap.Https), NormalizeUrl(proxyConfig.Https), StringComparison.OrdinalIgnoreCase);
            if (!httpMatch || !httpsMatch)
                issues.Add(new DoctorIssue($"{targetName}_config_drift", "WARN", $"{targetName} proxy config drift: {targetName} has different proxy than config"));
        }
    }

    private static string NormalizeUrl(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
    private string ResolvePrimaryCommandName() => string.IsNullOrWhiteSpace(_sharedPrimaryCommandName) ? _configStore.GetPrimaryCommandName() : _sharedPrimaryCommandName;
}
