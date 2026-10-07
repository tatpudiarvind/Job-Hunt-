namespace ArvindJobHunter.Infrastructure.Persistence;

/// <summary>Implemented by in-memory-cached stores so an on-disk data import can force a reload.</summary>
public interface ICacheInvalidatable
{
    void Invalidate();
}
