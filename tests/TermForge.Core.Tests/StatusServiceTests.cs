using System;
using System.Collections.Generic;
using TermForge.Contracts;
using TermForge.Core.Interfaces;
using TermForge.Platform;
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

    [Fact]
    public void StatusService_uses_shared_environment_facts_for_runtime_command_name()
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

        Assert.Equal("tfx", result.Payload.PrimaryCommand);
    }

    [Fact]
    public void StatusService_includes_proxy_target_flags_in_report()
    {
        var store = new FakeConfigStore
        {
            RootPath = @"E:\TermForge",
            ConfigPath = @"E:\TermForge\scc.config.json",
            ModuleStatePath = @"E:\TermForge\module_state.json",
            RuntimeStatePath = @"E:\TermForge\state",
            PrimaryCommandName = "termforge",
            TargetFlags = new ProxyTargetFlags(Env: true, Git: true, Npm: false, Pip: false)
        };

        var service = new TermForge.Core.Services.StatusService(store);
        var result = service.BuildReport();

        Assert.True(result.Payload.Proxy.Targets.Env);
        Assert.True(result.Payload.Proxy.Targets.Git);
        Assert.False(result.Payload.Proxy.Targets.Npm);
        Assert.False(result.Payload.Proxy.Targets.Pip);
    }

    [Fact]
    public void StatusService_includes_npm_target_state_when_adapter_provided()
    {
        var store = new FakeConfigStore { RootPath = @"E:\TF", ConfigPath = @"E:\TF\c.json", ModuleStatePath = @"E:\TF\m.json", RuntimeStatePath = @"E:\TF\s", PrimaryCommandName = "tfx" };
        var npm = new FakeProxyTargetAdapter("npm", true, new ProxyConfigSnapshot(true, "http://proxy:8080", "http://proxy:8080", "localhost"));
        var result = new TermForge.Core.Services.StatusService(store, npmAdapter: npm).BuildReport();
        Assert.Single(result.Payload.Proxy.TargetStates);
        Assert.Equal("npm", result.Payload.Proxy.TargetStates[0].Target);
        Assert.True(result.Payload.Proxy.TargetStates[0].Available);
        Assert.True(result.Payload.Proxy.TargetStates[0].Enabled);
    }

    [Fact]
    public void StatusService_shows_npm_unavailable_when_not_installed()
    {
        var store = new FakeConfigStore { RootPath = @"E:\TF", ConfigPath = @"E:\TF\c.json", ModuleStatePath = @"E:\TF\m.json", RuntimeStatePath = @"E:\TF\s", PrimaryCommandName = "tfx" };
        var npm = new FakeProxyTargetAdapter("npm", false, new ProxyConfigSnapshot(false, "", "", ""));
        var result = new TermForge.Core.Services.StatusService(store, npmAdapter: npm).BuildReport();
        Assert.Single(result.Payload.Proxy.TargetStates);
        Assert.False(result.Payload.Proxy.TargetStates[0].Available);
    }

    [Fact]
    public void StatusService_skips_npm_when_adapter_not_provided()
    {
        var store = new FakeConfigStore { RootPath = @"E:\TF", ConfigPath = @"E:\TF\c.json", ModuleStatePath = @"E:\TF\m.json", RuntimeStatePath = @"E:\TF\s", PrimaryCommandName = "tfx" };
        var result = new TermForge.Core.Services.StatusService(store).BuildReport();
        Assert.Empty(result.Payload.Proxy.TargetStates);
    }

    [Fact]
    public void StatusService_includes_pip_target_state_when_adapter_provided()
    {
        var store = new FakeConfigStore { RootPath = @"E:\TF", ConfigPath = @"E:\TF\c.json", ModuleStatePath = @"E:\TF\m.json", RuntimeStatePath = @"E:\TF\s", PrimaryCommandName = "tfx" };
        var pip = new FakeProxyTargetAdapter("pip", true, new ProxyConfigSnapshot(true, "http://proxy:3128", "http://proxy:3128", ""));
        var result = new TermForge.Core.Services.StatusService(store, pipAdapter: pip).BuildReport();
        Assert.Single(result.Payload.Proxy.TargetStates);
        Assert.Equal("pip", result.Payload.Proxy.TargetStates[0].Target);
        Assert.True(result.Payload.Proxy.TargetStates[0].Enabled);
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
    public ProxyTargetFlags TargetFlags { get; set; } = ProxyTargetFlags.Default;

    public ProxyConfigSnapshot ReadProxyConfig() => _snapshot;
    public void WriteProxyConfig(ProxyConfigSnapshot snapshot) => _snapshot = snapshot;
    public ProxyTargetFlags GetProxyTargetFlags() => TargetFlags;
    public void WriteProxyTargetFlags(ProxyTargetFlags flags) => TargetFlags = flags;
    public string GetRootPath() => RootPath;
    public string GetConfigPath() => ConfigPath;
    public string GetModuleStatePath() => ModuleStatePath;
    public string GetRuntimeStatePath() => RuntimeStatePath;
    public string GetPrimaryCommandName() => PrimaryCommandName;
    public IReadOnlyList<string> GetEnabledModules() => EnabledModules;
}

internal sealed class FakeProxyTargetAdapter : IProxyTargetAdapter
{
    private readonly string _targetName;
    private readonly bool _isAvailable;
    private readonly ProxyConfigSnapshot _current;
    public FakeProxyTargetAdapter(string targetName, bool isAvailable, ProxyConfigSnapshot current) { _targetName = targetName; _isAvailable = isAvailable; _current = current; }
    public string TargetName => _targetName;
    public bool IsAvailable() => _isAvailable;
    public ProxyConfigSnapshot ReadCurrent() => _current;
    public ProxyConfigSnapshot PlanEnable(string http, string https, string noProxy) => throw new NotSupportedException();
    public ProxyConfigSnapshot PlanDisable() => throw new NotSupportedException();
    public ProxyConfigSnapshot Apply(ProxyConfigSnapshot desired) => throw new NotSupportedException();
    public ProxyConfigSnapshot Verify(ProxyConfigSnapshot desired) => throw new NotSupportedException();
    public ProxyConfigSnapshot Rollback(ProxyConfigSnapshot before) => throw new NotSupportedException();
}
