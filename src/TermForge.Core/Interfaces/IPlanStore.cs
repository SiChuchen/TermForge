using TermForge.Contracts;

namespace TermForge.Core.Interfaces;

public interface IPlanStore
{
    ProxyPlanPayload? GetPlan(string planId);
    void SavePlan(ProxyPlanPayload plan);
}
