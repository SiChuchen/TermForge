using System.Collections.Generic;
using TermForge.Contracts;
using TermForge.Platform;
using Xunit;

namespace TermForge.Core.Tests;

public class DoctorServiceTests
{
    [Fact]
    public void DoctorService_builds_json_payload_from_runtime_state()
    {
        var store = new FakeConfigStore
        {
            RootPath = @"E:\TermForge",
            ConfigPath = @"E:\TermForge\scc.config.json",
            ModuleStatePath = @"E:\TermForge\module_state.json",
            RuntimeStatePath = @"E:\TermForge\state",
            PrimaryCommandName = "tfx",
            EnabledModules = new List<string> { "proxy", "theme" }
        };

        var service = new TermForge.Core.Services.DoctorService(store);

        var result = service.BuildReport();

        Assert.Equal("doctor", result.Command);
        Assert.Equal("tfx", result.Payload.PrimaryCommandName);
        Assert.Equal("PASS", result.Payload.OverallStatus);
        Assert.Contains("proxy", result.Payload.EnabledModules);
    }

    [Fact]
    public void DoctorService_uses_shared_environment_facts_for_runtime_command_name()
    {
        var store = new FakeConfigStore
        {
            RootPath = @"E:\TermForge",
            ConfigPath = @"E:\TermForge\scc.config.json",
            ModuleStatePath = @"E:\TermForge\module_state.json",
            RuntimeStatePath = @"E:\TermForge\state",
            PrimaryCommandName = "tfx",
            EnabledModules = new List<string> { "proxy", "theme" }
        };

        var service = new TermForge.Core.Services.DoctorService(store);
        var result = service.BuildReport();

        Assert.Equal("tfx", result.Payload.PrimaryCommandName);
    }

    [Fact]
    public void DoctorService_warns_when_npm_target_enabled_but_not_available()
    {
        var store = new FakeConfigStore { RootPath = @"E:\TF", ConfigPath = @"E:\TF\c.json", ModuleStatePath = @"E:\TF\m.json", RuntimeStatePath = @"E:\TF\s", PrimaryCommandName = "tfx", TargetFlags = new ProxyTargetFlags(true, false, true, false) };
        var npm = new FakeProxyTargetAdapter("npm", false, new ProxyConfigSnapshot(false, "", "", ""));
        var result = new TermForge.Core.Services.DoctorService(store, npmAdapter: npm).BuildReport();
        Assert.Equal("WARN", result.Payload.OverallStatus);
        Assert.Contains(result.Payload.Issues, i => i.Name == "npm_not_found");
    }

    [Fact]
    public void DoctorService_warns_on_npm_config_drift()
    {
        var store = new FakeConfigStore { RootPath = @"E:\TF", ConfigPath = @"E:\TF\c.json", ModuleStatePath = @"E:\TF\m.json", RuntimeStatePath = @"E:\TF\s", PrimaryCommandName = "tfx" };
        store.WriteProxyConfig(new ProxyConfigSnapshot(true, "http://config-proxy:8080", "http://config-proxy:8080", "localhost"));
        var npm = new FakeProxyTargetAdapter("npm", true, new ProxyConfigSnapshot(true, "http://different:9999", "http://different:9999", "localhost"));
        var result = new TermForge.Core.Services.DoctorService(store, npmAdapter: npm).BuildReport();
        Assert.Equal("WARN", result.Payload.OverallStatus);
        Assert.Contains(result.Payload.Issues, i => i.Name == "npm_config_drift");
    }

    [Fact]
    public void DoctorService_passes_when_npm_config_matches()
    {
        var store = new FakeConfigStore { RootPath = @"E:\TF", ConfigPath = @"E:\TF\c.json", ModuleStatePath = @"E:\TF\m.json", RuntimeStatePath = @"E:\TF\s", PrimaryCommandName = "tfx" };
        store.WriteProxyConfig(new ProxyConfigSnapshot(true, "http://proxy:8080", "http://proxy:8080", "localhost"));
        var npm = new FakeProxyTargetAdapter("npm", true, new ProxyConfigSnapshot(true, "http://proxy:8080", "http://proxy:8080", "localhost"));
        var result = new TermForge.Core.Services.DoctorService(store, npmAdapter: npm).BuildReport();
        Assert.Equal("PASS", result.Payload.OverallStatus);
        Assert.DoesNotContain(result.Payload.Issues, i => i.Name == "npm_config_drift");
    }
}
