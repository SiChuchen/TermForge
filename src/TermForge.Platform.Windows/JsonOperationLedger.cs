using System.Text.Json;
using TermForge.Contracts;
using TermForge.Core.Interfaces;

namespace TermForge.Platform.Windows;

public sealed class JsonOperationLedger : IOperationLedger
{
    private readonly string _path;

    public JsonOperationLedger(string path)
    {
        _path = path;
    }

    public ProxyApplyPayload? GetChange(string changeId)
    {
        return ReadChanges().FirstOrDefault(change => string.Equals(change.ChangeId, changeId, StringComparison.Ordinal));
    }

    public void AppendChange(ProxyApplyPayload change)
    {
        var changes = ReadChanges();
        changes.Add(change);
        WriteChanges(changes);
    }

    private List<ProxyApplyPayload> ReadChanges()
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

        return JsonSerializer.Deserialize<List<ProxyApplyPayload>>(content) ?? [];
    }

    private void WriteChanges(List<ProxyApplyPayload> changes)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_path, JsonSerializer.Serialize(changes, new JsonSerializerOptions { WriteIndented = true }));
    }
}
