using TermForge.Contracts;
using TermForge.Core.Interfaces;

namespace TermForge.Core.Services;

public sealed class StatusService
{
    private const string FallbackCommand = "wtctl";
    private const string PrimaryCommand = "termforge";
    private readonly IConfigStore _configStore;

    public StatusService(IConfigStore configStore)
    {
        _configStore = configStore;
    }

    public CommandEnvelope<StatusPayload> BuildReport()
    {
        var payload = new StatusPayload(
            RootPath: _configStore.GetRootPath(),
            PrimaryCommand: PrimaryCommand,
            FallbackCommand: FallbackCommand,
            EnabledModules: _configStore.GetEnabledModules(),
            ConfigPath: _configStore.GetConfigPath(),
            ModuleStatePath: _configStore.GetModuleStatePath(),
            RuntimeStatePath: Path.Combine(_configStore.GetRootPath(), "runtime"));

        return new CommandEnvelope<StatusPayload>(
            Command: "status",
            Status: "PASS",
            GeneratedAt: DateTimeOffset.UtcNow.ToString("O"),
            Warnings: [],
            Errors: [],
            Payload: payload);
    }
}
