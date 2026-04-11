using System.Text.Json;
using TermForge.Cli;
using TermForge.Contracts;

var appPaths = AppPaths.Create(AppContext.BaseDirectory);
var dispatcher = new CommandDispatcher(appPaths);
var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
var wantsJson = args.Contains("--json", StringComparer.OrdinalIgnoreCase);

try
{
    var result = dispatcher.Dispatch(args);
    Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));
    return 0;
}
catch (Exception exception)
{
    if (wantsJson)
    {
        var error = new CommandEnvelope<object?>(
            Command: GuessCommand(args),
            Status: "FAIL",
            GeneratedAt: DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
            Warnings: [],
            Errors: [exception.Message],
            Payload: null);
        Console.WriteLine(JsonSerializer.Serialize(error, jsonOptions));
        return 1;
    }

    Console.Error.WriteLine(exception.Message);
    return 1;
}

static string GuessCommand(IReadOnlyList<string> arguments)
{
    if (arguments.Count >= 2 && string.Equals(arguments[0], "proxy", StringComparison.OrdinalIgnoreCase))
    {
        return $"proxy.{arguments[1].ToLowerInvariant()}";
    }

    if (arguments.Count >= 1)
    {
        return arguments[0].ToLowerInvariant();
    }

    return "unknown";
}
