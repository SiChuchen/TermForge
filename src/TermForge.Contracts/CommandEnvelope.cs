namespace TermForge.Contracts;

public sealed record CommandEnvelope<T>(
    string Command,
    string Status,
    string GeneratedAt,
    string[] Warnings,
    string[] Errors,
    T Payload)
{
    public string SchemaVersion { get; init; } = "2026-04-11";
}
