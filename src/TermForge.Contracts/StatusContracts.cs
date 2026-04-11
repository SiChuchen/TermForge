namespace TermForge.Contracts;

public sealed record StatusPayload(
    string RootPath,
    string PrimaryCommand,
    string FallbackCommand,
    IReadOnlyList<string> EnabledModules,
    string ConfigPath,
    string ModuleStatePath,
    string RuntimeStatePath);
