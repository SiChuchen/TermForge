using TermForge.Contracts;
using TermForge.Core.Interfaces;
using TermForge.Core.Services;
using TermForge.Platform.Windows;

namespace TermForge.Cli;

internal sealed class CommandDispatcher
{
    private readonly IClock _clock;
    private readonly JsonConfigStore _configStore;
    private readonly WindowsEnvironmentAdapter _environmentAdapter;
    private readonly JsonOperationLedger _operationLedger;
    private readonly JsonPlanStore _planStore;

    public CommandDispatcher(AppPaths appPaths)
    {
        _clock = new CliClock();
        _configStore = new JsonConfigStore(appPaths.RepoRoot, appPaths.ConfigPath, appPaths.ModuleStatePath);
        _planStore = new JsonPlanStore(appPaths.PlanStorePath);
        _operationLedger = new JsonOperationLedger(appPaths.OperationLedgerPath);
        _environmentAdapter = new WindowsEnvironmentAdapter();
    }

    public object Dispatch(IReadOnlyList<string> args)
    {
        if (args.Count == 2 && Is(args[0], "status") && Is(args[1], "--json"))
        {
            return new StatusService(_configStore).BuildReport();
        }

        if (args.Count >= 2 && Is(args[0], "proxy"))
        {
            return DispatchProxy(args);
        }

        throw new InvalidOperationException("Unsupported command. Use status --json or proxy <scan|plan|apply|rollback> ... --json.");
    }

    private object DispatchProxy(IReadOnlyList<string> args)
    {
        if (args.Count == 3 && Is(args[1], "scan") && Is(args[2], "--json"))
        {
            return CreateWorkflowService().Scan();
        }

        var options = ParseOptions(args, 2);
        RequireJson(options);

        if (Is(args[1], "plan"))
        {
            return DispatchPlan(options);
        }

        if (Is(args[1], "apply"))
        {
            return CreateWorkflowService().Apply(GetRequiredValue(options, "--plan-id"));
        }

        if (Is(args[1], "rollback"))
        {
            return CreateWorkflowService().Rollback(GetRequiredValue(options, "--change-id"));
        }

        throw new InvalidOperationException("Unsupported proxy command. Use scan, plan, apply, or rollback.");
    }

    private CommandEnvelope<ProxyPlanPayload> DispatchPlan(Dictionary<string, string?> options)
    {
        var mode = GetRequiredValue(options, "--mode");
        var targets = GetRequiredValue(options, "--targets");
        if (!string.Equals(targets, "env", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Phase1 only supports --targets env.");
        }

        if (string.Equals(mode, "enable", StringComparison.OrdinalIgnoreCase))
        {
            var http = GetRequiredValue(options, "--http");
            var https = options.GetValueOrDefault("--https") ?? string.Empty;
            var noProxy = options.GetValueOrDefault("--no-proxy") ?? string.Empty;
            return CreateWorkflowService().PlanEnable(http, https, noProxy);
        }

        if (string.Equals(mode, "disable", StringComparison.OrdinalIgnoreCase))
        {
            return CreateDisablePlan();
        }

        throw new InvalidOperationException("Unsupported proxy plan mode. Use enable or disable.");
    }

    private CommandEnvelope<ProxyPlanPayload> CreateDisablePlan()
    {
        var current = _environmentAdapter.ReadEnvironmentProxy();
        var desired = new ProxyConfigSnapshot(false, string.Empty, string.Empty, string.Empty);
        var payload = new ProxyPlanPayload(CreateId("plan"), "env", "disable", current, desired);
        _planStore.SavePlan(payload);
        return Envelope("proxy.plan", payload);
    }

    private ProxyWorkflowService CreateWorkflowService()
    {
        return new ProxyWorkflowService(_configStore, _planStore, _operationLedger, _environmentAdapter, _clock);
    }

    private CommandEnvelope<TPayload> Envelope<TPayload>(string command, TPayload payload)
    {
        return new CommandEnvelope<TPayload>(
            Command: command,
            Status: "PASS",
            GeneratedAt: _clock.NowText(),
            Warnings: [],
            Errors: [],
            Payload: payload);
    }

    private string CreateId(string prefix)
    {
        return $"{prefix}-{_clock.NowText()}-{Guid.NewGuid():N}";
    }

    private static Dictionary<string, string?> ParseOptions(IReadOnlyList<string> args, int startIndex)
    {
        var options = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        for (var index = startIndex; index < args.Count; index++)
        {
            var token = args[index];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unexpected argument: {token}");
            }

            if (index + 1 < args.Count && !args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                options[token] = args[index + 1];
                index++;
                continue;
            }

            options[token] = null;
        }

        return options;
    }

    private static string GetRequiredValue(Dictionary<string, string?> options, string name)
    {
        if (!options.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Missing required option: {name}");
        }

        return value;
    }

    private static void RequireJson(Dictionary<string, string?> options)
    {
        if (!options.ContainsKey("--json"))
        {
            throw new InvalidOperationException("This phase1 CLI requires --json output.");
        }
    }

    private static bool Is(string actual, string expected)
    {
        return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class CliClock : IClock
    {
        public string NowText()
        {
            return DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        }
    }
}
