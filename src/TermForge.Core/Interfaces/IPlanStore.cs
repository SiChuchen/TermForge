using TermForge.Contracts;

namespace TermForge.Core.Interfaces;

public interface IPlanStore
{
    PlanRecord? GetPlanRecord(string planId)
        => throw new NotImplementedException();

    void SavePlanRecord(PlanRecord plan)
        => throw new NotImplementedException();

    ProxyPlanPayload? GetPlan(string planId)
    {
        return GetPlanRecord(planId)?.ToProxyPlanPayload();
    }

    void SavePlan(ProxyPlanPayload plan)
    {
        SavePlanRecord(plan);
    }
}
