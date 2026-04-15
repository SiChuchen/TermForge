using System;
using System.IO;
using TermForge.Contracts;
using TermForge.Platform;
using TermForge.Platform.Windows;
using Xunit;

namespace TermForge.Core.Tests;

public class PipProxyAdapterTests
{
    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "termforge-pip-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static WindowsPipProxyAdapter CreateAdapter(string tempDir, bool pipExists = true)
    {
        var pipIniPath = Path.Combine(tempDir, "pip.ini");
        return new WindowsPipProxyAdapter(
            resolvePipPath: () => pipExists ? "pip.exe" : null,
            getPipIniPath: () => pipIniPath);
    }

    [Fact]
    public void IsAvailable_returns_true_when_pip_found()
    {
        using var dir = new TempDir();
        var adapter = CreateAdapter(dir.Path, pipExists: true);
        Assert.True(adapter.IsAvailable());
    }

    [Fact]
    public void IsAvailable_returns_false_when_pip_missing()
    {
        using var dir = new TempDir();
        var adapter = CreateAdapter(dir.Path, pipExists: false);
        Assert.False(adapter.IsAvailable());
    }

    [Fact]
    public void ReadCurrent_returns_empty_when_pip_ini_missing()
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
    public void ReadCurrent_parses_global_proxy_key()
    {
        using var dir = new TempDir();
        var adapter = CreateAdapter(dir.Path);
        var pipIniPath = Path.Combine(dir.Path, "pip.ini");
        File.WriteAllText(pipIniPath, "[global]\nproxy = http://127.0.0.1:7890\n");

        var result = adapter.ReadCurrent();

        Assert.True(result.Enabled);
        Assert.Equal("http://127.0.0.1:7890", result.Http);
        Assert.Equal("http://127.0.0.1:7890", result.Https);
        Assert.Equal(string.Empty, result.NoProxy);
    }

    [Fact]
    public void Apply_writes_proxy_to_global_section()
    {
        using var dir = new TempDir();
        var adapter = CreateAdapter(dir.Path);
        var desired = new ProxyConfigSnapshot(true, "http://proxy:8080", "http://proxy:8443", "ignored");

        var result = adapter.Apply(desired);

        Assert.True(result.Enabled);
        Assert.Equal("http://proxy:8443", result.Http);

        var pipIniPath = Path.Combine(dir.Path, "pip.ini");
        Assert.True(File.Exists(pipIniPath));
        var content = File.ReadAllText(pipIniPath);
        Assert.Contains("[global]", content);
        Assert.Contains("proxy = http://proxy:8443", content);
    }

    [Fact]
    public void Apply_removes_proxy_when_disabled()
    {
        using var dir = new TempDir();
        var adapter = CreateAdapter(dir.Path);
        var pipIniPath = Path.Combine(dir.Path, "pip.ini");
        File.WriteAllText(pipIniPath, "[global]\nproxy = http://old:8080\n");

        var result = adapter.Apply(new ProxyConfigSnapshot(false, "", "", ""));

        Assert.False(result.Enabled);
        var content = File.ReadAllText(pipIniPath);
        Assert.DoesNotContain("proxy", content);
        Assert.Contains("[global]", content);
    }

    [Fact]
    public void Apply_creates_global_section_if_missing()
    {
        using var dir = new TempDir();
        var adapter = CreateAdapter(dir.Path);
        var pipIniPath = Path.Combine(dir.Path, "pip.ini");
        File.WriteAllText(pipIniPath, "[install]\nno-deps = true\n");

        adapter.Apply(new ProxyConfigSnapshot(true, "http://p:7890", "http://p:7890", ""));

        var content = File.ReadAllText(pipIniPath);
        Assert.Contains("[global]", content);
        Assert.Contains("proxy = http://p:7890", content);
        Assert.Contains("[install]", content);
    }

    [Fact]
    public void Verify_throws_on_mismatch()
    {
        using var dir = new TempDir();
        var adapter = CreateAdapter(dir.Path);
        var pipIniPath = Path.Combine(dir.Path, "pip.ini");
        File.WriteAllText(pipIniPath, "[global]\nproxy = http://different:9999\n");

        var desired = new ProxyConfigSnapshot(true, "http://expected:7890", "http://expected:7890", "");

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
    }

    [Fact]
    public void Rollback_restores_previous_state()
    {
        using var dir = new TempDir();
        var adapter = CreateAdapter(dir.Path);
        var before = new ProxyConfigSnapshot(true, "http://before:8080", "http://before:8080", "");
        adapter.Apply(before);
        adapter.Apply(new ProxyConfigSnapshot(true, "http://after:9999", "http://after:9999", ""));

        var result = adapter.Rollback(before);

        Assert.Equal("http://before:8080", result.Http);
    }

    [Fact]
    public void PlanEnable_uses_https_value_for_proxy_key()
    {
        using var dir = new TempDir();
        var adapter = CreateAdapter(dir.Path);
        var desired = adapter.PlanEnable("http://p:8080", "http://p:8443", "ignored");

        Assert.True(desired.Enabled);
        Assert.Equal("http://p:8443", desired.Http);
    }

    [Fact]
    public void TargetName_returns_pip()
    {
        using var dir = new TempDir();
        var adapter = CreateAdapter(dir.Path);
        Assert.Equal("pip", adapter.TargetName);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; }
        public TempDir() { Path = CreateTempDir(); }
        public void Dispose() { Directory.Delete(Path, true); }
    }
}
