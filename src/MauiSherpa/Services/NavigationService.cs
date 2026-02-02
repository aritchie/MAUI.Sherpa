using MauiSherpa.Core.Interfaces;
using Microsoft.AspNetCore.Components;

namespace MauiSherpa.Services;

public class NavigationService : INavigationService
{
    private readonly NavigationManager _navigationManager;

    public NavigationService(NavigationManager navigationManager)
    {
        _navigationManager = navigationManager;
    }

    public Task NavigateToAsync(string route)
    {
        try
        {
            _navigationManager.NavigateTo(route);
        }
        catch (InvalidOperationException)
        {
            // NavigationManager may not be initialized yet (e.g., during early startup)
        }
        return Task.CompletedTask;
    }

    public Task NavigateBackAsync()
    {
        // Blazor doesn't have built-in back navigation, use JS interop if needed
        return Task.CompletedTask;
    }

    public Task<string?> GetCurrentRouteAsync()
    {
        return Task.FromResult<string?>(_navigationManager.Uri);
    }
}
