using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamRouteTool.Localization;
using SteamRouteTool.Models;
using SteamRouteTool.Services;

namespace SteamRouteTool.ViewModels;

/// <summary>A relay of an expanded route.</summary>
public sealed partial class RelayViewModel : ObservableObject
{
    readonly RoutesViewModel owner;

    public RelayViewModel(RoutesViewModel owner, RouteViewModel route, Relay relay, int number)
    {
        this.owner = owner;
        Route = route;
        Relay = relay;
        Number = number;
    }

    public RouteViewModel Route { get; }
    public Relay Relay { get; }
    public int Number { get; }
    public string Name => Loc.T("Routes.RelayName", Number);
    public string Endpoint => Loc.T("Routes.RelayEndpoint", Relay.Address, Relay.MinPort, Relay.MaxPort);

    public PingText Ping => owner.DescribePing([Relay]);

    public bool IsBlocked
    {
        get => owner.IsRelayBlocked(Relay);
        set => owner.RequestSetRelayBlocked(Route, Relay, value);
    }

    [RelayCommand]
    void PingAgain()
    {
        owner.RequestPingRelay(Route, Relay);
    }

    public void RefreshTexts()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(Endpoint));
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(Ping));
        OnPropertyChanged(nameof(IsBlocked));
    }
}
