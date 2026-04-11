namespace TermForge.Contracts;

public sealed record ProxyTarget(
    string Name,
    string Scope);

public sealed record ProxyPlanStep(
    string Target,
    string Action,
    string? Before = null,
    string? After = null,
    string? Reason = null);

public sealed record ProxyScanResult(
    string Summary,
    ProxyTarget[] Targets,
    string[] Warnings,
    string[] Errors);

public sealed record ProxyPlan(
    string PlanId,
    string Profile,
    ProxyTarget[] Targets,
    ProxyPlanStep[] Steps,
    string[] Warnings,
    string[] Errors);

public sealed record ProxyApplyResult(
    string PlanId,
    string OperationId,
    string[] Warnings,
    string[] Errors);

public sealed record ProxyRollbackResult(
    string ChangeId,
    string OperationId,
    string[] Warnings,
    string[] Errors);
