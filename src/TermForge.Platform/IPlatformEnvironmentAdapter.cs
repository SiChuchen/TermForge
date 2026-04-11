using TermForge.Contracts;

namespace TermForge.Platform;

public interface IPlatformEnvironmentAdapter
{
    ProxyConfigSnapshot ReadEnvironmentProxy();
    void ApplyEnvironmentProxy(ProxyConfigSnapshot snapshot);
}
