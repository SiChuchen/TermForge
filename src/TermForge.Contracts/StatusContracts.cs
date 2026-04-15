using System.Collections.Generic;

namespace TermForge.Contracts;

public sealed record StatusPayload(
    string RootPath,
    string PrimaryCommand,
    string FallbackCommand,
    IReadOnlyList<string> EnabledModules,
    string ConfigPath,
    string ModuleStatePath,
    string RuntimeStatePath,
    StatusProxySummary Proxy);

public sealed record StatusProxySummary(
    bool Enabled,
    string Http,
    string Https,
    string NoProxy,
    ProxyTargetFlags Targets,
    IReadOnlyList<StatusTargetState> TargetStates);

public sealed record StatusTargetState(
    string Target,
    bool Available,
    bool Enabled,
    string Http,
    string Https,
    string NoProxy);
