using System.Diagnostics;
using System.Text.Json;
using TermForge.Contracts;
using TermForge.Core.Interfaces;
using TermForge.Core.Services;
using TermForge.Platform;
using TermForge.Platform.Windows;

namespace TermForge.Cli;

internal sealed class CommandDispatcher
{
    private readonly IClock _clock;
    private readonly JsonConfigStore _configStore;
    private readonly WindowsEnvironmentAdapter _environmentAdapter;
    private readonly WindowsGitProxyAdapter _gitProxyAdapter;
    private readonly IProxyTargetAdapter _npmAdapter;
    private readonly IProxyTargetAdapter _pipAdapter;
    private readonly JsonOperationLedger _operationLedger;
    private readonly JsonPlanStore _planStore;

    public CommandDispatcher(AppPaths appPaths)
    {
        _clock = new CliClock();
        _configStore = new JsonConfigStore(appPaths.RepoRoot, appPaths.ConfigPath, appPaths.ModuleStatePath);
        _planStore = new JsonPlanStore(appPaths.PlanStorePath);
        _operationLedger = new JsonOperationLedger(appPaths.OperationLedgerPath);
        _environmentAdapter = new WindowsEnvironmentAdapter();
        _gitProxyAdapter = new WindowsGitProxyAdapter();
        _npmAdapter = new WindowsNpmProxyAdapter();
        _pipAdapter = new WindowsPipProxyAdapter();
    }

    public object Dispatch(IReadOnlyList<string> args)
    {
        if (args.Count == 2 && Is(args[0], "status") && Is(args[1], "--json"))
        {
            return new StatusService(_configStore, LoadSharedPrimaryCommandName(), _npmAdapter, _pipAdapter).BuildReport();
        }


        if (args.Count == 2 && Is(args[0], "doctor") && Is(args[1], "--json"))
        {
            return new DoctorService(_configStore, LoadSharedPrimaryCommandName(), _npmAdapter, _pipAdapter).BuildReport();
        }
        if (args.Count >= 2 && Is(args[0], "proxy"))
        {
            return DispatchProxy(args);
        }

        throw new InvalidOperationException("Unsupported command. Use status --json, doctor --json, or proxy <scan|plan|apply|rollback> ... --json.");
    }

    private object DispatchProxy(IReadOnlyList<string> args)
    {
        if (args.Count == 3 && Is(args[1], "scan") && Is(args[2], "--json"))
        {
            return CreateWorkflowService().Scan();
        }

        if (args.Count >= 3 && Is(args[1], "scan"))
        {
            var scanOptions = ParseOptions(args, 2);
            RequireJson(scanOptions);
            var scanTargets = GetRequiredValue(scanOptions, "--targets");
            var workflow = CreateWorkflowService();
            if (string.Equals(scanTargets, "npm", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(scanTargets, "pip", StringComparison.OrdinalIgnoreCase))
            {
                return workflow.ScanTarget(scanTargets.ToLowerInvariant());
            }
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

    private object DispatchPlan(Dictionary<string, string?> options)
    {
        var mode = GetRequiredValue(options, "--mode");
        var targets = GetRequiredValue(options, "--targets");
        var workflow = CreateWorkflowService();

        if (string.Equals(mode, "enable", StringComparison.OrdinalIgnoreCase))
        {
            var http = GetRequiredValue(options, "--http");
            RequireValues(options, "--https", "--no-proxy");
            var https = GetRequiredValue(options, "--https");
            var noProxy = GetRequiredValue(options, "--no-proxy");
            if (string.Equals(targets, "env", StringComparison.OrdinalIgnoreCase))
            {
                return workflow.PlanEnable(http, https, noProxy);
            }

            if (string.Equals(targets, "git", StringComparison.OrdinalIgnoreCase))
            {
                return workflow.PlanGitEnable(http, https, noProxy);
            }

            if (string.Equals(targets, "npm", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(targets, "pip", StringComparison.OrdinalIgnoreCase))
            {
                return workflow.PlanTargetEnable(targets.ToLowerInvariant(), http, https, noProxy);
            }

            if (string.Equals(targets, "composite", StringComparison.OrdinalIgnoreCase))
            {
                return workflow.PlanCompositeEnable(http, https, noProxy);
            }

            throw new InvalidOperationException("Phase1 only supports standalone --targets env or --targets git.");
        }

        if (string.Equals(mode, "disable", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(targets, "env", StringComparison.OrdinalIgnoreCase))
            {
                return CreateDisablePlan();
            }

            if (string.Equals(targets, "git", StringComparison.OrdinalIgnoreCase))
            {
                return workflow.PlanGitDisable();
            }

            if (string.Equals(targets, "npm", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(targets, "pip", StringComparison.OrdinalIgnoreCase))
            {
                return workflow.PlanTargetDisable(targets.ToLowerInvariant());
            }

            if (string.Equals(targets, "composite", StringComparison.OrdinalIgnoreCase))
            {
                return workflow.PlanCompositeDisable();
            }

            throw new InvalidOperationException("Phase1 only supports standalone --targets env or --targets git.");
        }

        throw new InvalidOperationException("Unsupported proxy plan mode. Use enable or disable.");
    }

    private CommandEnvelope<PlanRecord> CreateDisablePlan()
    {
        return CreateWorkflowService().PlanDisable();
    }

    private ProxyWorkflowService CreateWorkflowService()
    {
        return new ProxyWorkflowService(_configStore, _planStore, _operationLedger, _environmentAdapter, _clock, _gitProxyAdapter, _npmAdapter, _pipAdapter);
    }

    private string? LoadSharedPrimaryCommandName()
    {
        return TryLoadSharedEnvironmentFacts()?.PrimaryCommandName;
    }

    private SharedEnvironmentFactsBridgeResult? TryLoadSharedEnvironmentFacts()
    {
        foreach (var shellName in new[] { "pwsh", "powershell.exe" })
        {
            var facts = TryLoadSharedEnvironmentFacts(shellName);
            if (facts is not null)
            {
                return facts;
            }
        }

        return null;
    }

    private SharedEnvironmentFactsBridgeResult? TryLoadSharedEnvironmentFacts(string shellName)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = shellName,
            Arguments = $"-NoLogo -NoProfile -Command \"{BuildSharedFactsScript()}\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = _configStore.GetRootPath()
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            _ = process.StandardError.ReadToEnd();
            process.WaitForExit();

            if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
            {
                return null;
            }

            return JsonSerializer.Deserialize<SharedEnvironmentFactsBridgeResult>(
                output,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    private string BuildSharedFactsScript()
    {
        var commonModulePath = Path.Combine(_configStore.GetRootPath(), "modules", "common.ps1")
            .Replace("'", "''");

        return string.Join(
            "; ",
            "$ErrorActionPreference = 'Stop'",
            $". '{commonModulePath}'",
            "$config = Get-SccConfig",
            "$facts = Get-SccEnvironmentFacts",
            "[pscustomobject][ordered]@{ PrimaryCommandName = Get-SccPrimaryCommandName -Config $config; Host = $facts.Host; Tools = @($facts.Tools); ProxyEnvironment = $facts.ProxyEnvironment; InstallHost = $facts.InstallHost } | ConvertTo-Json -Depth 6 -Compress");
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

    private static void RequireValues(Dictionary<string, string?> options, params string[] names)
    {
        var missing = names
            .Where(name => !options.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
            .ToArray();

        if (missing.Length > 0)
        {
            throw new InvalidOperationException($"Missing required option(s): {string.Join(", ", missing)}");
        }
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

    private sealed record SharedEnvironmentFactsBridgeResult(
        string PrimaryCommandName,
        EnvironmentHostFacts Host,
        IReadOnlyList<EnvironmentToolFact> Tools,
        EnvironmentProxyFact ProxyEnvironment,
        EnvironmentInstallHostFact InstallHost);
}
