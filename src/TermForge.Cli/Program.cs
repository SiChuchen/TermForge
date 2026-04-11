using System.Text.Json;
using TermForge.Cli;

var appPaths = AppPaths.Create(Directory.GetCurrentDirectory());
var dispatcher = new CommandDispatcher(appPaths);

try
{
    var result = dispatcher.Dispatch(args);
    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(exception.Message);
    return 1;
}
