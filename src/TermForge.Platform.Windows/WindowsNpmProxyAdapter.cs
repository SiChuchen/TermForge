using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TermForge.Contracts;
using TermForge.Platform;

namespace TermForge.Platform.Windows;

public sealed class WindowsNpmProxyAdapter : IProxyTargetAdapter
{
    private const string ProxyKey = "proxy";
    private const string HttpsProxyKey = "https-proxy";
    private const string NoProxyKey = "noproxy";

    private static readonly HashSet<string> ManagedKeys = new(StringComparer.Ordinal)
    {
        ProxyKey, HttpsProxyKey, NoProxyKey
    };

    private readonly Func<string?> _resolveNpmPath;
    private readonly Func<string> _getNpmrcPath;

    public string TargetName => "npm";

    public WindowsNpmProxyAdapter(
        Func<string?>? resolveNpmPath = null,
        Func<string>? getNpmrcPath = null)
    {
        _resolveNpmPath = resolveNpmPath ?? DefaultResolveNpmPath;
        _getNpmrcPath = getNpmrcPath ?? DefaultGetNpmrcPath;
    }

    public bool IsAvailable() => _resolveNpmPath() is not null;

    public ProxyConfigSnapshot ReadCurrent()
    {
        var path = _getNpmrcPath();
        if (!File.Exists(path))
        {
            return new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty);
        }

        var lines = File.ReadAllLines(path);
        var http = FindValue(lines, ProxyKey);
        var https = FindValue(lines, HttpsProxyKey);
        var noProxy = FindValue(lines, NoProxyKey);
        var enabled = !string.IsNullOrWhiteSpace(http) || !string.IsNullOrWhiteSpace(https);
        return new ProxyConfigSnapshot(enabled, http, https, noProxy);
    }

    public ProxyConfigSnapshot PlanEnable(string http, string https, string noProxy)
    {
        return new ProxyConfigSnapshot(true, http, https, noProxy);
    }

    public ProxyConfigSnapshot PlanDisable()
    {
        return new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty);
    }

    public ProxyConfigSnapshot Apply(ProxyConfigSnapshot desired)
    {
        var path = _getNpmrcPath();
        var lines = File.Exists(path)
            ? File.ReadAllLines(path).ToList()
            : new List<string>();

        if (desired.Enabled)
        {
            SetOrAdd(lines, ProxyKey, desired.Http);
            SetOrAdd(lines, HttpsProxyKey, desired.Https);
            SetOrAdd(lines, NoProxyKey, desired.NoProxy);
        }
        else
        {
            RemoveManaged(lines);
        }

        EnsureDirectory(path);
        File.WriteAllLines(path, lines);
        return ReadCurrent();
    }

    public ProxyConfigSnapshot Verify(ProxyConfigSnapshot desired)
    {
        var current = ReadCurrent();
        if (Normalize(current) != Normalize(desired))
        {
            throw new InvalidOperationException("npm proxy verification failed");
        }

        return current;
    }

    public ProxyConfigSnapshot Rollback(ProxyConfigSnapshot before)
    {
        return Apply(before);
    }

    private static string FindValue(string[] lines, string key)
    {
        var prefix = key + "=";
        var line = lines.FirstOrDefault(l => l.StartsWith(prefix, StringComparison.Ordinal));
        return line is null ? string.Empty : line.Substring(prefix.Length);
    }

    private static void SetOrAdd(List<string> lines, string key, string value)
    {
        var prefix = key + "=";
        var index = lines.FindIndex(l => l.StartsWith(prefix, StringComparison.Ordinal));
        var entry = key + "=" + value;

        if (index >= 0)
        {
            lines[index] = entry;
        }
        else
        {
            lines.Add(entry);
        }
    }

    private static void RemoveManaged(List<string> lines)
    {
        lines.RemoveAll(l =>
        {
            var eq = l.IndexOf('=');
            if (eq < 0) return false;
            return ManagedKeys.Contains(l.Substring(0, eq));
        });
    }

    private static ProxyConfigSnapshot Normalize(ProxyConfigSnapshot s)
    {
        var http = s.Http?.Trim() ?? string.Empty;
        var https = string.IsNullOrWhiteSpace(s.Https) ? http : s.Https.Trim();
        var noProxy = s.NoProxy?.Trim() ?? string.Empty;
        return new ProxyConfigSnapshot(s.Enabled, http, https, noProxy);
    }

    private static void EnsureDirectory(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    private static string? DefaultResolveNpmPath()
    {
        var env = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var entry in env.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = entry.Trim();
            var cmd = Path.Combine(trimmed, "npm.cmd");
            if (File.Exists(cmd)) return cmd;
            var bare = Path.Combine(trimmed, "npm");
            if (File.Exists(bare)) return bare;
        }

        return null;
    }

    private static string DefaultGetNpmrcPath()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".npmrc");
    }
}
