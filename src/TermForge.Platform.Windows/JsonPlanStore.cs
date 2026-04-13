using System.Text.Json;
using TermForge.Contracts;
using TermForge.Core.Interfaces;

namespace TermForge.Platform.Windows;

public sealed class JsonPlanStore : IPlanStore
{
    private readonly string _path;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public JsonPlanStore(string path)
    {
        _path = path;
    }

    public ProxyPlanPayload? GetPlan(string planId)
    {
        return GetPlanRecord(planId)?.ToProxyPlanPayload();
    }

    public void SavePlan(ProxyPlanPayload plan)
    {
        SavePlanRecord(plan);
    }

    public PlanRecord? GetPlanRecord(string planId)
    {
        return ReadPlans().FirstOrDefault(plan => string.Equals(plan.PlanId, planId, StringComparison.Ordinal));
    }

    public void SavePlanRecord(PlanRecord plan)
    {
        var plans = ReadPlans();
        var existingIndex = plans.FindIndex(existing => string.Equals(existing.PlanId, plan.PlanId, StringComparison.Ordinal));
        if (existingIndex >= 0)
        {
            plans[existingIndex] = plan;
        }
        else
        {
            plans.Add(plan);
        }

        WritePlans(plans);
    }

    private List<PlanRecord> ReadPlans()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        var content = File.ReadAllText(_path);
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<PlanRecord>>(content, JsonOptions) ?? [];
    }

    private void WritePlans(List<PlanRecord> plans)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_path, JsonSerializer.Serialize(plans, JsonOptions));
    }
}
