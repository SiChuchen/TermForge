using TermForge.Contracts;

namespace TermForge.Core.Interfaces;

public interface IConfigStore
{
    ProxyConfigSnapshot ReadProxyConfig();
    void WriteProxyConfig(ProxyConfigSnapshot snapshot);
    ProxyTargetFlags GetProxyTargetFlags();
    void WriteProxyTargetFlags(ProxyTargetFlags flags);
    string GetRootPath();
    string GetConfigPath();
    string GetModuleStatePath();
    string GetRuntimeStatePath();
    string GetPrimaryCommandName();
    IReadOnlyList<string> GetEnabledModules();
}
