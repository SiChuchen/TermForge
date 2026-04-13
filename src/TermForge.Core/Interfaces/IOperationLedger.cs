using TermForge.Contracts;

namespace TermForge.Core.Interfaces;

public interface IOperationLedger
{
    ProxyApplyPayload? GetChange(string changeId);
    void AppendChange(ProxyApplyPayload change);

    ChangeRecord? GetChangeRecord(string changeId)
    {
        var change = GetChange(changeId);
        return change is null ? null : (ChangeRecord)change;
    }

    void AppendChangeRecord(ChangeRecord change)
    {
        AppendChange(change.ToProxyApplyPayload());
    }
}
