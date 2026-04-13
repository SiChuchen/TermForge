using TermForge.Contracts;

namespace TermForge.Core.Interfaces;

public interface IOperationLedger
{
    ChangeRecord? GetChangeRecord(string changeId)
        => throw new NotImplementedException();

    void AppendChangeRecord(ChangeRecord change)
        => throw new NotImplementedException();

    ProxyApplyPayload? GetChange(string changeId)
    {
        return GetChangeRecord(changeId)?.ToProxyApplyPayload();
    }

    void AppendChange(ProxyApplyPayload change)
    {
        AppendChangeRecord(change);
    }
}
