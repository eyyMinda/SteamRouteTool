using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamRouteTool.Models;
using SteamRouteTool.Services;

namespace SteamRouteTool.ViewModels;

/// <summary>A route (point of presence) row. Ping and blocked state are read from <see cref="RoutesViewModel"/>.</summary>
public sealed partial class RouteViewModel : ObservableObject
{
    readonly RoutesViewModel owner;

    public RouteViewModel(RoutesViewModel owner, Pop pop, bool expanded)
    {
        this.owner = owner;
        Pop = pop;
        // Set the field directly: the initial state shouldn't trigger the "expanded" ping.
        isExpanded = expanded;
        Relays = pop.Relays.Select((relay, index) => new RelayViewModel(owner, this, relay, index + 1)).ToList();
        RegionOrder = Math.Max(0, RegionClassifier.All.ToList().IndexOf(pop.Region));
    }

    public Pop Pop { get; }
    public IReadOnlyList<RelayViewModel> Relays { get; }
    public string Name => Pop.Description;
    public string Code => Pop.Code;
    public string Region => Pop.Region;
    public int RegionOrder { get; }
    public int RelayCount => Pop.Relays.Count;
    public bool HasMultipleRelays => Pop.Relays.Count > 1;

    /// <summary>Whether each relay is shown as its own row, so relays can be blocked individually.</summary>
    [ObservableProperty]
    bool isExpanded;

    partial void OnIsExpandedChanged(bool value)
    {
        if (value) { owner.OnRouteExpanded(this); }
    }

    /// <summary>The best ping of the route's unblocked relays.</summary>
    public PingText Ping => owner.DescribePing(Pop.Relays);
    public int PingSortValue => Ping.SortValue;

    /// <summary>True if every relay is blocked, null if only some are. Setting it blocks or unblocks the whole route.</summary>
    public bool? IsBlocked
    {
        get => owner.GetBlockedState(Pop);
        set => owner.RequestSetRouteBlocked(this, value == true);
    }

    public int BlockedSortValue => IsBlocked switch { true => 0, null => 1, false => 2 };

    /// <summary>E.g. " · 2 of 4 relays blocked" for a partly blocked route, empty otherwise.</summary>
    public string BlockedDetail => IsBlocked == null ? $" · {Pop.Relays.Count(owner.IsRelayBlocked)} of {RelayCount} relays blocked" : "";

    [RelayCommand]
    void PingAgain()
    {
        owner.RequestPingRoute(this);
    }

    public void Refresh()
    {
        OnPropertyChanged(nameof(Ping));
        OnPropertyChanged(nameof(PingSortValue));
        OnPropertyChanged(nameof(IsBlocked));
        OnPropertyChanged(nameof(BlockedSortValue));
        OnPropertyChanged(nameof(BlockedDetail));
        foreach (RelayViewModel relay in Relays) { relay.Refresh(); }
    }
}
