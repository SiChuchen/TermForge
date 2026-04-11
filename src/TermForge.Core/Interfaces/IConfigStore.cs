using System.Threading;
using System.Threading.Tasks;

namespace TermForge.Core.Interfaces;

public interface IConfigStore
{
    Task<string?> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(string content, CancellationToken cancellationToken = default);
}
