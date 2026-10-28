using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamRouteTool.Models;
using SteamRouteTool.Services;

namespace SteamRouteTool.ViewModels;

/// <summary>
/// The route list of the selected game: loading the network config, blocking routes through the firewall, pinging and
/// filtering. All members run on the UI thread; firewall changes are moved to a background thread while they run.
/// </summary>
public sealed partial class RoutesViewModel : ObservableObject
{
    public const string AllRegions = "All regions";
    public const string StatusAll = "All routes";
    public const string StatusBlocked = "Blocked";
    public const string StatusNotBlocked = "Not blocked";

    readonly SdrConfigClient configClient;
    readonly FirewallService firewall;
    readonly PingService pingService;
    readonly PingCache pingCache;
    readonly SettingsStore settingsStore;
    readonly AppSettings settings;

    IReadOnlyList<Pop> pops = Array.Empty<Pop>();
    HashSet<Relay> blockedRelays = new HashSet<Relay>();
    // This session's ping per relay address (null = no reply), addresses being pinged, and last session's successful pings.
    readonly Dictionary<string, int?> pingResults = new Dictionary<string, int?>();
    readonly HashSet<string> pendingPings = new HashSet<string>();
    readonly Dictionary<string, int> cachedPings;
    // Incremented per load, so a slow load for a game the user already switched away from is ignored.
    int loadVersion;
    bool initialized;

    public RoutesViewModel(SdrConfigClient configClient, FirewallService firewall, PingService pingService, PingCache pingCache,
        SettingsStore settingsStore, AppSettings settings)
    {
        this.configClient = configClient;
        this.firewall = firewall;
        this.pingService = pingService;
        this.pingCache = pingCache;
        this.settingsStore = settingsStore;
        this.settings = settings;
        cachedPings = pingCache.Load();
        selectedGame = Game.FromAppId(settings.LastAppId);
        groupByRegion = settings.GroupByRegion;
        autoMaxPingMs = settings.AutoMaxPingMs;
        autoMinRoutes = settings.AutoMinRoutes;

        ListCollectionView view = (ListCollectionView)CollectionViewSource.GetDefaultView(Routes);
        view.Filter = item => FilterRoute((RouteViewModel)item);
        view.SortDescriptions.Add(new SortDescription(nameof(RouteViewModel.RegionOrder), ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(RouteViewModel.Name), ListSortDirection.Ascending));
        // No live sorting: rows don't jump around while pings arrive (sorting by Ping again re-sorts), and WPF's live
        // shaping misorders grouped rows. Blocked-state changes refresh the view explicitly for the status filter.
        RoutesView = view;
        ApplyGrouping();
    }

    public ObservableCollection<RouteViewModel> Routes { get; } = new ObservableCollection<RouteViewModel>();
    public ICollectionView RoutesView { get; }
    public IReadOnlyList<Game> Games => Game.Supported;
    public ObservableCollection<string> RegionOptions { get; } = new ObservableCollection<string> { AllRegions };
    public IReadOnlyList<string> StatusOptions { get; } = [StatusAll, StatusBlocked, StatusNotBlocked];

    [ObservableProperty]
    Game selectedGame;

    [ObservableProperty]
    string searchText = "";

    [ObservableProperty]
    string selectedRegion = AllRegions;

    [ObservableProperty]
    string selectedStatus = StatusAll;

    [ObservableProperty]
    bool groupByRegion;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyCanExecuteChangedFor(nameof(PingAllCommand), nameof(RefreshCommand), nameof(BlockAllCommand), nameof(UnblockAllCommand), nameof(BlockAllExceptShownCommand), nameof(ApplyAutoSelectCommand))]
    bool isLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(CanChangeRules))]
    [NotifyCanExecuteChangedFor(nameof(BlockAllCommand), nameof(UnblockAllCommand), nameof(BlockAllExceptShownCommand), nameof(ApplyAutoSelectCommand))]
    bool isFirewallBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    [NotifyPropertyChangedFor(nameof(AutoSelectPreview))]
    [NotifyCanExecuteChangedFor(nameof(PingAllCommand), nameof(ApplyAutoSelectCommand))]
    bool isPinging;

    /// <summary>Shown in an error banner until dismissed.</summary>
    [ObservableProperty]
    string? errorMessage;

    /// <summary>Shown in an informational banner until dismissed.</summary>
    [ObservableProperty]
    string? statusMessage;

    public bool IsBusy => IsLoading || IsFirewallBusy || IsPinging;
    public bool CanChangeRules => !IsFirewallBusy && !IsLoading;

    /// <summary>True when routes are loaded but the filters hide all of them.</summary>
    public bool HasNoShownRoutes => Routes.Count > 0 && !RoutesView.Cast<object>().Any();

    void NotifySummaryChanged()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(HasNoShownRoutes));
        OnPropertyChanged(nameof(AutoSelectPreview));
    }

    public string Summary
    {
        get
        {
            int blocked = Routes.Count(route => route.IsBlocked != false);
            int shown = RoutesView.Cast<object>().Count();
            return $"{blocked} of {Routes.Count} routes blocked · {shown} shown";
        }
    }

    // ----- Loading -----

    public async Task InitializeAsync()
    {
        if (initialized) { return; }
        initialized = true;
        // With "unblock on exit", the rules were removed on purpose when the app closed, so the saved profile (not the
        // empty firewall) says what should be blocked. Otherwise the firewall is the truth.
        await LoadGameAsync(SelectedGame, fromFirewall: !settings.UnblockOnExit);
    }

    /// <summary>Removes every rule this tool owns, for "unblock on exit". Blocks the caller; failures are ignored.</summary>
    public void RemoveRulesOnExit()
    {
        try
        {
            firewall.RemoveAllRules();
        }
        catch (Exception ex) when (FirewallService.IsFirewallError(ex)) { }
    }

    partial void OnSelectedGameChanged(Game value)
    {
        settings.LastAppId = value.AppId;
        settingsStore.Save(settings);
        if (initialized) { _ = LoadGameAsync(value, fromFirewall: false); }
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    Task RefreshAsync()
    {
        return LoadGameAsync(SelectedGame, fromFirewall: true);
    }

    bool CanRefresh() => !IsLoading;

    /// <summary>
    /// Loads the game's routes and decides what's blocked: on launch (or refresh) the existing firewall rules, when switching
    /// games the game's saved profile. The rules are then synced so they match exactly, which also removes outdated rules.
    /// </summary>
    async Task LoadGameAsync(Game game, bool fromFirewall)
    {
        int load = ++loadVersion;
        IsLoading = true;
        ErrorMessage = null;
        StatusMessage = null;

        IReadOnlyList<Pop> loaded;
        try
        {
            loaded = await configClient.GetPopsAsync(game.AppId);
        }
        catch (Exception ex)
        {
            if (load != loadVersion) { return; }
            ErrorMessage = $"Couldn't load the route list for {game.Name}: {ex.Message}";
            IsLoading = false;
            return;
        }
        if (load != loadVersion) { return; }

        HashSet<Relay>? blocked = null;
        string? appliedMessage = null;
        if (!fromFirewall && settings.Profiles.TryGetValue(game.AppId, out GameProfile? profile))
        {
            HashSet<string> addresses = profile.BlockedRelays.ToHashSet();
            blocked = loaded.SelectMany(pop => pop.Relays).Where(relay => addresses.Contains(relay.Address)).ToHashSet();
            int blockedRoutes = CountBlockedRoutes(loaded, blocked);
            appliedMessage = $"Applied your saved {game.Name} profile to the firewall ({blockedRoutes} {(blockedRoutes == 1 ? "route" : "routes")} blocked).";
        }
        else
        {
            try
            {
                Dictionary<string, HashSet<string>> addresses = await Task.Run(firewall.GetBlockedAddresses);
                blocked = FirewallService.MatchBlockedRelays(loaded, addresses);
            }
            catch (Exception ex) when (FirewallService.IsFirewallError(ex))
            {
                ShowFirewallError(ex);
            }
        }
        if (load != loadVersion) { return; }

        pops = loaded;
        blockedRelays = blocked ?? new HashSet<Relay>();
        BuildRoutes();
        IsLoading = false;

        if (settings.PingOnStartup) { _ = PingAllAsync(); }

        // Only sync if the current state is known; otherwise the sync could remove rules the list doesn't show.
        if (blocked != null)
        {
            HashSet<Relay> snapshot = blocked.ToHashSet();
            if (await RunFirewallChangeAsync(() => firewall.SyncRules(loaded, snapshot)) && load == loadVersion)
            {
                SaveProfile();
                if (appliedMessage != null) { StatusMessage = appliedMessage; }
            }
        }
    }

    void BuildRoutes()
    {
        Routes.Clear();
        foreach (Pop pop in pops)
        {
            // Partly blocked routes start expanded, so it's visible which relays are blocked.
            Routes.Add(new RouteViewModel(this, pop, expanded: pop.Relays.Count > 1 && GetBlockedState(pop) == null));
        }

        string selected = SelectedRegion;
        RegionOptions.Clear();
        RegionOptions.Add(AllRegions);
        foreach (string region in RegionClassifier.All.Where(region => pops.Any(pop => pop.Region == region))) { RegionOptions.Add(region); }
        SelectedRegion = RegionOptions.Contains(selected) ? selected : AllRegions;
        NotifySummaryChanged();
    }

    static int CountBlockedRoutes(IEnumerable<Pop> pops, IReadOnlySet<Relay> blocked)
    {
        return pops.Count(pop => pop.Relays.Any(blocked.Contains));
    }

    // ----- Filtering -----

    partial void OnSearchTextChanged(string value) => RefreshView();
    partial void OnSelectedRegionChanged(string value) => RefreshView();
    partial void OnSelectedStatusChanged(string value) => RefreshView();

    partial void OnGroupByRegionChanged(bool value)
    {
        settings.GroupByRegion = value;
        settingsStore.Save(settings);
        ApplyGrouping();
    }

    void ApplyGrouping()
    {
        RoutesView.GroupDescriptions.Clear();
        if (GroupByRegion) { RoutesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(RouteViewModel.Region))); }
    }

    void RefreshView()
    {
        RoutesView.Refresh();
        NotifySummaryChanged();
    }

    bool FilterRoute(RouteViewModel route)
    {
        if (SelectedRegion != AllRegions && route.Region != SelectedRegion) { return false; }
        if (SelectedStatus == StatusBlocked && route.IsBlocked == false) { return false; }
        if (SelectedStatus == StatusNotBlocked && route.IsBlocked != false) { return false; }
        string search = SearchText.Trim();
        return search.Length == 0
            || route.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
            || route.Code.Contains(search, StringComparison.OrdinalIgnoreCase)
            || route.Region.Contains(search, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand]
    void ClearFilters()
    {
        SearchText = "";
        SelectedRegion = AllRegions;
        SelectedStatus = StatusAll;
    }

    // ----- Blocking -----

    public bool IsRelayBlocked(Relay relay) => blockedRelays.Contains(relay);

    /// <summary>True if every relay of the route is blocked, false if none, null if only some.</summary>
    public bool? GetBlockedState(Pop pop)
    {
        int blockedCount = pop.Relays.Count(blockedRelays.Contains);
        if (blockedCount == 0) { return false; }
        return blockedCount == pop.Relays.Count ? true : null;
    }

    public void RequestSetRouteBlocked(RouteViewModel route, bool block)
    {
        _ = SetRouteRelaysBlockedAsync(route, block ? route.Pop.Relays.ToList() : new List<Relay>());
    }

    public void RequestSetRelayBlocked(RouteViewModel route, Relay relay, bool block)
    {
        List<Relay> relays = route.Pop.Relays.Where(blockedRelays.Contains).Where(other => other != relay).ToList();
        if (block) { relays.Add(relay); }
        _ = SetRouteRelaysBlockedAsync(route, relays);
    }

    // Blocks exactly the given relays of a route. The checkbox shows the click right away; if the firewall rejects the
    // change, the row is refreshed back to the real state.
    async Task SetRouteRelaysBlockedAsync(RouteViewModel route, List<Relay> relays)
    {
        if (!CanChangeRules)
        {
            // The checkbox already changed visually; put it back once the binding has finished updating.
            _ = Application.Current.Dispatcher.BeginInvoke((Action)route.Refresh);
            return;
        }

        bool unblocksAny = route.Pop.Relays.Any(relay => blockedRelays.Contains(relay) && !relays.Contains(relay));
        if (await RunFirewallChangeAsync(() => firewall.SetBlockedRelays(route.Pop, relays)))
        {
            blockedRelays.ExceptWith(route.Pop.Relays);
            blockedRelays.UnionWith(relays);
            SaveProfile();
            if (unblocksAny) { _ = PingRoutesAsync([route]); }
        }
        route.Refresh();
        OnBlockedStateChanged();
    }

    [RelayCommand(CanExecute = nameof(CanChangeRules))]
    async Task BlockAllAsync()
    {
        if (!Confirm("Block every route? You won't be able to connect to any Valve server until you unblock some.")) { return; }
        await ApplyBlockedAsync(pops.SelectMany(pop => pop.Relays).ToHashSet(), "Blocked every route.");
    }

    [RelayCommand(CanExecute = nameof(CanChangeRules))]
    Task UnblockAllAsync()
    {
        return ApplyBlockedAsync(new HashSet<Relay>(), "Unblocked every route and removed all of the tool's firewall rules.");
    }

    /// <summary>Allows only the routes the current filters show, e.g. one region: every other route is blocked.</summary>
    [RelayCommand(CanExecute = nameof(CanChangeRules))]
    async Task BlockAllExceptShownAsync()
    {
        HashSet<RouteViewModel> shown = RoutesView.Cast<RouteViewModel>().ToHashSet();
        if (shown.Count == 0 || shown.Count == Routes.Count)
        {
            StatusMessage = "Use the search or filters to show the routes you want to keep, then block all the others.";
            return;
        }
        int hidden = Routes.Count - shown.Count;
        if (!Confirm($"Block the {hidden} routes that aren't shown, and unblock the {shown.Count} shown routes?")) { return; }
        HashSet<Relay> blocked = Routes.Where(route => !shown.Contains(route)).SelectMany(route => route.Pop.Relays).ToHashSet();
        await ApplyBlockedAsync(blocked, $"Only the {shown.Count} shown routes are allowed; {hidden} routes are blocked.");
    }

    async Task ApplyBlockedAsync(HashSet<Relay> newBlocked, string message)
    {
        List<RouteViewModel> unblocked = Routes.Where(route => route.Pop.Relays.Any(relay => blockedRelays.Contains(relay) && !newBlocked.Contains(relay))).ToList();
        if (await RunFirewallChangeAsync(() => firewall.SyncRules(pops, newBlocked)))
        {
            blockedRelays = newBlocked;
            SaveProfile();
            StatusMessage = message;
            _ = PingRoutesAsync(unblocked);
        }
        RefreshAllRoutes();
    }

    // Runs a firewall change on a background thread so the window doesn't freeze (blocking everything is ~135 rule writes).
    async Task<bool> RunFirewallChangeAsync(Action change)
    {
        IsFirewallBusy = true;
        try
        {
            await Task.Run(change);
            return true;
        }
        catch (Exception ex) when (FirewallService.IsFirewallError(ex))
        {
            ShowFirewallError(ex);
            return false;
        }
        finally
        {
            IsFirewallBusy = false;
        }
    }

    void ShowFirewallError(Exception ex)
    {
        ErrorMessage = "Couldn't update the Windows Firewall rules: " + ex.Message +
            " Make sure the Windows Defender Firewall service is running and Steam Route Tool runs as administrator.";
    }

    void SaveProfile()
    {
        settings.Profiles[SelectedGame.AppId] = new GameProfile { BlockedRelays = blockedRelays.Select(relay => relay.Address).Distinct().ToList() };
        settingsStore.Save(settings);
    }

    static bool Confirm(string message)
    {
        return MessageBox.Show(message, "Steam Route Tool", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }

    void RefreshAllRoutes()
    {
        foreach (RouteViewModel route in Routes) { route.Refresh(); }
        OnBlockedStateChanged();
    }

    // The status filter depends on the blocked state, so the view is re-filtered when it's active.
    void OnBlockedStateChanged()
    {
        if (SelectedStatus != StatusAll) { RefreshView(); }
        else { NotifySummaryChanged(); }
    }

    [RelayCommand]
    void DismissMessages()
    {
        ErrorMessage = null;
        StatusMessage = null;
    }

    // ----- Auto select -----

    /// <summary>Whether the Auto select panel is open.</summary>
    [ObservableProperty]
    bool isAutoSelectOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutoSelectPreview))]
    int autoMaxPingMs;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutoSelectPreview))]
    int autoMinRoutes;

    partial void OnAutoMaxPingMsChanged(int value)
    {
        settings.AutoMaxPingMs = value;
        settingsStore.Save(settings);
    }

    partial void OnAutoMinRoutesChanged(int value)
    {
        settings.AutoMinRoutes = value;
        settingsStore.Save(settings);
    }

    [RelayCommand]
    void ToggleAutoSelect()
    {
        IsAutoSelectOpen = !IsAutoSelectOpen;
        OnPropertyChanged(nameof(AutoSelectPreview));
    }

    /// <summary>
    /// The best known ping of a route: this session's result if a relay answered, otherwise the last session's. Blocked
    /// routes can't be pinged, so for them the last session's value is all there is.
    /// </summary>
    int? GetKnownPing(Pop pop)
    {
        return pop.Relays
            .Select(relay => pingResults.GetValueOrDefault(relay.Address) ?? (cachedPings.TryGetValue(relay.Address, out int cached) ? cached : null))
            .Min();
    }

    IReadOnlyList<Pop> GetAutoSelectedRoutes()
    {
        return AutoSelector.SelectRoutesToKeep(pops.Select(pop => (pop, GetKnownPing(pop))), AutoMaxPingMs, AutoMinRoutes);
    }

    /// <summary>What Auto select would do with the current settings, e.g. "Keeps 9 routes and blocks 36: Warsaw (13 ms), ... and 3 more."</summary>
    public string AutoSelectPreview
    {
        get
        {
            if (pops.Count == 0) { return ""; }
            IReadOnlyList<Pop> kept = GetAutoSelectedRoutes();
            if (kept.Count == 0) { return "No pings are known yet. Click Ping all first."; }

            int unknown = pops.Count(pop => GetKnownPing(pop) == null);
            string names = string.Join(", ", kept.Take(6).Select(pop => $"{pop.Description} ({GetKnownPing(pop)} ms)")) + (kept.Count > 6 ? $" and {kept.Count - 6} more" : "");
            string preview = $"Keeps {kept.Count} {(kept.Count == 1 ? "route" : "routes")} and blocks {pops.Count - kept.Count}: {names}.";
            if (unknown > 0) { preview += $" {unknown} {(unknown == 1 ? "route has" : "routes have")} no known ping and will be blocked."; }
            if (IsPinging) { preview += " Pings are still being measured."; }
            return preview;
        }
    }

    /// <summary>Unblocks the selected routes and blocks every other route, all relays included.</summary>
    [RelayCommand(CanExecute = nameof(CanApplyAutoSelect))]
    async Task ApplyAutoSelectAsync()
    {
        IReadOnlyList<Pop> kept = GetAutoSelectedRoutes();
        if (kept.Count == 0)
        {
            StatusMessage = "Auto select needs at least one known ping. Click Ping all first.";
            return;
        }
        HashSet<Pop> keptSet = kept.ToHashSet();
        HashSet<Relay> blocked = pops.Where(pop => !keptSet.Contains(pop)).SelectMany(pop => pop.Relays).ToHashSet();
        IsAutoSelectOpen = false;
        await ApplyBlockedAsync(blocked, $"Auto select kept the {kept.Count} fastest {(kept.Count == 1 ? "route" : "routes")} and blocked {pops.Count - kept.Count}.");
    }

    bool CanApplyAutoSelect() => CanChangeRules && !IsPinging;

    // ----- Pinging -----

    public PingText DescribePing(IEnumerable<Relay> relays)
    {
        return PingDisplay.Describe(relays, blockedRelays, pingResults, pendingPings, cachedPings);
    }

    [RelayCommand(CanExecute = nameof(CanPingAll))]
    async Task PingAllAsync()
    {
        IsPinging = true;
        await PingRoutesAsync(Routes.ToList());
        IsPinging = false;
        SavePingCache();
    }

    bool CanPingAll() => !IsPinging && !IsLoading;

    public void OnRouteExpanded(RouteViewModel route)
    {
        _ = PingRoutesAsync([route]);
    }

    public void RequestPingRoute(RouteViewModel route)
    {
        _ = PingRoutesAsync([route]);
    }

    public void RequestPingRelay(RouteViewModel route, Relay relay)
    {
        _ = PingRelaysAsync(route, [relay]);
    }

    /// <summary>
    /// Pings routes in two rounds (see <see cref="PingService"/>), shown routes first:
    /// 1. Quick: the first relay of each collapsed route (every relay of an expanded one), several at a time. On networks
    ///    without ICMP limits this is all it takes.
    /// 2. Careful: whatever didn't answer is retried one at a time; collapsed routes try further relays until one answers.
    /// Blocked relays are skipped (their ICMP is blocked too), and so are relays another run is already pinging: each run
    /// only clears the pending state of the relays it pinged itself.
    /// </summary>
    async Task PingRoutesAsync(IReadOnlyList<RouteViewModel> routes)
    {
        List<(RouteViewModel Route, List<Relay> Relays)> runs = new List<(RouteViewModel Route, List<Relay> Relays)>();
        foreach (RouteViewModel route in OrderShownFirst(routes))
        {
            List<Relay> relays = route.Pop.Relays.Where(relay => !blockedRelays.Contains(relay) && !pendingPings.Contains(relay.Address)).ToList();
            if (relays.Count == 0) { continue; }

            foreach (Relay relay in relays)
            {
                pingResults.Remove(relay.Address);
                pendingPings.Add(relay.Address);
            }
            runs.Add((route, relays));
            route.Refresh();
        }

        await Task.WhenAll(runs.SelectMany(run => (run.Route.IsExpanded ? run.Relays : [run.Relays[0]]).Select(relay => PingRelayAsync(run.Route, relay, quick: true))));
        foreach ((RouteViewModel route, List<Relay> relays) in runs)
        {
            if (route.IsExpanded)
            {
                foreach (Relay relay in relays.Where(relay => pingResults.GetValueOrDefault(relay.Address) == null).ToList())
                {
                    await PingRelayAsync(route, relay, quick: false);
                }
            }
            else if (!route.Pop.Relays.Any(relay => pingResults.GetValueOrDefault(relay.Address) != null))
            {
                foreach (Relay relay in relays)
                {
                    await PingRelayAsync(route, relay, quick: false);
                    if (pingResults.GetValueOrDefault(relay.Address) != null) { break; }
                }
            }
            pendingPings.ExceptWith(relays.Select(relay => relay.Address));
            route.Refresh();
        }
    }

    async Task PingRelaysAsync(RouteViewModel route, IReadOnlyList<Relay> relays)
    {
        List<Relay> toPing = relays.Where(relay => !blockedRelays.Contains(relay) && !pendingPings.Contains(relay.Address)).ToList();
        foreach (Relay relay in toPing)
        {
            pingResults.Remove(relay.Address);
            pendingPings.Add(relay.Address);
        }
        route.Refresh();
        foreach (Relay relay in toPing) { await PingRelayAsync(route, relay, quick: false); }
        pendingPings.ExceptWith(toPing.Select(relay => relay.Address));
        route.Refresh();
    }

    async Task PingRelayAsync(RouteViewModel route, Relay relay, bool quick)
    {
        int? responseTime = quick ? await pingService.PingQuicklyAsync(relay.Address) : await pingService.PingCarefullyAsync(relay.Address);
        pingResults[relay.Address] = responseTime;
        route.Refresh();
        if (IsAutoSelectOpen) { OnPropertyChanged(nameof(AutoSelectPreview)); }
    }

    List<RouteViewModel> OrderShownFirst(IReadOnlyList<RouteViewModel> routes)
    {
        List<RouteViewModel> shown = RoutesView.Cast<RouteViewModel>().ToList();
        return routes.OrderBy(route => shown.IndexOf(route) is int index && index >= 0 ? index : int.MaxValue).ToList();
    }

    /// <summary>Saves this session's successful pings so the next launch can show them right away.</summary>
    public void SavePingCache()
    {
        foreach ((string address, int? responseTime) in pingResults)
        {
            if (responseTime != null) { cachedPings[address] = responseTime.Value; }
        }
        pingCache.Save(cachedPings);
    }
}
