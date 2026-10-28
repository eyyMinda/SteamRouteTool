using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
        Name = "Relay " + number;
    }

    public RouteViewModel Route { get; }
    public Relay Relay { get; }
    public string Name { get; }
    public string Endpoint => $"{Relay.Address} · ports {Relay.MinPort}-{Relay.MaxPort}";

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

    public void Refresh()
    {
        OnPropertyChanged(nameof(Ping));
        OnPropertyChanged(nameof(IsBlocked));
    }
}
