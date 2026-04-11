namespace TermForge.Cli;

internal sealed record AppPaths(
    string RepoRoot,
    string ConfigPath,
    string ModuleStatePath,
    string PlanStorePath,
    string OperationLedgerPath)
{
    public static AppPaths Create(string startPath)
    {
        var repoRoot = FindRepoRoot(startPath);
        var stateRoot = Path.Combine(repoRoot, "state");

        return new AppPaths(
            RepoRoot: repoRoot,
            ConfigPath: Path.Combine(repoRoot, "scc.config.json"),
            ModuleStatePath: Path.Combine(repoRoot, "module_state.json"),
            PlanStorePath: Path.Combine(stateRoot, "proxy-plans.json"),
            OperationLedgerPath: Path.Combine(stateRoot, "proxy-ledger.json"));
    }

    private static string FindRepoRoot(string startPath)
    {
        var current = new DirectoryInfo(Path.GetFullPath(startPath));
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "TermForge.sln")) ||
                File.Exists(Path.Combine(current.FullName, "setup.ps1")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Path.GetFullPath(startPath);
    }
}
