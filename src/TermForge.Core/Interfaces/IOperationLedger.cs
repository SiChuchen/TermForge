using TermForge.Contracts;

namespace TermForge.Core.Interfaces;

public interface IOperationLedger
{
    ProxyApplyPayload? GetChange(string changeId);
    void AppendChange(ProxyApplyPayload change);
}
