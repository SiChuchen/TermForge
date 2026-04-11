namespace TermForge.Contracts;

public sealed record ProxyConfigSnapshot(bool Enabled, string Http, string Https, string NoProxy);
public sealed record ProxyScanPayload(string Target, ProxyConfigSnapshot Config);
public sealed record ProxyPlanPayload(string PlanId, string Target, string Mode, ProxyConfigSnapshot Before, ProxyConfigSnapshot Desired);
public sealed record ProxyApplyPayload(string ChangeId, string PlanId, string Target, ProxyConfigSnapshot After);
