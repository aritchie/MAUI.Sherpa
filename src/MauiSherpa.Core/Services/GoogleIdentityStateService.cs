using MauiSherpa.Core.Interfaces;

namespace MauiSherpa.Core.Services;

public class GoogleIdentityStateService : IGoogleIdentityStateService
{
    private GoogleIdentity? _selectedIdentity;

    public GoogleIdentity? SelectedIdentity => _selectedIdentity;

    public event Action? OnSelectionChanged;

    public void SetSelectedIdentity(GoogleIdentity? identity)
    {
        if (_selectedIdentity != identity)
        {
            _selectedIdentity = identity;
            OnSelectionChanged?.Invoke();
        }
    }
}
