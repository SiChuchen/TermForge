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
    private readonly EnvironmentHostFacts? _hostFacts;
    private readonly IReadOnlyList<EnvironmentToolFact>? _toolFacts;
    private readonly EnvironmentProxyFact? _proxyEnvFact;

    public DoctorService(
        IConfigStore configStore,
        string? sharedPrimaryCommandName = null,
        IProxyTargetAdapter? npmAdapter = null,
        IProxyTargetAdapter? pipAdapter = null,
        EnvironmentHostFacts? hostFacts = null,
        IReadOnlyList<EnvironmentToolFact>? toolFacts = null,
        EnvironmentProxyFact? proxyEnvFact = null)
    {
        _configStore = configStore;
        _sharedPrimaryCommandName = sharedPrimaryCommandName;
        _npmAdapter = npmAdapter;
        _pipAdapter = pipAdapter;
        _hostFacts = hostFacts;
        _toolFacts = toolFacts;
        _proxyEnvFact = proxyEnvFact;
    }

    public CommandEnvelope<DoctorPayload> BuildReport()
    {
        var enabledModules = _configStore.GetEnabledModules();
        var targetFlags = _configStore.GetProxyTargetFlags();
        var proxyConfig = _configStore.ReadProxyConfig();

        var profiles = BuildProfiles();
        var tools = BuildTools();

        var issues = new List<DoctorIssue>();
        CheckToolAvailability(issues);
        CheckTargetAvailability(_npmAdapter, targetFlags.Npm, "npm", issues);
        CheckTargetAvailability(_pipAdapter, targetFlags.Pip, "pip", issues);
        CheckConfigDrift(_npmAdapter, proxyConfig, "npm", issues);
        CheckConfigDrift(_pipAdapter, proxyConfig, "pip", issues);

        var warnCount = issues.Count(i => i.Status == "WARN");
        var failCount = issues.Count(i => i.Status == "FAIL");
        var overallStatus = failCount > 0 ? "FAIL" : warnCount > 0 ? "WARN" : "PASS";

        var payload = new DoctorPayload(
            _configStore.GetRootPath(),
            ResolvePrimaryCommandName(),
            overallStatus,
            failCount,
            warnCount,
            profiles,
            enabledModules,
            tools,
            issues);

        return new CommandEnvelope<DoctorPayload>(
            Command: "doctor",
            Status: overallStatus,
            GeneratedAt: DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            Warnings: [],
            Errors: [],
            Payload: payload);
    }

    private List<DoctorProfile> BuildProfiles()
    {
        var profiles = new List<DoctorProfile>();
        if (_hostFacts is not null)
        {
            profiles.Add(new DoctorProfile("PowerShell", "PASS",
                $"{_hostFacts.PowerShellEdition} {_hostFacts.PowerShellVersion}", null));
            profiles.Add(new DoctorProfile("OS", "PASS",
                _hostFacts.OsVersion, null));
        }
        else
        {
            profiles.Add(new DoctorProfile("PowerShell", "PASS", "managed profile detected", null));
            profiles.Add(new DoctorProfile("VSCode", "PASS", "managed profile detected", null));
        }
        return profiles;
    }

    private List<DoctorTool> BuildTools()
    {
        var tools = new List<DoctorTool>();
        if (_toolFacts is not null && _toolFacts.Count > 0)
        {
            foreach (var tool in _toolFacts)
            {
                tools.Add(new DoctorTool(tool.Name, tool.Status, tool.Message, tool.CommandPath));
            }
        }
        else
        {
            tools.Add(new DoctorTool("config", "PASS", _configStore.GetConfigPath(), _configStore.GetConfigPath()));
            tools.Add(new DoctorTool("module_state", "PASS", _configStore.GetModuleStatePath(), _configStore.GetModuleStatePath()));
            tools.Add(new DoctorTool("runtime_state", "PASS", _configStore.GetRuntimeStatePath(), _configStore.GetRuntimeStatePath()));
        }
        return tools;
    }

    private void CheckToolAvailability(List<DoctorIssue> issues)
    {
        if (_toolFacts is null) return;
        foreach (var tool in _toolFacts)
        {
            if (!tool.Detected && tool.Required)
            {
                issues.Add(new DoctorIssue($"{tool.Name}_missing", "FAIL", $"Required tool '{tool.Name}' not found: {tool.Message}"));
            }
        }
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
