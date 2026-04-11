using System.Collections.Generic;
using TermForge.Contracts;
using TermForge.Core.Interfaces;
using Xunit;

namespace TermForge.Core.Tests;

public class StatusServiceTests
{
    [Fact]
    public void StatusService_uses_runtime_config_for_command_name_and_paths()
    {
        var store = new FakeConfigStore
        {
            RootPath = @"E:\TermForge",
            ConfigPath = @"E:\TermForge\scc.config.json",
            ModuleStatePath = @"E:\TermForge\module_state.json",
            RuntimeStatePath = @"E:\TermForge\state",
            PrimaryCommandName = "tfx"
        };

        var service = new TermForge.Core.Services.StatusService(store);

        var result = service.BuildReport();

        Assert.Equal("status", result.Command);
        Assert.Equal("tfx", result.Payload.PrimaryCommand);
        Assert.Equal(@"E:\TermForge\state", result.Payload.RuntimeStatePath);
        Assert.Contains("proxy", result.Payload.EnabledModules);
    }
}

internal sealed class FakeConfigStore : IConfigStore
{
    private ProxyConfigSnapshot _snapshot = new(false, string.Empty, string.Empty, string.Empty);

    public string RootPath { get; set; } = "termforge";
    public string ConfigPath { get; set; } = "scc.config.json";
    public string ModuleStatePath { get; set; } = "module_state.json";
    public string RuntimeStatePath { get; set; } = "state";
    public string PrimaryCommandName { get; set; } = "termforge";
    public IReadOnlyList<string> EnabledModules { get; set; } = new List<string> { "proxy" };

    public ProxyConfigSnapshot ReadProxyConfig() => _snapshot;
    public void WriteProxyConfig(ProxyConfigSnapshot snapshot) => _snapshot = snapshot;
    public string GetRootPath() => RootPath;
    public string GetConfigPath() => ConfigPath;
    public string GetModuleStatePath() => ModuleStatePath;
    public string GetRuntimeStatePath() => RuntimeStatePath;
    public string GetPrimaryCommandName() => PrimaryCommandName;
    public IReadOnlyList<string> GetEnabledModules() => EnabledModules;
}
