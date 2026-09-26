namespace PoultryFarm.Blazor.Services;

public sealed class NavBadgeState
{
    public int PendingFarmerRegistrations { get; private set; }

    public event Action? Changed;

    public void SetPendingFarmerRegistrations(int count)
    {
        var normalized = Math.Max(0, count);
        if (PendingFarmerRegistrations == normalized)
        {
            return;
        }

        PendingFarmerRegistrations = normalized;
        Changed?.Invoke();
    }

    public void IncrementPendingFarmerRegistrations()
    {
        PendingFarmerRegistrations++;
        Changed?.Invoke();
    }
}
