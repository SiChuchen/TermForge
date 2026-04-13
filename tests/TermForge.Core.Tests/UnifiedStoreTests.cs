using System;
using System.IO;
using System.Reflection;
using TermForge.Contracts;
using Xunit;

namespace TermForge.Core.Tests;

public class UnifiedStoreTests
{
    [Fact]
    public void PlanRecord_can_wrap_git_plan_payload()
    {
        var payload = new GitProxyPlan(
            Target: "git",
            Mode: "enable",
            Before: new GitProxySnapshot(true, "global", "", "", ""),
            Desired: new GitProxySnapshot(true, "global", "http://127.0.0.1:7890", "http://127.0.0.1:7890", "127.0.0.1,localhost,::1"),
            Actions: new[]
            {
                new GitProxyPlanAction("http.proxy", "set", "", "http://127.0.0.1:7890")
            });

        var record = new PlanRecord(
            PlanId: "plan-1",
            Target: "git",
            SchemaVersion: "2026-04-13",
            CreatedAt: "2026-04-13 12:00:00",
            PayloadType: "git-proxy-plan",
            Payload: payload);

        Assert.Equal("git", record.Target);
        Assert.Equal("git-proxy-plan", record.PayloadType);
    }

    [Fact]
    public void UnifiedStore_round_trips_git_plan_and_env_change_records()
    {
        var root = Path.Combine(Path.GetTempPath(), "termforge-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var planStore = UnifiedStoreHarness.CreatePlanStore(Path.Combine(root, "plans.json"));
            var ledger = UnifiedStoreHarness.CreateLedger(Path.Combine(root, "ledger.json"));

            var gitPlan = new PlanRecord(
                "plan-git",
                "git",
                "2026-04-13",
                "2026-04-13 12:00:00",
                "git-proxy-plan",
                new GitProxyPlan(
                    "git",
                    "enable",
                    new GitProxySnapshot(true, "global", "", "", ""),
                    new GitProxySnapshot(true, "global", "http://127.0.0.1:7890", "http://127.0.0.1:7890", "127.0.0.1,localhost,::1"),
                    new[] { new GitProxyPlanAction("http.proxy", "set", "", "http://127.0.0.1:7890") }));

            var envChange = new ChangeRecord(
                "change-env",
                "env",
                "plan-env",
                "2026-04-13",
                "2026-04-13 12:05:00",
                "proxy-apply",
                new ProxyConfigSnapshot(false, "", "", ""),
                new ProxyConfigSnapshot(true, "http://127.0.0.1:7890", "http://127.0.0.1:7890", "127.0.0.1,localhost,::1"));

            planStore.SavePlanRecord(gitPlan);
            ledger.AppendChangeRecord(envChange);

            var storedPlan = (PlanRecord)planStore.GetPlanRecord("plan-git")!;
            var storedChange = (ChangeRecord)ledger.GetChangeRecord("change-env")!;
            var storedGitPayload = storedPlan.GetPayload<GitProxyPlan>();
            var storedEnvAfter = storedChange.GetAfter<ProxyConfigSnapshot>();

            Assert.Equal("git", storedPlan.Target);
            Assert.Equal("git-proxy-plan", storedPlan.PayloadType);
            Assert.Equal("http://127.0.0.1:7890", storedGitPayload.Desired.HttpProxy);
            Assert.Equal("env", storedChange.Target);
            Assert.Equal("http://127.0.0.1:7890", storedEnvAfter.Http);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}

internal static class UnifiedStoreHarness
{
    public static dynamic CreatePlanStore(string path)
    {
        var assembly = Assembly.LoadFrom(GetWindowsAssemblyPath());
        var type = assembly.GetType("TermForge.Platform.Windows.JsonPlanStore", throwOnError: true)!;
        return Activator.CreateInstance(type, path)!;
    }

    public static dynamic CreateLedger(string path)
    {
        var assembly = Assembly.LoadFrom(GetWindowsAssemblyPath());
        var type = assembly.GetType("TermForge.Platform.Windows.JsonOperationLedger", throwOnError: true)!;
        return Activator.CreateInstance(type, path)!;
    }

    private static string GetWindowsAssemblyPath()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "TermForge.Platform.Windows", "bin", "Debug", "net8.0", "TermForge.Platform.Windows.dll"));
    }
}
