using System;

namespace TermForge.Core.Interfaces;

public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
