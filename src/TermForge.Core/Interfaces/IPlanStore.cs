using System.Threading;
using System.Threading.Tasks;

namespace TermForge.Core.Interfaces;

public interface IPlanStore
{
    Task<string?> LoadAsync(string planId, CancellationToken cancellationToken = default);

    Task SaveAsync(string planId, string content, CancellationToken cancellationToken = default);
}
