using System.Text.Json;
using TermForge.Contracts;
using TermForge.Core.Interfaces;

namespace TermForge.Platform.Windows;

public sealed class JsonOperationLedger : IOperationLedger
{
    private readonly string _path;
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public JsonOperationLedger(string path)
    {
        _path = path;
    }

    public ProxyApplyPayload? GetChange(string changeId)
    {
        return GetChangeRecord(changeId)?.ToProxyApplyPayload();
    }

    public void AppendChange(ProxyApplyPayload change)
    {
        AppendChangeRecord(change);
    }

    public ChangeRecord? GetChangeRecord(string changeId)
    {
        return ReadChanges().FirstOrDefault(change => string.Equals(change.ChangeId, changeId, StringComparison.Ordinal));
    }

    public void AppendChangeRecord(ChangeRecord change)
    {
        var changes = ReadChanges();
        changes.Add(change);
        WriteChanges(changes);
    }

    private List<ChangeRecord> ReadChanges()
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        var content = File.ReadAllText(_path);
        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        return JsonSerializer.Deserialize<List<ChangeRecord>>(content, JsonOptions) ?? [];
    }

    private void WriteChanges(List<ChangeRecord> changes)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_path, JsonSerializer.Serialize(changes, JsonOptions));
    }
}
