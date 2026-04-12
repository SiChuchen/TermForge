namespace TermForge.Contracts;

public sealed record GitProxySnapshot(
    bool Available,
    string Scope,
    string HttpProxy,
    string HttpsProxy,
    string NoProxy);

public sealed record GitProxyPlanAction(
    string Key,
    string Action,
    string Before,
    string After);

public sealed record GitProxyPlan(
    string Target,
    string Mode,
    GitProxySnapshot Before,
    GitProxySnapshot Desired,
    IReadOnlyList<GitProxyPlanAction> Actions);
