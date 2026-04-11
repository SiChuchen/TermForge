using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TermForge.Core.Interfaces;

public interface IOperationLedger
{
    Task AppendAsync(string entry, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ReadAsync(CancellationToken cancellationToken = default);
}
