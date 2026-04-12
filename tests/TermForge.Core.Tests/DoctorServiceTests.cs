using System.Collections.Generic;
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
}
