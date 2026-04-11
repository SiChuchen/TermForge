using TermForge.Contracts;

namespace TermForge.Platform;

public interface IPlatformEnvironmentAdapter
{
    ProxyConfigSnapshot ReadEnvironmentProxy();
    ProxyConfigSnapshot ApplyEnvironmentProxy(ProxyConfigSnapshot snapshot);
}
