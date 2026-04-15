using System.Text.Json;
using System.Text.Json.Nodes;
using TermForge.Contracts;
using TermForge.Core.Interfaces;

namespace TermForge.Platform.Windows;

public sealed class JsonConfigStore : IConfigStore
{
    private readonly string _configPath;
    private readonly string _moduleStatePath;
    private readonly string _rootPath;

    public JsonConfigStore(string rootPath, string configPath, string moduleStatePath)
    {
        _rootPath = rootPath;
        _configPath = configPath;
        _moduleStatePath = moduleStatePath;
    }

    public ProxyConfigSnapshot ReadProxyConfig()
    {
        var root = ReadJsonObject(_configPath);
        var proxy = root["proxy"] as JsonObject;
        return new ProxyConfigSnapshot(
            Enabled: proxy?["enabled"]?.GetValue<bool>() ?? false,
            Http: proxy?["http"]?.GetValue<string>() ?? string.Empty,
            Https: proxy?["https"]?.GetValue<string>() ?? string.Empty,
            NoProxy: proxy?["noProxy"]?.GetValue<string>() ?? string.Empty);
    }

    public void WriteProxyConfig(ProxyConfigSnapshot snapshot)
    {
        var root = ReadJsonObject(_configPath);
        root["proxy"] = new JsonObject
        {
            ["enabled"] = snapshot.Enabled,
            ["http"] = snapshot.Http,
            ["https"] = snapshot.Https,
            ["noProxy"] = snapshot.NoProxy
        };
        WriteJsonObject(_configPath, root);
    }

    public ProxyTargetFlags GetProxyTargetFlags()
    {
        var root = ReadJsonObject(_configPath);
        var proxy = root["proxy"] as JsonObject;
        var targets = proxy?["targets"] as JsonObject;
        if (targets is null)
        {
            return ProxyTargetFlags.Default;
        }

        return new ProxyTargetFlags(
            Env: targets["env"]?.GetValue<bool>() ?? true,
            Git: targets["git"]?.GetValue<bool>() ?? false,
            Npm: targets["npm"]?.GetValue<bool>() ?? false,
            Pip: targets["pip"]?.GetValue<bool>() ?? false);
    }

    public void WriteProxyTargetFlags(ProxyTargetFlags flags)
    {
        var root = ReadJsonObject(_configPath);
        var proxy = root["proxy"] as JsonObject;
        if (proxy is null)
        {
            proxy = new JsonObject();
            root["proxy"] = proxy;
        }

        proxy["targets"] = new JsonObject
        {
            ["env"] = flags.Env,
            ["git"] = flags.Git,
            ["npm"] = flags.Npm,
            ["pip"] = flags.Pip
        };
        WriteJsonObject(_configPath, root);
    }

    public string GetRootPath()
    {
        return _rootPath;
    }

    public string GetConfigPath()
    {
        return _configPath;
    }

    public string GetModuleStatePath()
    {
        return _moduleStatePath;
    }

    public string GetRuntimeStatePath()
    {
        return Path.Combine(_rootPath, "state");
    }

    public string GetPrimaryCommandName()
    {
        var root = ReadJsonObject(_configPath);
        var cli = root["cli"] as JsonObject;
        var commandName = cli?["commandName"]?.GetValue<string>();
        return string.IsNullOrWhiteSpace(commandName) ? "termforge" : commandName;
    }

    public IReadOnlyList<string> GetEnabledModules()
    {
        var root = ReadJsonObject(_moduleStatePath);
        var enabled = new List<string>();
        foreach (var pair in root)
        {
            if (pair.Value?.GetValue<bool>() == true)
            {
                enabled.Add(pair.Key);
            }
        }

        return enabled;
    }

    private static JsonObject ReadJsonObject(string path)
    {
        if (!File.Exists(path))
        {
            return new JsonObject();
        }

        var content = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(content))
        {
            return new JsonObject();
        }

        return JsonNode.Parse(content) as JsonObject ?? new JsonObject();
    }

    private static void WriteJsonObject(string path, JsonObject root)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }
}
