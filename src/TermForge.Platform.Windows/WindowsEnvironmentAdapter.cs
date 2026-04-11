using TermForge.Contracts;
using TermForge.Platform;

namespace TermForge.Platform.Windows;

public sealed class WindowsEnvironmentAdapter : IPlatformEnvironmentAdapter
{
    public ProxyConfigSnapshot ReadEnvironmentProxy()
    {
        var http = ReadFirst("http_proxy", "HTTP_PROXY");
        var https = ReadFirst("https_proxy", "HTTPS_PROXY");
        var noProxy = ReadFirst("no_proxy", "NO_PROXY");
        var enabled = !string.IsNullOrWhiteSpace(http) || !string.IsNullOrWhiteSpace(https);

        return new ProxyConfigSnapshot(enabled, http, https, noProxy);
    }

    public void ApplyEnvironmentProxy(ProxyConfigSnapshot snapshot)
    {
        if (!snapshot.Enabled)
        {
            Clear("http_proxy", "HTTP_PROXY", "https_proxy", "HTTPS_PROXY", "no_proxy", "NO_PROXY");
            return;
        }

        var normalized = new ProxyConfigSnapshot(
            Enabled: true,
            Http: snapshot.Http.Trim(),
            Https: string.IsNullOrWhiteSpace(snapshot.Https) ? snapshot.Http.Trim() : snapshot.Https.Trim(),
            NoProxy: snapshot.NoProxy.Trim());

        Write("http_proxy", normalized.Http);
        Write("HTTP_PROXY", normalized.Http);
        Write("https_proxy", normalized.Https);
        Write("HTTPS_PROXY", normalized.Https);
        Write("no_proxy", normalized.NoProxy);
        Write("NO_PROXY", normalized.NoProxy);
    }

    private static string ReadFirst(params string[] names)
    {
        foreach (var name in names)
        {
            var userValue = Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User);
            if (!string.IsNullOrWhiteSpace(userValue))
            {
                return userValue;
            }

            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return string.Empty;
    }

    private static void Write(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            Environment.SetEnvironmentVariable(name, null);
            Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.User);
            return;
        }

        Environment.SetEnvironmentVariable(name, value);
        Environment.SetEnvironmentVariable(name, value, EnvironmentVariableTarget.User);
    }

    private static void Clear(params string[] names)
    {
        foreach (var name in names)
        {
            Environment.SetEnvironmentVariable(name, null);
            Environment.SetEnvironmentVariable(name, null, EnvironmentVariableTarget.User);
        }
    }
}
