using System;
using System.Collections.Generic;
using System.IO;
using TermForge.Contracts;
using TermForge.Core.Interfaces;
using TermForge.Platform;
using TermForge.Platform.Windows;
using Xunit;

namespace TermForge.Core.Tests;

public class ProxyWorkflowServiceTests
{
    [Fact]
    public void ProxyWorkflowService_plans_env_enable_operation()
    {
        var configStore = new FakeConfigStore();
        var planStore = new FakePlanStore();
        var ledger = new FakeOperationLedger();
        var environment = new FakePlatformEnvironmentAdapter(
            new ProxyConfigSnapshot(true, "http://env:8080", "http://env:8443", "env.local"));
        var clock = new FakeClock();

        var service = new TermForge.Core.Services.ProxyWorkflowService(configStore, planStore, ledger, environment, clock);

        var result = service.PlanEnable("http://127.0.0.1:7890", "http://127.0.0.1:7890", "127.0.0.1,localhost,::1");
        var payload = result.Payload.ToProxyPlanPayload();

        Assert.Equal("proxy.plan", result.Command);
        Assert.Equal("env", result.Payload.Target);
        Assert.True(payload.Desired.Enabled);
        Assert.Equal("http://env:8080", payload.Before.Http);
    }

    [Fact]
    public void ProxyWorkflowService_plans_env_disable_with_unified_record_contract()
    {
        var configStore = new FakeConfigStore();
        var planStore = new FakePlanStore();
        var ledger = new FakeOperationLedger();
        var environment = new FakePlatformEnvironmentAdapter(
            new ProxyConfigSnapshot(true, "http://env:8080", "http://env:8443", "env.local"));
        var clock = new FakeClock();

        var service = new TermForge.Core.Services.ProxyWorkflowService(configStore, planStore, ledger, environment, clock);

        var result = service.PlanDisable();
        var payload = result.Payload.ToProxyPlanPayload();

        Assert.Equal("proxy.plan", result.Command);
        Assert.Equal("2026-04-13", result.Payload.SchemaVersion);
        Assert.Equal("proxy-plan", result.Payload.PayloadType);
        Assert.Equal("env", result.Payload.Target);
        Assert.Equal("disable", payload.Mode);
        Assert.False(payload.Desired.Enabled);
        Assert.Equal("http://env:8080", payload.Before.Http);
    }

    [Fact]
    public void ProxyWorkflowService_apply_and_rollback_use_live_environment_state()
    {
        var configStore = new FakeConfigStore();
        var planStore = new FakePlanStore();
        var ledger = new FakeOperationLedger();
        var environment = new FakePlatformEnvironmentAdapter(
            new ProxyConfigSnapshot(true, "http://before:8080", "http://before:8443", "before.local"));
        var clock = new FakeClock();
        var service = new TermForge.Core.Services.ProxyWorkflowService(configStore, planStore, ledger, environment, clock);

        var plan = service.PlanEnable("http://target:7890", "", "127.0.0.1");
        var apply = service.Apply(plan.Payload.PlanId);
        var afterApply = environment.Current;
        var rollback = service.Rollback(apply.Payload.ChangeId);
        var applyAfter = apply.Payload.GetAfter<ProxyConfigSnapshot>();
        var rollbackAfter = rollback.Payload.GetAfter<ProxyConfigSnapshot>();

        Assert.Equal("http://target:7890", afterApply.Http);
        Assert.Equal("http://target:7890", applyAfter.Https);
        Assert.Equal("http://before:8080", rollbackAfter.Http);
        Assert.Equal("http://before:8443", rollbackAfter.Https);
    }

    [Fact]
    public void ProxyWorkflowService_generates_unique_ids_with_same_clock_tick()
    {
        var configStore = new FakeConfigStore();
        var planStore = new FakePlanStore();
        var ledger = new FakeOperationLedger();
        var environment = new FakePlatformEnvironmentAdapter();
        var clock = new FakeClock { Value = "2026-04-11T00:00:00Z" };
        var service = new TermForge.Core.Services.ProxyWorkflowService(configStore, planStore, ledger, environment, clock);

        var first = service.PlanEnable("http://one", "", "");
        var second = service.PlanEnable("http://two", "", "");

        Assert.NotEqual(first.Payload.PlanId, second.Payload.PlanId);
    }

    [Fact]
    public void ProxyWorkflowService_GitProxy_can_plan_git_enable_operation()
    {
        var configStore = new FakeConfigStore();
        var planStore = new FakePlanStore();
        var ledger = new FakeOperationLedger();
        var environment = new FakePlatformEnvironmentAdapter();
        var clock = new FakeClock();
        var gitAdapter = new FakeGitProxyAdapter(
            new GitProxySnapshot(true, "global", "", "", ""));

        var service = new TermForge.Core.Services.ProxyWorkflowService(
            configStore,
            planStore,
            ledger,
            environment,
            clock,
            gitAdapter);

        var result = service.PlanGitEnable(
            "http://127.0.0.1:7890",
            "http://127.0.0.1:7890",
            "127.0.0.1,localhost,::1");
        var payload = result.Payload.GetPayload<GitProxyPlan>();

        Assert.Equal("proxy.plan", result.Command);
        Assert.Equal("git", result.Payload.Target);
        Assert.Equal("git", payload.Target);
    }

    [Fact]
    public void ProxyWorkflowService_GitProxy_can_plan_git_disable_operation()
    {
        var configStore = new FakeConfigStore();
        var planStore = new FakePlanStore();
        var ledger = new FakeOperationLedger();
        var environment = new FakePlatformEnvironmentAdapter();
        var clock = new FakeClock();
        var gitAdapter = new FakeGitProxyAdapter(
            new GitProxySnapshot(
                true,
                "global",
                "http://127.0.0.1:7890",
                "http://127.0.0.1:7890",
                "127.0.0.1,localhost,::1"));

        var service = new TermForge.Core.Services.ProxyWorkflowService(
            configStore,
            planStore,
            ledger,
            environment,
            clock,
            gitAdapter);

        var result = service.PlanGitDisable();
        var payload = result.Payload.GetPayload<GitProxyPlan>();

        Assert.Equal("proxy.plan", result.Command);
        Assert.Equal("git", result.Payload.Target);
        Assert.Equal("disable", payload.Mode);
        Assert.Equal(3, payload.Actions.Count);
    }

    [Fact]
    public void ProxyWorkflowService_plans_composite_env_git_enable_in_fixed_order()
    {
        var configStore = new FakeConfigStore
        {
            TargetFlags = new ProxyTargetFlags(Env: true, Git: true, Npm: false, Pip: false)
        };
        var service = new TermForge.Core.Services.ProxyWorkflowService(
            configStore,
            new FakePlanStore(),
            new FakeOperationLedger(),
            new FakePlatformEnvironmentAdapter(
                new ProxyConfigSnapshot(true, "http://env:8080", "http://env:8443", "env.local")),
            new FakeClock(),
            new FakeGitProxyAdapter(new GitProxySnapshot(true, "global", "", "", "")));

        var result = service.PlanCompositeEnable(
            "http://127.0.0.1:7890",
            "http://127.0.0.1:7890",
            "127.0.0.1,localhost,::1");

        var payload = result.Payload.GetPayload<CompositeProxyPlan>();

        Assert.Equal("composite", result.Payload.Target);
        Assert.Equal(new[] { "env", "git" }, payload.Targets);
        Assert.Collection(
            payload.Plans,
            plan => Assert.Equal("env", plan.Target),
            plan => Assert.Equal("git", plan.Target));
    }

    [Fact]
    public void ProxyWorkflowService_compensates_env_when_git_apply_fails_in_composite_apply()
    {
        var configStore = new FakeConfigStore
        {
            TargetFlags = new ProxyTargetFlags(Env: true, Git: true, Npm: false, Pip: false)
        };
        var planStore = new FakePlanStore();
        var ledger = new FakeOperationLedger();
        var environment = new FakePlatformEnvironmentAdapter(
            new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty));
        var gitAdapter = new InjectableGitProxyAdapter(new GitProxySnapshot(true, "global", "", "", ""))
        {
            ThrowOnApply = true
        };
        var service = new TermForge.Core.Services.ProxyWorkflowService(
            configStore,
            planStore,
            ledger,
            environment,
            new FakeClock(),
            gitAdapter);

        var plan = service.PlanCompositeEnable(
            "http://127.0.0.1:7890",
            "http://127.0.0.1:7890",
            "127.0.0.1,localhost,::1");

        var error = Assert.Throws<InvalidOperationException>(() => service.Apply(plan.Payload.PlanId));

        Assert.Contains("git", error.Message);
        Assert.False(environment.Current.Enabled);
        Assert.Equal(0, ledger.Count);
    }

    [Fact]
    public void ProxyWorkflowService_verifies_env_cleanup_when_env_apply_verification_fails_in_composite_apply()
    {
        var configStore = new FakeConfigStore
        {
            TargetFlags = new ProxyTargetFlags(Env: true, Git: true, Npm: false, Pip: false)
        };
        var planStore = new FakePlanStore();
        var ledger = new FakeOperationLedger();
        var environment = new FakePlatformEnvironmentAdapter(
            new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty));
        environment.EnqueueAppliedTransform(snapshot => new ProxyConfigSnapshot(snapshot.Enabled, snapshot.Http + "-broken", snapshot.Https, snapshot.NoProxy));
        environment.EnqueueAppliedTransform(snapshot => new ProxyConfigSnapshot(snapshot.Enabled, snapshot.Http + "-revert-broken", snapshot.Https, snapshot.NoProxy));
        var service = new TermForge.Core.Services.ProxyWorkflowService(
            configStore,
            planStore,
            ledger,
            environment,
            new FakeClock(),
            new InjectableGitProxyAdapter(new GitProxySnapshot(true, "global", "", "", "")));

        var plan = service.PlanCompositeEnable(
            "http://127.0.0.1:7890",
            "http://127.0.0.1:7890",
            "127.0.0.1,localhost,::1");

        var error = Assert.Throws<InvalidOperationException>(() => service.Apply(plan.Payload.PlanId));

        Assert.Contains("revert", error.Message);
        Assert.Equal(0, ledger.Count);
    }

    [Fact]
    public void ProxyWorkflowService_verifies_git_cleanup_when_git_apply_fails_in_composite_apply()
    {
        var configStore = new FakeConfigStore
        {
            TargetFlags = new ProxyTargetFlags(Env: true, Git: true, Npm: false, Pip: false)
        };
        var planStore = new FakePlanStore();
        var ledger = new FakeOperationLedger();
        var environment = new FakePlatformEnvironmentAdapter(
            new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty));
        var gitAdapter = new InjectableGitProxyAdapter(new GitProxySnapshot(true, "global", "", "", ""))
        {
            ThrowOnApply = true,
            CorruptRollback = true
        };
        var service = new TermForge.Core.Services.ProxyWorkflowService(
            configStore,
            planStore,
            ledger,
            environment,
            new FakeClock(),
            gitAdapter);

        var plan = service.PlanCompositeEnable(
            "http://127.0.0.1:7890",
            "http://127.0.0.1:7890",
            "127.0.0.1,localhost,::1");

        var error = Assert.Throws<InvalidOperationException>(() => service.Apply(plan.Payload.PlanId));

        Assert.Contains("verification failed", error.Message);
        Assert.False(environment.Current.Enabled);
        Assert.Equal(0, ledger.Count);
    }

    [Fact]
    public void ProxyWorkflowService_applies_composite_env_git_successfully()
    {
        var configStore = new FakeConfigStore
        {
            TargetFlags = new ProxyTargetFlags(Env: true, Git: true, Npm: false, Pip: false)
        };
        var planStore = new FakePlanStore();
        var ledger = new FakeOperationLedger();
        var environment = new FakePlatformEnvironmentAdapter(
            new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty));
        var gitAdapter = new InjectableGitProxyAdapter(new GitProxySnapshot(true, "global", "", "", ""));
        var service = new TermForge.Core.Services.ProxyWorkflowService(
            configStore,
            planStore,
            ledger,
            environment,
            new FakeClock(),
            gitAdapter);

        var plan = service.PlanCompositeEnable(
            "http://127.0.0.1:7890",
            "http://127.0.0.1:7890",
            "127.0.0.1,localhost,::1");

        var apply = service.Apply(plan.Payload.PlanId);
        var payload = apply.Payload.GetAfter<CompositeProxyChange>();

        Assert.Equal("composite", apply.Payload.Target);
        Assert.Equal("composite-proxy-change", apply.Payload.PayloadType);
        Assert.False(payload.RollbackTriggered);
        Assert.Null(payload.FailureTarget);
        Assert.Equal(new[] { "env", "git" }, payload.Targets);
        Assert.Collection(
            payload.Changes,
            change => Assert.Equal("env", change.Target),
            change => Assert.Equal("git", change.Target));
        Assert.True(environment.Current.Enabled);
        Assert.Equal("http://127.0.0.1:7890", gitAdapter.Current.HttpProxy);
        Assert.Equal(1, ledger.Count);
    }

    [Fact]
    public void ProxyWorkflowService_rolls_back_composite_change_in_reverse_order()
    {
        var configStore = new FakeConfigStore
        {
            TargetFlags = new ProxyTargetFlags(Env: true, Git: true, Npm: false, Pip: false)
        };
        var planStore = new FakePlanStore();
        var ledger = new FakeOperationLedger();
        var environment = new FakePlatformEnvironmentAdapter(
            new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty));
        var gitAdapter = new InjectableGitProxyAdapter(new GitProxySnapshot(true, "global", "", "", ""));
        var service = new TermForge.Core.Services.ProxyWorkflowService(
            configStore,
            planStore,
            ledger,
            environment,
            new FakeClock(),
            gitAdapter);

        var plan = service.PlanCompositeEnable(
            "http://127.0.0.1:7890",
            "http://127.0.0.1:7890",
            "127.0.0.1,localhost,::1");

        var apply = service.Apply(plan.Payload.PlanId);
        var rollback = service.Rollback(apply.Payload.ChangeId);
        var payload = rollback.Payload.GetAfter<CompositeProxyChange>();

        Assert.Equal("composite", rollback.Payload.Target);
        Assert.Equal("composite-proxy-change", rollback.Payload.PayloadType);
        Assert.Equal(new[] { "env", "git" }, payload.Targets);
        Assert.Collection(
            payload.Changes,
            change => Assert.Equal("git", change.Target),
            change => Assert.Equal("env", change.Target));
        Assert.False(environment.Current.Enabled);
        Assert.Equal(string.Empty, gitAdapter.Current.HttpProxy);
        Assert.Equal(2, ledger.Count);
    }

    [Fact]
    public void ProxyWorkflowService_applies_and_rolls_back_reloaded_composite_records_from_json_store()
    {
        var root = Path.Combine(Path.GetTempPath(), "termforge-composite-store-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var configStore = new FakeConfigStore
            {
                TargetFlags = new ProxyTargetFlags(Env: true, Git: true, Npm: false, Pip: false)
            };
            var environment = new FakePlatformEnvironmentAdapter(
                new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty));
            var gitAdapter = new InjectableGitProxyAdapter(new GitProxySnapshot(true, "global", "", "", ""));
            var clock = new FakeClock();
            var planStorePath = Path.Combine(root, "plans.json");
            var ledgerPath = Path.Combine(root, "ledger.json");

            var writerService = new TermForge.Core.Services.ProxyWorkflowService(
                configStore,
                new JsonPlanStore(planStorePath),
                new JsonOperationLedger(ledgerPath),
                environment,
                clock,
                gitAdapter);

            var plan = writerService.PlanCompositeEnable(
                "http://127.0.0.1:7890",
                "http://127.0.0.1:7890",
                "127.0.0.1,localhost,::1");

            var readerService = new TermForge.Core.Services.ProxyWorkflowService(
                configStore,
                new JsonPlanStore(planStorePath),
                new JsonOperationLedger(ledgerPath),
                environment,
                clock,
                gitAdapter);

            var apply = readerService.Apply(plan.Payload.PlanId);
            var applyPayload = apply.Payload.GetAfter<CompositeProxyChange>();

            var rollbackService = new TermForge.Core.Services.ProxyWorkflowService(
                configStore,
                new JsonPlanStore(planStorePath),
                new JsonOperationLedger(ledgerPath),
                environment,
                clock,
                gitAdapter);

            var rollback = rollbackService.Rollback(apply.Payload.ChangeId);
            var rollbackPayload = rollback.Payload.GetAfter<CompositeProxyChange>();

            Assert.Equal("composite", apply.Payload.Target);
            Assert.Collection(
                applyPayload.Changes,
                change => Assert.Equal("env", change.Target),
                change => Assert.Equal("git", change.Target));
            Assert.Equal("composite", rollback.Payload.Target);
            Assert.Collection(
                rollbackPayload.Changes,
                change => Assert.Equal("git", change.Target),
                change => Assert.Equal("env", change.Target));
            Assert.False(environment.Current.Enabled);
            Assert.Equal(string.Empty, gitAdapter.Current.HttpProxy);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void ProxyWorkflowService_can_apply_and_rollback_git_through_store_records()
    {
        var configStore = new FakeConfigStore();
        var planStore = new FakePlanStore();
        var ledger = new FakeOperationLedger();
        var environment = new FakePlatformEnvironmentAdapter();
        var clock = new FakeClock();
        var gitAdapter = new FakeGitProxyAdapter(
            new GitProxySnapshot(true, "global", "", "", ""));

        var service = new TermForge.Core.Services.ProxyWorkflowService(
            configStore,
            planStore,
            ledger,
            environment,
            clock,
            gitAdapter);

        var planEnvelope = service.PlanGitEnable(
            "http://127.0.0.1:7890",
            "http://127.0.0.1:7890",
            "127.0.0.1,localhost,::1");

        var applied = service.Apply(planEnvelope.Payload.PlanId);
        var rolledBack = service.Rollback(applied.Payload.ChangeId);
        var appliedSnapshot = applied.Payload.GetAfter<GitProxySnapshot>();
        var rolledBackSnapshot = rolledBack.Payload.GetAfter<GitProxySnapshot>();

        Assert.Equal("proxy.apply", applied.Command);
        Assert.Equal("proxy.rollback", rolledBack.Command);
        Assert.Equal("git", applied.Payload.Target);
        Assert.Equal("git", rolledBack.Payload.Target);
        Assert.Equal("http://127.0.0.1:7890", appliedSnapshot.HttpProxy);
        Assert.Equal(string.Empty, rolledBackSnapshot.HttpProxy);
    }

    [Fact]
    public void ProxyWorkflowService_GitProxy_apply_and_rollback_use_adapter_state()
    {
        var configStore = new FakeConfigStore();
        var planStore = new FakePlanStore();
        var ledger = new FakeOperationLedger();
        var environment = new FakePlatformEnvironmentAdapter();
        var clock = new FakeClock();
        var gitAdapter = new FakeGitProxyAdapter(
            new GitProxySnapshot(
                true,
                "global",
                "http://before:8080",
                "http://before:8443",
                "before.local"));

        var service = new TermForge.Core.Services.ProxyWorkflowService(
            configStore,
            planStore,
            ledger,
            environment,
            clock,
            gitAdapter);

        var plan = service.PlanGitEnable(
            "http://target:7890",
            "http://target:7891",
            "127.0.0.1");
        var gitPlan = plan.Payload.GetPayload<GitProxyPlan>();

        var apply = service.ApplyGit(gitPlan);
        var rollback = service.RollbackGit(gitPlan.Before);

        Assert.Equal("proxy.apply", apply.Command);
        Assert.Equal("http://target:7890", apply.Payload.HttpProxy);
        Assert.Equal("http://target:7891", apply.Payload.HttpsProxy);
        Assert.Equal("proxy.rollback", rollback.Command);
        Assert.Equal("http://before:8080", rollback.Payload.HttpProxy);
        Assert.Equal("http://before:8443", rollback.Payload.HttpsProxy);
    }

    [Fact]
    public void ScanTarget_returns_npm_current_state()
    {
        var npmAdapter = new FakeTargetProxyAdapter("npm",
            new ProxyConfigSnapshot(true, "http://npm:8080", "http://npm:8443", "npm.local"));
        var service = CreateServiceWithAdapters(npmAdapter: npmAdapter);

        var result = service.ScanTarget("npm");

        Assert.Equal("proxy.scan", result.Command);
        Assert.Equal("npm", result.Payload.Target);
        Assert.Equal("http://npm:8080", result.Payload.Config.Http);
        Assert.True(result.Payload.Config.Enabled);
    }

    [Fact]
    public void ScanTarget_returns_pip_current_state()
    {
        var pipAdapter = new FakeTargetProxyAdapter("pip",
            new ProxyConfigSnapshot(true, "http://pip:8080", "http://pip:8443", "pip.local"));
        var service = CreateServiceWithAdapters(pipAdapter: pipAdapter);

        var result = service.ScanTarget("pip");

        Assert.Equal("proxy.scan", result.Command);
        Assert.Equal("pip", result.Payload.Target);
        Assert.Equal("http://pip:8080", result.Payload.Config.Http);
        Assert.True(result.Payload.Config.Enabled);
    }

    [Fact]
    public void PlanTargetEnable_saves_npm_plan_record()
    {
        var npmAdapter = new FakeTargetProxyAdapter("npm",
            new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty));
        var planStore = new FakePlanStore();
        var service = CreateServiceWithAdapters(planStore: planStore, npmAdapter: npmAdapter);

        var result = service.PlanTargetEnable("npm", "http://127.0.0.1:7890", "http://127.0.0.1:7890", "127.0.0.1");

        Assert.Equal("proxy.plan", result.Command);
        Assert.Equal("npm", result.Payload.Target);
        Assert.Equal("target-proxy-plan", result.Payload.PayloadType);
        var payload = result.Payload.GetPayload<TargetProxyPlanPayload>();
        Assert.False(payload.Before.Enabled);
        Assert.True(payload.Desired.Enabled);
        Assert.Equal("http://127.0.0.1:7890", payload.Desired.Http);
        Assert.NotNull(planStore.GetPlanRecord(result.Payload.PlanId));
    }

    [Fact]
    public void PlanTargetEnable_saves_pip_plan_record()
    {
        var pipAdapter = new FakeTargetProxyAdapter("pip",
            new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty));
        var planStore = new FakePlanStore();
        var service = CreateServiceWithAdapters(planStore: planStore, pipAdapter: pipAdapter);

        var result = service.PlanTargetEnable("pip", "http://127.0.0.1:7890", "http://127.0.0.1:7890", "127.0.0.1");

        Assert.Equal("proxy.plan", result.Command);
        Assert.Equal("pip", result.Payload.Target);
        Assert.Equal("target-proxy-plan", result.Payload.PayloadType);
        var payload = result.Payload.GetPayload<TargetProxyPlanPayload>();
        Assert.True(payload.Desired.Enabled);
        Assert.Equal("http://127.0.0.1:7890", payload.Desired.Http);
        Assert.NotNull(planStore.GetPlanRecord(result.Payload.PlanId));
    }

    [Fact]
    public void PlanTargetDisable_saves_npm_disabled_plan()
    {
        var npmAdapter = new FakeTargetProxyAdapter("npm",
            new ProxyConfigSnapshot(true, "http://npm:8080", "http://npm:8443", "npm.local"));
        var planStore = new FakePlanStore();
        var service = CreateServiceWithAdapters(planStore: planStore, npmAdapter: npmAdapter);

        var result = service.PlanTargetDisable("npm");

        Assert.Equal("proxy.plan", result.Command);
        Assert.Equal("npm", result.Payload.Target);
        var payload = result.Payload.GetPayload<TargetProxyPlanPayload>();
        Assert.True(payload.Before.Enabled);
        Assert.False(payload.Desired.Enabled);
        Assert.Equal(string.Empty, payload.Desired.Http);
    }

    [Fact]
    public void Apply_reads_npm_plan_and_applies()
    {
        var npmAdapter = new FakeTargetProxyAdapter("npm",
            new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty));
        var service = CreateServiceWithAdapters(npmAdapter: npmAdapter);

        var plan = service.PlanTargetEnable("npm", "http://127.0.0.1:7890", "http://127.0.0.1:7890", "127.0.0.1");
        var apply = service.Apply(plan.Payload.PlanId);

        Assert.Equal("proxy.apply", apply.Command);
        Assert.Equal("npm", apply.Payload.Target);
        Assert.Equal("target-proxy-apply", apply.Payload.PayloadType);
        var after = apply.Payload.GetAfter<ProxyConfigSnapshot>();
        Assert.True(after.Enabled);
        Assert.Equal("http://127.0.0.1:7890", after.Http);
    }

    [Fact]
    public void Apply_reads_pip_plan_and_applies()
    {
        var pipAdapter = new FakeTargetProxyAdapter("pip",
            new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty));
        var service = CreateServiceWithAdapters(pipAdapter: pipAdapter);

        var plan = service.PlanTargetEnable("pip", "http://127.0.0.1:7890", "http://127.0.0.1:7890", "127.0.0.1");
        var apply = service.Apply(plan.Payload.PlanId);

        Assert.Equal("proxy.apply", apply.Command);
        Assert.Equal("pip", apply.Payload.Target);
        var after = apply.Payload.GetAfter<ProxyConfigSnapshot>();
        Assert.True(after.Enabled);
        Assert.Equal("http://127.0.0.1:7890", after.Http);
    }

    [Fact]
    public void Rollback_npm_restores_before_state()
    {
        var npmAdapter = new FakeTargetProxyAdapter("npm",
            new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty));
        var service = CreateServiceWithAdapters(npmAdapter: npmAdapter);

        var plan = service.PlanTargetEnable("npm", "http://127.0.0.1:7890", "http://127.0.0.1:7890", "127.0.0.1");
        var apply = service.Apply(plan.Payload.PlanId);
        var rollback = service.Rollback(apply.Payload.ChangeId);

        Assert.Equal("proxy.rollback", rollback.Command);
        Assert.Equal("npm", rollback.Payload.Target);
        Assert.Equal("target-proxy-rollback", rollback.Payload.PayloadType);
        var after = rollback.Payload.GetAfter<ProxyConfigSnapshot>();
        Assert.False(after.Enabled);
        Assert.Equal(string.Empty, after.Http);
    }

    [Fact]
    public void Rollback_pip_restores_before_state()
    {
        var pipAdapter = new FakeTargetProxyAdapter("pip",
            new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty));
        var service = CreateServiceWithAdapters(pipAdapter: pipAdapter);

        var plan = service.PlanTargetEnable("pip", "http://127.0.0.1:7890", "http://127.0.0.1:7890", "127.0.0.1");
        var apply = service.Apply(plan.Payload.PlanId);
        var rollback = service.Rollback(apply.Payload.ChangeId);

        Assert.Equal("proxy.rollback", rollback.Command);
        Assert.Equal("pip", rollback.Payload.Target);
        var after = rollback.Payload.GetAfter<ProxyConfigSnapshot>();
        Assert.False(after.Enabled);
        Assert.Equal(string.Empty, after.Http);
    }

    [Fact]
    public void Apply_throws_for_npm_plan_when_adapter_missing()
    {
        var service = CreateServiceWithAdapters();
        var plan = service.PlanEnable("http://127.0.0.1:7890", "http://127.0.0.1:7890", "127.0.0.1");

        // PlanEnable creates an env plan. We manually construct an npm plan to test missing adapter.
        var fakePlanStore = new FakePlanStore();
        var clock = new FakeClock();
        var planPayload = new TargetProxyPlanPayload(
            new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty),
            new ProxyConfigSnapshot(true, "http://127.0.0.1:7890", "http://127.0.0.1:7890", "127.0.0.1"));
        var record = new PlanRecord("plan-missing-npm", "npm", "2026-04-13", clock.NowText(), "target-proxy-plan", planPayload);
        fakePlanStore.SavePlanRecord(record);

        var serviceWithoutAdapter = new TermForge.Core.Services.ProxyWorkflowService(
            new FakeConfigStore(), fakePlanStore, new FakeOperationLedger(),
            new FakePlatformEnvironmentAdapter(), clock);

        var ex = Assert.Throws<InvalidOperationException>(() => serviceWithoutAdapter.Apply("plan-missing-npm"));
        Assert.Contains("npm", ex.Message);
    }

    private static TermForge.Core.Services.ProxyWorkflowService CreateServiceWithAdapters(
        FakePlanStore? planStore = null,
        FakeTargetProxyAdapter? npmAdapter = null,
        FakeTargetProxyAdapter? pipAdapter = null)
    {
        return new TermForge.Core.Services.ProxyWorkflowService(
            new FakeConfigStore(),
            planStore ?? new FakePlanStore(),
            new FakeOperationLedger(),
            new FakePlatformEnvironmentAdapter(),
            new FakeClock(),
            npmProxyAdapter: npmAdapter,
            pipProxyAdapter: pipAdapter);
    }

    private static TermForge.Core.Services.ProxyWorkflowService CreateServiceWithAllAdapters(
        FakeConfigStore? configStore = null,
        FakePlanStore? planStore = null,
        FakeOperationLedger? ledger = null,
        FakePlatformEnvironmentAdapter? environment = null,
        InjectableGitProxyAdapter? gitAdapter = null,
        FakeTargetProxyAdapter? npmAdapter = null,
        FakeTargetProxyAdapter? pipAdapter = null)
    {
        return new TermForge.Core.Services.ProxyWorkflowService(
            configStore ?? new FakeConfigStore(),
            planStore ?? new FakePlanStore(),
            ledger ?? new FakeOperationLedger(),
            environment ?? new FakePlatformEnvironmentAdapter(),
            new FakeClock(),
            gitAdapter,
            npmAdapter,
            pipAdapter);
    }

    [Fact]
    public void PlanCompositeEnable_includes_npm_when_target_flag_on()
    {
        var configStore = new FakeConfigStore
        {
            TargetFlags = new ProxyTargetFlags(Env: true, Git: false, Npm: true, Pip: false)
        };
        var npmAdapter = new FakeTargetProxyAdapter("npm",
            new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty));
        var service = CreateServiceWithAllAdapters(configStore: configStore, npmAdapter: npmAdapter);

        var result = service.PlanCompositeEnable("http://127.0.0.1:7890", "http://127.0.0.1:7890", "127.0.0.1");
        var payload = result.Payload.GetPayload<CompositeProxyPlan>();

        Assert.Contains(payload.Targets, t => t == "npm");
        Assert.Contains(payload.Plans, p => p.Target == "npm");
    }

    [Fact]
    public void PlanCompositeEnable_includes_pip_when_target_flag_on()
    {
        var configStore = new FakeConfigStore
        {
            TargetFlags = new ProxyTargetFlags(Env: true, Git: false, Npm: false, Pip: true)
        };
        var pipAdapter = new FakeTargetProxyAdapter("pip",
            new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty));
        var service = CreateServiceWithAllAdapters(configStore: configStore, pipAdapter: pipAdapter);

        var result = service.PlanCompositeEnable("http://127.0.0.1:7890", "http://127.0.0.1:7890", "127.0.0.1");
        var payload = result.Payload.GetPayload<CompositeProxyPlan>();

        Assert.Contains(payload.Targets, t => t == "pip");
        Assert.Contains(payload.Plans, p => p.Target == "pip");
    }

    [Fact]
    public void PlanCompositeEnable_skips_npm_when_target_flag_off()
    {
        var configStore = new FakeConfigStore
        {
            TargetFlags = new ProxyTargetFlags(Env: true, Git: true, Npm: false, Pip: false)
        };
        var npmAdapter = new FakeTargetProxyAdapter("npm",
            new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty));
        var gitAdapter = new InjectableGitProxyAdapter(new GitProxySnapshot(true, "global", "", "", ""));
        var service = CreateServiceWithAllAdapters(configStore: configStore, gitAdapter: gitAdapter, npmAdapter: npmAdapter);

        var result = service.PlanCompositeEnable("http://127.0.0.1:7890", "http://127.0.0.1:7890", "127.0.0.1");
        var payload = result.Payload.GetPayload<CompositeProxyPlan>();

        Assert.DoesNotContain(payload.Targets, t => t == "npm");
    }

    [Fact]
    public void PlanCompositeEnable_skips_npm_when_adapter_missing()
    {
        var configStore = new FakeConfigStore
        {
            TargetFlags = new ProxyTargetFlags(Env: true, Git: true, Npm: true, Pip: false)
        };
        // No npm adapter provided
        var gitAdapter = new InjectableGitProxyAdapter(new GitProxySnapshot(true, "global", "", "", ""));
        var service = CreateServiceWithAllAdapters(configStore: configStore, gitAdapter: gitAdapter);

        var result = service.PlanCompositeEnable("http://127.0.0.1:7890", "http://127.0.0.1:7890", "127.0.0.1");
        var payload = result.Payload.GetPayload<CompositeProxyPlan>();

        Assert.DoesNotContain(payload.Targets, t => t == "npm");
    }

    [Fact]
    public void ApplyComposite_applies_env_git_npm_in_order()
    {
        var configStore = new FakeConfigStore
        {
            TargetFlags = new ProxyTargetFlags(Env: true, Git: true, Npm: true, Pip: false)
        };
        var ledger = new FakeOperationLedger();
        var environment = new FakePlatformEnvironmentAdapter(
            new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty));
        var gitAdapter = new InjectableGitProxyAdapter(new GitProxySnapshot(true, "global", "", "", ""));
        var npmAdapter = new FakeTargetProxyAdapter("npm",
            new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty));
        var service = CreateServiceWithAllAdapters(
            configStore: configStore, ledger: ledger, environment: environment,
            gitAdapter: gitAdapter, npmAdapter: npmAdapter);

        var plan = service.PlanCompositeEnable("http://127.0.0.1:7890", "http://127.0.0.1:7890", "127.0.0.1");
        var apply = service.Apply(plan.Payload.PlanId);
        var payload = apply.Payload.GetAfter<CompositeProxyChange>();

        Assert.Equal("composite", apply.Payload.Target);
        Assert.True(environment.Current.Enabled);
        Assert.Equal("http://127.0.0.1:7890", gitAdapter.Current.HttpProxy);
        Assert.True(npmAdapter.Current.Enabled);
        Assert.Equal("http://127.0.0.1:7890", npmAdapter.Current.Http);
        Assert.Equal(1, ledger.Count);
    }

    [Fact]
    public void ApplyComposite_compensates_on_npm_failure()
    {
        var configStore = new FakeConfigStore
        {
            TargetFlags = new ProxyTargetFlags(Env: true, Git: true, Npm: true, Pip: false)
        };
        var ledger = new FakeOperationLedger();
        var environment = new FakePlatformEnvironmentAdapter(
            new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty));
        var gitAdapter = new InjectableGitProxyAdapter(new GitProxySnapshot(true, "global", "", "", ""));
        var npmAdapter = new FakeTargetProxyAdapter("npm",
            new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty))
        {
            ThrowOnApply = true
        };
        var service = CreateServiceWithAllAdapters(
            configStore: configStore, ledger: ledger, environment: environment,
            gitAdapter: gitAdapter, npmAdapter: npmAdapter);

        var plan = service.PlanCompositeEnable("http://127.0.0.1:7890", "http://127.0.0.1:7890", "127.0.0.1");
        var error = Assert.Throws<InvalidOperationException>(() => service.Apply(plan.Payload.PlanId));

        Assert.Contains("npm", error.Message);
        Assert.False(environment.Current.Enabled);
        Assert.Equal(string.Empty, gitAdapter.Current.HttpProxy);
        Assert.Equal(0, ledger.Count);
    }

    [Fact]
    public void RollbackComposite_rolls_back_all_targets()
    {
        var configStore = new FakeConfigStore
        {
            TargetFlags = new ProxyTargetFlags(Env: true, Git: true, Npm: true, Pip: false)
        };
        var ledger = new FakeOperationLedger();
        var environment = new FakePlatformEnvironmentAdapter(
            new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty));
        var gitAdapter = new InjectableGitProxyAdapter(new GitProxySnapshot(true, "global", "", "", ""));
        var npmAdapter = new FakeTargetProxyAdapter("npm",
            new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty));
        var service = CreateServiceWithAllAdapters(
            configStore: configStore, ledger: ledger, environment: environment,
            gitAdapter: gitAdapter, npmAdapter: npmAdapter);

        var plan = service.PlanCompositeEnable("http://127.0.0.1:7890", "http://127.0.0.1:7890", "127.0.0.1");
        var apply = service.Apply(plan.Payload.PlanId);
        var rollback = service.Rollback(apply.Payload.ChangeId);
        var payload = rollback.Payload.GetAfter<CompositeProxyChange>();

        Assert.Equal("composite", rollback.Payload.Target);
        Assert.False(environment.Current.Enabled);
        Assert.Equal(string.Empty, gitAdapter.Current.HttpProxy);
        Assert.False(npmAdapter.Current.Enabled);
        Assert.Equal(2, ledger.Count);
    }

    [Fact]
    public void PlanCompositeDisable_creates_disable_plan_for_all_targets()
    {
        var configStore = new FakeConfigStore
        {
            TargetFlags = new ProxyTargetFlags(Env: true, Git: true, Npm: true, Pip: true)
        };
        var gitAdapter = new InjectableGitProxyAdapter(new GitProxySnapshot(true, "global", "", "", ""));
        var npmAdapter = new FakeTargetProxyAdapter("npm",
            new ProxyConfigSnapshot(true, "http://npm:8080", "http://npm:8443", "npm.local"));
        var pipAdapter = new FakeTargetProxyAdapter("pip",
            new ProxyConfigSnapshot(true, "http://pip:8080", "http://pip:8443", "pip.local"));
        var service = CreateServiceWithAllAdapters(
            configStore: configStore, gitAdapter: gitAdapter, npmAdapter: npmAdapter, pipAdapter: pipAdapter);

        var result = service.PlanCompositeDisable();
        var payload = result.Payload.GetPayload<CompositeProxyPlan>();

        Assert.Equal("disable", payload.Mode);
        Assert.Contains("env", payload.Targets);
        Assert.Contains("git", payload.Targets);
        Assert.Contains("npm", payload.Targets);
        Assert.Contains("pip", payload.Targets);
    }
}

internal sealed class FakePlanStore : IPlanStore
{
    private readonly Dictionary<string, PlanRecord> _plans = new();

    public PlanRecord? GetPlanRecord(string planId)
    {
        return _plans.TryGetValue(planId, out var plan) ? plan : null;
    }

    public void SavePlanRecord(PlanRecord plan)
    {
        _plans[plan.PlanId] = plan;
    }
}

internal sealed class FakeOperationLedger : IOperationLedger
{
    private readonly Dictionary<string, ChangeRecord> _changes = new();

    public int Count => _changes.Count;

    public ChangeRecord? GetChangeRecord(string changeId)
    {
        return _changes.TryGetValue(changeId, out var change) ? change : null;
    }

    public void AppendChangeRecord(ChangeRecord change)
    {
        _changes[change.ChangeId] = change;
    }
}

internal sealed class FakePlatformEnvironmentAdapter : IPlatformEnvironmentAdapter
{
    private readonly Queue<Func<ProxyConfigSnapshot, ProxyConfigSnapshot>> _appliedTransforms = new();

    public FakePlatformEnvironmentAdapter()
        : this(new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty))
    {
    }

    public FakePlatformEnvironmentAdapter(ProxyConfigSnapshot initial)
    {
        Current = initial;
    }

    public ProxyConfigSnapshot Current { get; private set; }

    public ProxyConfigSnapshot ReadEnvironmentProxy()
    {
        return Current;
    }

    public void EnqueueAppliedTransform(Func<ProxyConfigSnapshot, ProxyConfigSnapshot> transform)
    {
        _appliedTransforms.Enqueue(transform);
    }

    public void ApplyEnvironmentProxy(ProxyConfigSnapshot snapshot)
    {
        Current = _appliedTransforms.Count > 0 ? _appliedTransforms.Dequeue()(snapshot) : snapshot;
    }
}

internal sealed class FakeClock : IClock
{
    public string Value { get; set; } = "2026-04-11T00:00:00Z";

    public string NowText()
    {
        return Value;
    }
}

internal sealed class InjectableGitProxyAdapter : IGitProxyAdapter
{
    public InjectableGitProxyAdapter(GitProxySnapshot current)
    {
        Current = current;
    }

    public GitProxySnapshot Current { get; private set; }

    public bool ThrowOnApply { get; set; }

    public bool ThrowOnVerify { get; set; }

    public bool CorruptRollback { get; set; }

    public bool IsAvailable()
    {
        return Current.Available;
    }

    public GitProxySnapshot ReadCurrent()
    {
        return Current;
    }

    public GitProxyPlan PlanEnable(string httpProxy, string httpsProxy, string noProxy)
    {
        var before = ReadCurrent();
        var desired = new GitProxySnapshot(true, "global", httpProxy, httpsProxy, noProxy);
        return BuildPlan("enable", before, desired);
    }

    public GitProxyPlan PlanDisable()
    {
        var before = ReadCurrent();
        var desired = new GitProxySnapshot(before.Available, "global", string.Empty, string.Empty, string.Empty);
        return BuildPlan("disable", before, desired);
    }

    public GitProxySnapshot Apply(GitProxyPlan plan)
    {
        if (ThrowOnApply)
        {
            throw new InvalidOperationException("git apply failed");
        }

        Current = plan.Desired;
        return Current;
    }

    public GitProxySnapshot Verify(GitProxySnapshot desired)
    {
        if (ThrowOnVerify)
        {
            throw new InvalidOperationException("git verify failed");
        }

        if (Current.HttpProxy != desired.HttpProxy ||
            Current.HttpsProxy != desired.HttpsProxy ||
            Current.NoProxy != desired.NoProxy)
        {
            throw new InvalidOperationException("git proxy verification failed");
        }

        return Current;
    }

    public GitProxySnapshot Rollback(GitProxySnapshot before)
    {
        Current = CorruptRollback
            ? new GitProxySnapshot(before.Available, before.Scope, before.HttpProxy + "-rollback-broken", before.HttpsProxy, before.NoProxy)
            : before;
        return before;
    }

    private static GitProxyPlan BuildPlan(string mode, GitProxySnapshot before, GitProxySnapshot desired)
    {
        var actions = new List<GitProxyPlanAction>();
        AddAction(actions, "http.proxy", before.HttpProxy, desired.HttpProxy);
        AddAction(actions, "https.proxy", before.HttpsProxy, desired.HttpsProxy);
        AddAction(actions, "http.noProxy", before.NoProxy, desired.NoProxy);
        return new GitProxyPlan("git", mode, before, desired, actions);
    }

    private static void AddAction(List<GitProxyPlanAction> actions, string key, string before, string after)
    {
        if (before == after)
        {
            return;
        }

        var action = string.IsNullOrWhiteSpace(after) ? "unset" : "set";
        actions.Add(new GitProxyPlanAction(key, action, before, after));
    }
}

internal sealed class FakeTargetProxyAdapter : IProxyTargetAdapter
{
    private ProxyConfigSnapshot _current;

    public FakeTargetProxyAdapter(string targetName, ProxyConfigSnapshot current)
    {
        TargetName = targetName;
        _current = current;
    }

    public string TargetName { get; }
    public ProxyConfigSnapshot Current => _current;
    public bool ThrowOnApply { get; set; }
    public bool Available { get; set; } = true;

    public bool IsAvailable() => Available;

    public ProxyConfigSnapshot ReadCurrent() => _current;

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
        if (ThrowOnApply)
        {
            throw new InvalidOperationException($"{TargetName} apply failed");
        }

        _current = desired;
        return _current;
    }

    public ProxyConfigSnapshot Verify(ProxyConfigSnapshot desired)
    {
        if (_current != desired)
        {
            throw new InvalidOperationException($"{TargetName} proxy verification failed");
        }

        return _current;
    }

    public ProxyConfigSnapshot Rollback(ProxyConfigSnapshot before)
    {
        _current = before;
        return before;
    }
}
