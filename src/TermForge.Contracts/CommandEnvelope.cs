namespace TermForge.Contracts;

public sealed record CommandEnvelope<TPayload>(
    string Command,
    string Status,
    string GeneratedAt,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Errors,
    TPayload Payload)
{
    public string SchemaVersion { get; init; } = "2026-04-11";
}
