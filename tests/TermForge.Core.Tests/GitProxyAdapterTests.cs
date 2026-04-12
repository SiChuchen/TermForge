using TermForge.Contracts;
using Xunit;

namespace TermForge.Core.Tests;

public class GitProxyAdapterTests
{
    [Fact]
    public void GitProxySnapshot_defaults_to_global_scope_shape()
    {
        var snapshot = new GitProxySnapshot(
            Available: true,
            Scope: "global",
            HttpProxy: "http://127.0.0.1:7890",
            HttpsProxy: "http://127.0.0.1:7890",
            NoProxy: "127.0.0.1,localhost,::1");

        Assert.True(snapshot.Available);
        Assert.Equal("global", snapshot.Scope);
        Assert.Equal("http://127.0.0.1:7890", snapshot.HttpProxy);
    }
}
