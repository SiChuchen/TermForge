namespace TermForge.Contracts;

public sealed record StatusCheck(
    string Name,
    string Status,
    string? Details = null);

public sealed record StatusSnapshot(
    string Summary,
    StatusCheck[] Checks,
    string[] Warnings,
    string[] Errors);

public sealed record DoctorSnapshot(
    string Summary,
    StatusCheck[] Checks,
    string[] Warnings,
    string[] Errors);
