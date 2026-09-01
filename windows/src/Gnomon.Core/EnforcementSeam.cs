namespace Gnomon.Core;

/// <summary>Version 0.1 seam only. No implementation may enforce policy.</summary>
public interface IEnforcementAdapter
{
    void OnCategoryExhausted(string category);
    void OnLockdown(bool state);
}

public sealed class NoOpEnforcementAdapter : IEnforcementAdapter
{
    public void OnCategoryExhausted(string category) { }
    public void OnLockdown(bool state) { }
}
