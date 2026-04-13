using System.Text.Json;
using System.Text.Json.Serialization;

namespace TermForge.Contracts;

public sealed record PlanRecord(
    string PlanId,
    string Target,
    string SchemaVersion,
    string CreatedAt,
    string PayloadType,
    object Payload)
{
    [JsonIgnore]
    public ProxyConfigSnapshot Before => ToProxyPlanPayload().Before;

    [JsonIgnore]
    public ProxyConfigSnapshot Desired => ToProxyPlanPayload().Desired;

    public T GetPayload<T>()
    {
        return UnifiedStoreValueReader.Deserialize<T>(Payload);
    }

    public ProxyPlanPayload ToProxyPlanPayload()
    {
        return UnifiedStoreValueReader.Deserialize<ProxyPlanPayload>(Payload) with { PlanId = PlanId, Target = Target };
    }

    public static implicit operator PlanRecord(ProxyPlanPayload payload)
    {
        return new PlanRecord(
            PlanId: payload.PlanId,
            Target: payload.Target,
            SchemaVersion: "legacy-env",
            CreatedAt: string.Empty,
            PayloadType: "proxy-plan",
            Payload: payload);
    }
}

public sealed record ChangeRecord(
    string ChangeId,
    string Target,
    string PlanId,
    string SchemaVersion,
    string AppliedAt,
    string PayloadType,
    object Before,
    object After)
{
    [JsonIgnore]
    public ProxyConfigSnapshot AfterSnapshot => ToProxyApplyPayload().After;

    public T GetBefore<T>()
    {
        return UnifiedStoreValueReader.Deserialize<T>(Before);
    }

    public T GetAfter<T>()
    {
        return UnifiedStoreValueReader.Deserialize<T>(After);
    }

    public ProxyApplyPayload ToProxyApplyPayload()
    {
        var after = UnifiedStoreValueReader.Deserialize<ProxyConfigSnapshot>(After);
        return new ProxyApplyPayload(ChangeId, PlanId, Target, after);
    }

    public static implicit operator ChangeRecord(ProxyApplyPayload payload)
    {
        return new ChangeRecord(
            ChangeId: payload.ChangeId,
            Target: payload.Target,
            PlanId: payload.PlanId,
            SchemaVersion: "legacy-env",
            AppliedAt: string.Empty,
            PayloadType: "proxy-apply",
            Before: new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty),
            After: payload.After);
    }
}

internal static class UnifiedStoreValueReader
{
    public static T Deserialize<T>(object value)
    {
        return value switch
        {
            T typed => typed,
            JsonElement element => element.Deserialize<T>() ?? throw new InvalidOperationException($"Unable to deserialize {typeof(T).Name} from stored JSON."),
            _ => throw new InvalidOperationException($"Stored value cannot be used as {typeof(T).Name}.")
        };
    }
}
