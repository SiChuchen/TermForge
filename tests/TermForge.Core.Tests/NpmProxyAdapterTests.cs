using System;
using System.IO;
using TermForge.Contracts;
using TermForge.Platform;
using TermForge.Platform.Windows;
using Xunit;

namespace TermForge.Core.Tests;

public class NpmProxyAdapterTests
{
    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "termforge-npm-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static WindowsNpmProxyAdapter CreateAdapter(string tempDir, bool npmExists = true)
    {
        var npmrcPath = Path.Combine(tempDir, ".npmrc");
        return new WindowsNpmProxyAdapter(
            resolveNpmPath: () => npmExists ? "npm.cmd" : null,
            getNpmrcPath: () => npmrcPath);
    }

    [Fact]
    public void IsAvailable_returns_true_when_npm_found()
    {
        using var dir = new TempDir();
        var adapter = CreateAdapter(dir.Path, npmExists: true);
        Assert.True(adapter.IsAvailable());
    }

    [Fact]
    public void IsAvailable_returns_false_when_npm_missing()
    {
        using var dir = new TempDir();
        var adapter = CreateAdapter(dir.Path, npmExists: false);
        Assert.False(adapter.IsAvailable());
    }

    [Fact]
    public void ReadCurrent_returns_empty_when_npmrc_missing()
    {
        using var dir = new TempDir();
        var adapter = CreateAdapter(dir.Path);

        var result = adapter.ReadCurrent();

        Assert.False(result.Enabled);
        Assert.Equal(string.Empty, result.Http);
        Assert.Equal(string.Empty, result.Https);
        Assert.Equal(string.Empty, result.NoProxy);
    }

    [Fact]
    public void ReadCurrent_parses_existing_proxy_keys()
    {
        using var dir = new TempDir();
        var adapter = CreateAdapter(dir.Path);
        var npmrcPath = Path.Combine(dir.Path, ".npmrc");
        File.WriteAllLines(npmrcPath, new[]
        {
            "proxy=http://127.0.0.1:7890",
            "https-proxy=http://127.0.0.1:7890",
            "noproxy=127.0.0.1,localhost,::1"
        });

        var result = adapter.ReadCurrent();

        Assert.True(result.Enabled);
        Assert.Equal("http://127.0.0.1:7890", result.Http);
        Assert.Equal("http://127.0.0.1:7890", result.Https);
        Assert.Equal("127.0.0.1,localhost,::1", result.NoProxy);
    }

    [Fact]
    public void Apply_writes_proxy_keys_to_npmrc()
    {
        using var dir = new TempDir();
        var adapter = CreateAdapter(dir.Path);
        var desired = new ProxyConfigSnapshot(true, "http://proxy:8080", "http://proxy:8443", "localhost");

        var result = adapter.Apply(desired);

        Assert.True(result.Enabled);
        Assert.Equal("http://proxy:8080", result.Http);
        Assert.Equal("http://proxy:8443", result.Https);
        Assert.Equal("localhost", result.NoProxy);

        var npmrcPath = Path.Combine(dir.Path, ".npmrc");
        Assert.True(File.Exists(npmrcPath));
        var content = File.ReadAllText(npmrcPath);
        Assert.Contains("proxy=http://proxy:8080", content);
        Assert.Contains("https-proxy=http://proxy:8443", content);
        Assert.Contains("noproxy=localhost", content);
    }

    [Fact]
    public void Apply_removes_proxy_keys_when_disabled()
    {
        using var dir = new TempDir();
        var adapter = CreateAdapter(dir.Path);
        var npmrcPath = Path.Combine(dir.Path, ".npmrc");
        File.WriteAllLines(npmrcPath, new[]
        {
            "proxy=http://old:8080",
            "https-proxy=http://old:8443",
            "noproxy=old.local"
        });

        var result = adapter.Apply(new ProxyConfigSnapshot(false, "", "", ""));

        Assert.False(result.Enabled);
        var content = File.ReadAllText(npmrcPath);
        Assert.DoesNotContain("proxy=", content);
        Assert.DoesNotContain("https-proxy=", content);
        Assert.DoesNotContain("noproxy=", content);
    }

    [Fact]
    public void Apply_preserves_existing_npmrc_content()
    {
        using var dir = new TempDir();
        var adapter = CreateAdapter(dir.Path);
        var npmrcPath = Path.Combine(dir.Path, ".npmrc");
        File.WriteAllLines(npmrcPath, new[]
        {
            "registry=https://registry.npmjs.org/",
            "proxy=http://old:8080",
            "save-exact=true"
        });

        adapter.Apply(new ProxyConfigSnapshot(true, "http://new:7890", "http://new:7890", ""));

        var content = File.ReadAllText(npmrcPath);
        Assert.Contains("registry=https://registry.npmjs.org/", content);
        Assert.Contains("save-exact=true", content);
        Assert.Contains("proxy=http://new:7890", content);
    }

    [Fact]
    public void Verify_throws_on_mismatch()
    {
        using var dir = new TempDir();
        var adapter = CreateAdapter(dir.Path);
        var npmrcPath = Path.Combine(dir.Path, ".npmrc");
        File.WriteAllLines(npmrcPath, new[] { "proxy=http://different:9999" });

        var desired = new ProxyConfigSnapshot(true, "http://expected:7890", "", "");

        Assert.Throws<InvalidOperationException>(() => adapter.Verify(desired));
    }

    [Fact]
    public void Verify_passes_when_state_matches()
    {
        using var dir = new TempDir();
        var adapter = CreateAdapter(dir.Path);
        var desired = new ProxyConfigSnapshot(true, "http://127.0.0.1:7890", "http://127.0.0.1:7890", "");
        adapter.Apply(desired);

        var result = adapter.Verify(desired);

        Assert.True(result.Enabled);
        Assert.Equal("http://127.0.0.1:7890", result.Http);
    }

    [Fact]
    public void Rollback_restores_previous_state()
    {
        using var dir = new TempDir();
        var adapter = CreateAdapter(dir.Path);
        var before = new ProxyConfigSnapshot(true, "http://before:8080", "http://before:8443", "before.local");
        adapter.Apply(before);
        adapter.Apply(new ProxyConfigSnapshot(true, "http://after:9999", "", ""));

        var result = adapter.Rollback(before);

        Assert.Equal("http://before:8080", result.Http);
        Assert.Equal("http://before:8443", result.Https);
        Assert.Equal("before.local", result.NoProxy);
    }

    [Fact]
    public void PlanEnable_returns_desired_snapshot_without_writing()
    {
        using var dir = new TempDir();
        var adapter = CreateAdapter(dir.Path);
        var desired = adapter.PlanEnable("http://p:8080", "http://p:8443", "noproxy");

        Assert.True(desired.Enabled);
        Assert.Equal("http://p:8080", desired.Http);
        Assert.Equal("http://p:8443", desired.Https);
        Assert.Equal("noproxy", desired.NoProxy);

        var npmrcPath = Path.Combine(dir.Path, ".npmrc");
        Assert.False(File.Exists(npmrcPath));
    }

    [Fact]
    public void PlanDisable_returns_disabled_snapshot()
    {
        using var dir = new TempDir();
        var adapter = CreateAdapter(dir.Path);
        var desired = adapter.PlanDisable();

        Assert.False(desired.Enabled);
        Assert.Equal(string.Empty, desired.Http);
    }

    [Fact]
    public void TargetName_returns_npm()
    {
        using var dir = new TempDir();
        var adapter = CreateAdapter(dir.Path);
        Assert.Equal("npm", adapter.TargetName);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; }
        public TempDir() { Path = CreateTempDir(); }
        public void Dispose() { Directory.Delete(Path, true); }
    }
}
