namespace TermForge.Contracts;

public sealed record EnvironmentHostFacts(
    bool IsWindows,
    string OsVersion,
    string PowerShellEdition,
    string PowerShellVersion,
    string LocalAppData,
    string DocumentsPath,
    bool CanWriteLocalAppData);

public sealed record EnvironmentToolFact(
    string Name,
    bool Detected,
    string CommandPath,
    bool Required,
    bool CanAutoInstall,
    string Status,
    string Message);

public sealed record EnvironmentProxyFact(
    bool Enabled,
    string HttpProxy,
    string HttpsProxy,
    string NoProxy,
    string Source,
    string Status);

public sealed record EnvironmentInstallHostFact(
    bool IsAvailable,
    string ExecutablePath,
    string HostKind,
    string Status,
    string Message);
