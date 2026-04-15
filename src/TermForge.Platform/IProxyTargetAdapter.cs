using TermForge.Contracts;

namespace TermForge.Platform;

public interface IProxyTargetAdapter
{
    string TargetName { get; }
    bool IsAvailable();
    ProxyConfigSnapshot ReadCurrent();
    ProxyConfigSnapshot PlanEnable(string http, string https, string noProxy);
    ProxyConfigSnapshot PlanDisable();
    ProxyConfigSnapshot Apply(ProxyConfigSnapshot desired);
    ProxyConfigSnapshot Verify(ProxyConfigSnapshot desired);
    ProxyConfigSnapshot Rollback(ProxyConfigSnapshot before);
}
