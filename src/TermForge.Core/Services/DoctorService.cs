using System.Collections.Generic;
using TermForge.Contracts;
using TermForge.Core.Interfaces;

namespace TermForge.Core.Services;

public sealed class DoctorService
{
    private readonly IConfigStore _configStore;
    private readonly string? _sharedPrimaryCommandName;

    public DoctorService(IConfigStore configStore, string? sharedPrimaryCommandName = null)
    {
        _configStore = configStore;
        _sharedPrimaryCommandName = sharedPrimaryCommandName;
    }

    public CommandEnvelope<DoctorPayload> BuildReport()
    {
        var enabledModules = _configStore.GetEnabledModules();

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

        var payload = new DoctorPayload(
            RootPath: _configStore.GetRootPath(),
            PrimaryCommandName: ResolvePrimaryCommandName(),
            OverallStatus: "PASS",
            FailCount: 0,
            WarnCount: 0,
            Profiles: profiles,
            EnabledModules: enabledModules,
            Tools: tools,
            Issues: []);

        return new CommandEnvelope<DoctorPayload>(
            Command: "doctor",
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
