using TermForge.Contracts;

namespace TermForge.Core.Interfaces;

public interface IPlanStore
{
    ProxyPlanPayload? GetPlan(string planId);
    void SavePlan(ProxyPlanPayload plan);

    PlanRecord? GetPlanRecord(string planId)
    {
        var plan = GetPlan(planId);
        return plan is null ? null : (PlanRecord)plan;
    }

    void SavePlanRecord(PlanRecord plan)
    {
        SavePlan(plan.ToProxyPlanPayload());
    }
}
