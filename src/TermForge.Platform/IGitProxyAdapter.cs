using TermForge.Contracts;

namespace TermForge.Platform;

public interface IGitProxyAdapter
{
    bool IsAvailable();
    GitProxySnapshot ReadCurrent();
    GitProxyPlan PlanEnable(string httpProxy, string httpsProxy, string noProxy);
    GitProxyPlan PlanDisable();
    GitProxySnapshot Apply(GitProxyPlan plan);
    GitProxySnapshot Verify(GitProxySnapshot desired);
    GitProxySnapshot Rollback(GitProxySnapshot before);
}
