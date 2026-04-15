using System.Collections.Generic;
using TermForge.Contracts;
using TermForge.Core.Interfaces;
using TermForge.Platform;

namespace TermForge.Core.Services;

public sealed class StatusService
{
    private const string FallbackCommand = "wtctl";
    private readonly IConfigStore _configStore;
    private readonly string? _sharedPrimaryCommandName;
    private readonly IProxyTargetAdapter? _npmAdapter;
    private readonly IProxyTargetAdapter? _pipAdapter;

    public StatusService(
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

    public CommandEnvelope<StatusPayload> BuildReport()
    {
        var proxyConfig = _configStore.ReadProxyConfig();
        var targetFlags = _configStore.GetProxyTargetFlags();
        var targetStates = BuildTargetStates();

        var payload = new StatusPayload(
            RootPath: _configStore.GetRootPath(),
            PrimaryCommand: ResolvePrimaryCommandName(),
            FallbackCommand: FallbackCommand,
            EnabledModules: _configStore.GetEnabledModules(),
            ConfigPath: _configStore.GetConfigPath(),
            ModuleStatePath: _configStore.GetModuleStatePath(),
            RuntimeStatePath: _configStore.GetRuntimeStatePath(),
            Proxy: new StatusProxySummary(
                proxyConfig.Enabled,
                proxyConfig.Http,
                proxyConfig.Https,
                proxyConfig.NoProxy,
                targetFlags,
                targetStates));

        return new CommandEnvelope<StatusPayload>(
            Command: "status",
            Status: "PASS",
            GeneratedAt: DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            Warnings: [],
            Errors: [],
            Payload: payload);
    }

    private List<StatusTargetState> BuildTargetStates()
    {
        var targetStates = new List<StatusTargetState>();
        if (_npmAdapter is not null)
        {
            var snap = _npmAdapter.ReadCurrent();
            targetStates.Add(new StatusTargetState("npm", _npmAdapter.IsAvailable(), snap.Enabled, snap.Http, snap.Https, snap.NoProxy));
        }
        if (_pipAdapter is not null)
        {
            var snap = _pipAdapter.ReadCurrent();
            targetStates.Add(new StatusTargetState("pip", _pipAdapter.IsAvailable(), snap.Enabled, snap.Http, snap.Https, snap.NoProxy));
        }
        return targetStates;
    }

    private string ResolvePrimaryCommandName()
    {
        return string.IsNullOrWhiteSpace(_sharedPrimaryCommandName)
            ? _configStore.GetPrimaryCommandName()
            : _sharedPrimaryCommandName;
    }
}
