using Xunit;

namespace TermForge.Core.Tests;

public class StatusServiceTests
{
    [Fact]
    public void Solution_bootstrap_test_project_compiles()
    {
        Assert.True(true);
    }

    [Fact]
    public void CommandEnvelope_defaults_to_expected_schema_version()
    {
        var envelope = new TermForge.Contracts.CommandEnvelope<string>(
            Command: "status",
            Status: "PASS",
            GeneratedAt: "2026-04-11 00:00:00",
            Warnings: [],
            Errors: [],
            Payload: "ok");

        Assert.Equal("2026-04-11", envelope.SchemaVersion);
    }
}
