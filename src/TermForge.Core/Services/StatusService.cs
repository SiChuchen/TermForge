using TermForge.Contracts;
using TermForge.Core.Interfaces;

namespace TermForge.Core.Services;

public sealed class StatusService
{
    private const string FallbackCommand = "wtctl";
    private readonly IConfigStore _configStore;
    private readonly string? _sharedPrimaryCommandName;

    public StatusService(IConfigStore configStore, string? sharedPrimaryCommandName = null)
    {
        _configStore = configStore;
        _sharedPrimaryCommandName = sharedPrimaryCommandName;
    }

    public CommandEnvelope<StatusPayload> BuildReport()
    {
        var proxyConfig = _configStore.ReadProxyConfig();
        var targetFlags = _configStore.GetProxyTargetFlags();
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
                targetFlags));

        return new CommandEnvelope<StatusPayload>(
            Command: "status",
            Status: "PASS",
            GeneratedAt: DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            Warnings: [],
            Errors: [],
            Payload: payload);
    }

    private string ResolvePrimaryCommandName()
    {
        return string.IsNullOrWhiteSpace(_sharedPrimaryCommandName)
            ? _configStore.GetPrimaryCommandName()
            : _sharedPrimaryCommandName;
    }
}
