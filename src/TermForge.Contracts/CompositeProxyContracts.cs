using System;

namespace TermForge.Contracts;

public sealed record CompositeProxyPlan(
    IReadOnlyList<string> Targets,
    string Mode,
    IReadOnlyList<CompositeTargetPlan> Plans);

public sealed record CompositeTargetPlan(
    string Target,
    string PayloadType,
    object Payload)
{
    public T GetPayload<T>()
    {
        return UnifiedStoreValueReader.Deserialize<T>(Payload);
    }

    public ProxyPlanPayload ToProxyPlanPayload()
    {
        EnsurePayloadType("proxy-plan");
        return UnifiedStoreValueReader.Deserialize<ProxyPlanPayload>(Payload) with { Target = Target };
    }

    public GitProxyPlan ToGitProxyPlan()
    {
        EnsurePayloadType("git-proxy-plan");
        return UnifiedStoreValueReader.Deserialize<GitProxyPlan>(Payload) with { Target = Target };
    }

    private void EnsurePayloadType(string expected)
    {
        if (!string.Equals(PayloadType, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Composite target '{Target}' payload type '{PayloadType}' cannot be read as '{expected}'.");
        }
    }
}

public sealed record CompositeProxyChange(
    IReadOnlyList<string> Targets,
    string Mode,
    IReadOnlyList<CompositeTargetChange> Changes,
    bool RollbackTriggered,
    string? FailureTarget);

public sealed record CompositeTargetChange(
    string Target,
    string PayloadType,
    object Before,
    object After)
{
    public T GetBefore<T>()
    {
        return UnifiedStoreValueReader.Deserialize<T>(Before);
    }

    public T GetAfter<T>()
    {
        return UnifiedStoreValueReader.Deserialize<T>(After);
    }

    public ProxyConfigSnapshot ToProxyBeforeSnapshot()
    {
        EnsurePayloadType("proxy-config-snapshot");
        return UnifiedStoreValueReader.Deserialize<ProxyConfigSnapshot>(Before);
    }

    public ProxyConfigSnapshot ToProxyAfterSnapshot()
    {
        EnsurePayloadType("proxy-config-snapshot");
        return UnifiedStoreValueReader.Deserialize<ProxyConfigSnapshot>(After);
    }

    public GitProxySnapshot ToGitBeforeSnapshot()
    {
        EnsurePayloadType("git-proxy-snapshot");
        return UnifiedStoreValueReader.Deserialize<GitProxySnapshot>(Before);
    }

    public GitProxySnapshot ToGitAfterSnapshot()
    {
        EnsurePayloadType("git-proxy-snapshot");
        return UnifiedStoreValueReader.Deserialize<GitProxySnapshot>(After);
    }

    private void EnsurePayloadType(string expected)
    {
        if (!string.Equals(PayloadType, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Composite target '{Target}' payload type '{PayloadType}' cannot be read as '{expected}'.");
        }
    }
}
