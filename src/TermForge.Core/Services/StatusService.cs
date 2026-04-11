using TermForge.Contracts;
using TermForge.Core.Interfaces;

namespace TermForge.Core.Services;

public sealed class StatusService
{
    private const string FallbackCommand = "wtctl";
    private readonly IConfigStore _configStore;

    public StatusService(IConfigStore configStore)
    {
        _configStore = configStore;
    }

    public CommandEnvelope<StatusPayload> BuildReport()
    {
        var payload = new StatusPayload(
            RootPath: _configStore.GetRootPath(),
            PrimaryCommand: _configStore.GetPrimaryCommandName(),
            FallbackCommand: FallbackCommand,
            EnabledModules: _configStore.GetEnabledModules(),
            ConfigPath: _configStore.GetConfigPath(),
            ModuleStatePath: _configStore.GetModuleStatePath(),
            RuntimeStatePath: _configStore.GetRuntimeStatePath());

        return new CommandEnvelope<StatusPayload>(
            Command: "status",
            Status: "PASS",
            GeneratedAt: DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            Warnings: [],
            Errors: [],
            Payload: payload);
    }
}
