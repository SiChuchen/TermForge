using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TermForge.Contracts;
using TermForge.Platform;

namespace TermForge.Platform.Windows;

public sealed class WindowsPipProxyAdapter : IProxyTargetAdapter
{
    private const string ProxyKey = "proxy";
    private const string GlobalSection = "[global]";

    private readonly Func<string?> _resolvePipPath;
    private readonly Func<string> _getPipIniPath;

    public string TargetName => "pip";

    public WindowsPipProxyAdapter(
        Func<string?>? resolvePipPath = null,
        Func<string>? getPipIniPath = null)
    {
        _resolvePipPath = resolvePipPath ?? DefaultResolvePipPath;
        _getPipIniPath = getPipIniPath ?? DefaultGetPipIniPath;
    }

    public bool IsAvailable() => _resolvePipPath() is not null;

    public ProxyConfigSnapshot ReadCurrent()
    {
        var path = _getPipIniPath();
        if (!File.Exists(path))
        {
            return new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty);
        }

        var lines = File.ReadAllLines(path);
        var proxyValue = ReadProxyFromGlobal(lines);
        if (string.IsNullOrWhiteSpace(proxyValue))
        {
            return new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty);
        }

        return new ProxyConfigSnapshot(true, proxyValue, proxyValue, string.Empty);
    }

    public ProxyConfigSnapshot PlanEnable(string http, string https, string noProxy)
    {
        var value = string.IsNullOrWhiteSpace(https) ? http : https;
        return new ProxyConfigSnapshot(true, value, value, string.Empty);
    }

    public ProxyConfigSnapshot PlanDisable()
    {
        return new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty);
    }

    public ProxyConfigSnapshot Apply(ProxyConfigSnapshot desired)
    {
        var path = _getPipIniPath();
        var lines = File.Exists(path)
            ? File.ReadAllLines(path).ToList()
            : new List<string>();

        if (desired.Enabled)
        {
            EnsureGlobalSection(lines);
            SetOrAddProxy(lines, desired.Https);
        }
        else
        {
            RemoveProxy(lines);
        }

        EnsureDirectory(path);
        File.WriteAllLines(path, lines);
        return ReadCurrent();
    }

    public ProxyConfigSnapshot Verify(ProxyConfigSnapshot desired)
    {
        var current = ReadCurrent();
        var normalizedDesired = new ProxyConfigSnapshot(
            desired.Enabled,
            desired.Http?.Trim() ?? string.Empty,
            string.IsNullOrWhiteSpace(desired.Https) ? (desired.Http?.Trim() ?? string.Empty) : desired.Https.Trim(),
            string.Empty);

        if (current.Enabled != normalizedDesired.Enabled ||
            current.Http != normalizedDesired.Http)
        {
            throw new InvalidOperationException("pip proxy verification failed");
        }

        return current;
    }

    public ProxyConfigSnapshot Rollback(ProxyConfigSnapshot before)
    {
        return Apply(before);
    }

    private static string? ReadProxyFromGlobal(string[] lines)
    {
        var inGlobal = false;
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                inGlobal = string.Equals(trimmed, GlobalSection, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (inGlobal && trimmed.StartsWith(ProxyKey + " ", StringComparison.Ordinal))
            {
                var eq = trimmed.IndexOf('=');
                if (eq >= 0)
                {
                    return trimmed.Substring(eq + 1).Trim();
                }
            }
            else if (inGlobal && trimmed.StartsWith(ProxyKey + "=", StringComparison.Ordinal))
            {
                return trimmed.Substring(ProxyKey.Length + 1).Trim();
            }
        }

        return null;
    }

    private static void EnsureGlobalSection(List<string> lines)
    {
        var hasGlobal = lines.Any(l => string.Equals(l.Trim(), GlobalSection, StringComparison.OrdinalIgnoreCase));
        if (!hasGlobal)
        {
            lines.Insert(0, GlobalSection);
        }
    }

    private static void SetOrAddProxy(List<string> lines, string value)
    {
        var inGlobal = false;
        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                inGlobal = string.Equals(trimmed, GlobalSection, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (inGlobal && (trimmed.StartsWith(ProxyKey + " ", StringComparison.Ordinal) ||
                             trimmed.StartsWith(ProxyKey + "=", StringComparison.Ordinal)))
            {
                lines[i] = $"{ProxyKey} = {value}";
                return;
            }
        }

        var globalIndex = lines.FindIndex(l => string.Equals(l.Trim(), GlobalSection, StringComparison.OrdinalIgnoreCase));
        lines.Insert(globalIndex + 1, $"{ProxyKey} = {value}");
    }

    private static void RemoveProxy(List<string> lines)
    {
        var inGlobal = false;
        var toRemove = new List<int>();
        for (var i = 0; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                inGlobal = string.Equals(trimmed, GlobalSection, StringComparison.OrdinalIgnoreCase);
                continue;
            }

            if (inGlobal && (trimmed.StartsWith(ProxyKey + " ", StringComparison.Ordinal) ||
                             trimmed.StartsWith(ProxyKey + "=", StringComparison.Ordinal)))
            {
                toRemove.Add(i);
            }
        }

        // Remove in reverse order to preserve indices
        for (var j = toRemove.Count - 1; j >= 0; j--)
        {
            lines.RemoveAt(toRemove[j]);
        }
    }

    private static void EnsureDirectory(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    private static string? DefaultResolvePipPath()
    {
        var env = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var entry in env.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = entry.Trim();
            var exe = Path.Combine(trimmed, "pip.exe");
            if (File.Exists(exe)) return exe;
            var bare = Path.Combine(trimmed, "pip");
            if (File.Exists(bare)) return bare;
        }

        return null;
    }

    private static string DefaultGetPipIniPath()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "pip", "pip.ini");
    }
}
