namespace TermForge.Cli;

internal sealed record AppPaths(
    string RepoRoot,
    string TermForgeRoot,
    string ConfigPath,
    string ModuleStatePath,
    string PlanStorePath,
    string OperationLedgerPath)
{
    public static AppPaths Create(string workingDirectory)
    {
        var repoRoot = FindRepoRoot(workingDirectory);
        var termForgeRoot = Path.Combine(repoRoot, ".termforge");
        var stateRoot = Path.Combine(termForgeRoot, "state");

        return new AppPaths(
            RepoRoot: repoRoot,
            TermForgeRoot: termForgeRoot,
            ConfigPath: Path.Combine(termForgeRoot, "config.json"),
            ModuleStatePath: Path.Combine(termForgeRoot, "module-state.json"),
            PlanStorePath: Path.Combine(stateRoot, "proxy-plans.json"),
            OperationLedgerPath: Path.Combine(stateRoot, "proxy-ledger.json"));
    }

    private static string FindRepoRoot(string workingDirectory)
    {
        var current = new DirectoryInfo(Path.GetFullPath(workingDirectory));
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TermForge.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Path.GetFullPath(workingDirectory);
    }
}
