namespace TermForge.Contracts;

public sealed record DoctorIssue(
    string Name,
    string Status,
    string Message);

public sealed record DoctorProfile(
    string Name,
    string Status,
    string Message,
    string? Path);

public sealed record DoctorTool(
    string Name,
    string Status,
    string Message,
    string? Path);

public sealed record DoctorPayload(
    string RootPath,
    string PrimaryCommandName,
    string OverallStatus,
    int FailCount,
    int WarnCount,
    IReadOnlyList<DoctorProfile> Profiles,
    IReadOnlyList<string> EnabledModules,
    IReadOnlyList<DoctorTool> Tools,
    IReadOnlyList<DoctorIssue> Issues);
