namespace TermForge.Contracts;

public sealed record CompositeProxyPlan(
    IReadOnlyList<string> Targets,
    string Mode,
    IReadOnlyList<CompositeTargetPlan> Plans);

public sealed record CompositeTargetPlan(
    string Target,
    string PayloadType,
    object Payload);

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
    object After);
