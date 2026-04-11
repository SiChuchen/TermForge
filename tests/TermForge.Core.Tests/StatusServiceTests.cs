using System.Collections.Generic;
using TermForge.Contracts;
using TermForge.Core.Interfaces;
using Xunit;

namespace TermForge.Core.Tests;

public class StatusServiceTests
{
    [Fact]
    public void StatusService_builds_report_from_config_store()
    {
        var store = new FakeConfigStore();
        var service = new TermForge.Core.Services.StatusService(store);

        var result = service.BuildReport();

        Assert.Equal("status", result.Command);
        Assert.Equal("termforge", result.Payload.PrimaryCommand);
        Assert.Equal("wtctl", result.Payload.FallbackCommand);
        Assert.Contains("proxy", result.Payload.EnabledModules);
        Assert.Equal(System.IO.Path.Combine(store.RootPath, "state"), result.Payload.RuntimeStatePath);
        Assert.Matches(@"^\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}$", result.GeneratedAt);
    }
}

internal sealed class FakeConfigStore : IConfigStore
{
    private ProxyConfigSnapshot _snapshot = new(false, string.Empty, string.Empty, string.Empty);

    public string ConfigPath { get; set; } = ".termforge/config.json";

    public IReadOnlyList<string> EnabledModules { get; set; } = new List<string> { "proxy" };

    public string ModuleStatePath { get; set; } = ".termforge/modules";

    public string RootPath { get; set; } = "termforge";

    public ProxyConfigSnapshot ReadProxyConfig()
    {
        return _snapshot;
    }

    public void WriteProxyConfig(ProxyConfigSnapshot snapshot)
    {
        _snapshot = snapshot;
    }

    public string GetRootPath()
    {
        return RootPath;
    }

    public string GetConfigPath()
    {
        return ConfigPath;
    }

    public string GetModuleStatePath()
    {
        return ModuleStatePath;
    }

    public IReadOnlyList<string> GetEnabledModules()
    {
        return EnabledModules;
    }
}
