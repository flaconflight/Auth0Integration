using System.Collections.Concurrent;
using Auth0Integration.Functions.Models;

namespace Auth0Integration.Functions.Services;

public class InMemoryCreditContextStore : ICreditContextStore
{
    private static readonly TimeSpan EntryTtl = TimeSpan.FromMinutes(30);
    private readonly ConcurrentDictionary<string, CreditContextEntry> _store = new();

    public void Store(string otc, CreditContextEntry entry)
    {
        CleanExpiredEntries();
        _store[otc] = entry;
    }

    public CreditContextEntry? Retrieve(string otc)
    {
        CleanExpiredEntries();

        if (_store.TryGetValue(otc, out var entry))
        {
            if (DateTime.UtcNow - entry.CreatedAt <= EntryTtl)
            {
                return entry;
            }

            _store.TryRemove(otc, out _);
        }

        return null;
    }

    public void Remove(string otc)
    {
        _store.TryRemove(otc, out _);
    }

    private void CleanExpiredEntries()
    {
        var cutoff = DateTime.UtcNow - EntryTtl;
        foreach (var kvp in _store)
        {
            if (kvp.Value.CreatedAt < cutoff)
            {
                _store.TryRemove(kvp.Key, out _);
            }
        }
    }
}
