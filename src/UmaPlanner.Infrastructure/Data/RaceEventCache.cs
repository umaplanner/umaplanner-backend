using UmaPlanner.Core.Entities;

namespace UmaPlanner.Infrastructure.Data;

public class RaceEventCache
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private List<UmaRaceEvent> _events = new();

    public async Task UpdateAsync(List<UmaRaceEvent> events)
    {
        await _lock.WaitAsync();
        try
        {
            _events = events ?? new List<UmaRaceEvent>();
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<List<UmaRaceEvent>> GetAllAsync()
    {
        await _lock.WaitAsync();
        try
        {
            return new List<UmaRaceEvent>(_events);
        }
        finally
        {
            _lock.Release();
        }
    }
}
