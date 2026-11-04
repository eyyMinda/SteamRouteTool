using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SteamRouteTool.Localization;
using SteamRouteTool.Models;
using SteamRouteTool.Services;

namespace SteamRouteTool.ViewModels;

/// <summary>
/// The route list of the selected game: loading the network config, blocking routes through the firewall, pinging and
/// filtering. All members run on the UI thread; firewall changes are moved to a background thread while they run.
/// </summary>
public sealed partial class RoutesViewModel : ObservableObject
{
    public const string AllRegions = "All";
    public const string StatusAll = "All";
    public const string StatusBlocked = "Blocked";
    public const string StatusNotBlocked = "NotBlocked";

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
        autoRouteCount = Math.Clamp(settings.AutoRouteCount, 1, 20);

        ListCollectionView view = (ListCollectionView)CollectionViewSource.GetDefaultView(Routes);
        view.Filter = item => FilterRoute((RouteViewModel)item);
        view.SortDescriptions.Add(new SortDescription(nameof(RouteViewModel.RegionOrder), ListSortDirection.Ascending));
        view.SortDescriptions.Add(new SortDescription(nameof(RouteViewModel.Name), ListSortDirection.Ascending));
        // No live sorting: rows don't jump around while pings arrive (sorting by Ping again re-sorts), and WPF's live
        // shaping misorders grouped rows. Blocked-state changes refresh the view explicitly for the status filter.
        RoutesView = view;
        ApplyGrouping();
        Loc.Instance.Changed += OnLanguageChanged;
    }

    // Texts built here are refreshed; messages already shown are dismissed rather than left in the old language.
    void OnLanguageChanged()
    {
        UpdateFirewallWarning();
        DismissMessages();
        PresetMessage = null;
        foreach (RouteViewModel route in Routes) { route.RefreshTexts(); }
        foreach (PresetViewModel preset in Presets) { preset.Refresh(); }
        // Group headers are the translated region names.
        ApplyGrouping();
        RefreshView();
        OnPropertyChanged(nameof(AutoSelectLabel));
        OnPropertyChanged(nameof(SavePresetLabel));
        OnPropertyChanged(nameof(PresetsTitle));
    }

    public ObservableCollection<RouteViewModel> Routes { get; } = new ObservableCollection<RouteViewModel>();
    public ICollectionView RoutesView { get; }
    public IReadOnlyList<Game> Games => Game.Supported;
    /// <summary>The region filter's choices; <see cref="LocalizedOption.Key"/> is the internal region name.</summary>
    public ObservableCollection<LocalizedOption> RegionOptions { get; } = new ObservableCollection<LocalizedOption> { AllRegionsOption };
    public IReadOnlyList<LocalizedOption> StatusOptions { get; } =
    [
        new LocalizedOption(StatusAll, "Routes.StatusAll"),
        new LocalizedOption(StatusBlocked, "Routes.StatusBlocked"),
        new LocalizedOption(StatusNotBlocked, "Routes.StatusNotBlocked"),
    ];

    static readonly LocalizedOption AllRegionsOption = new LocalizedOption(AllRegions, "Routes.AllRegions");
    static readonly Dictionary<string, LocalizedOption> RegionOptionsByName = RegionClassifier.All
        .ToDictionary(region => region, region => new LocalizedOption(region, "Region." + region.Replace(" ", "")));

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
        OnPropertyChanged(nameof(BlockedSummary));
        OnPropertyChanged(nameof(HasNoShownRoutes));
        OnPropertyChanged(nameof(AutoSelectPreview));
        NotifyHeadlineChanged();
        UpdateSelectedPreset();
    }

    /// <summary>E.g. "40 of 45 routes blocked", or empty before the routes have loaded. Shown in the tray icon's tooltip.</summary>
    public string BlockedSummary => Routes.Count == 0 ? "" : Loc.T("Routes.BlockedSummary", BlockedCount, Routes.Count);

    int BlockedCount => Routes.Count(route => route.IsBlocked != false);

    /// <summary>Routes with at least one blocked relay, for the headline numbers.</summary>
    public int BlockedRouteCount => BlockedCount;
    public int AllowedRouteCount => Routes.Count - BlockedCount;

    // The best known ping of every route that's still (at least partly) allowed, fastest first.
    List<(Pop Pop, int Ms)> AllowedPings => pops
        .Where(pop => pop.Relays.Any(relay => !blockedRelays.Contains(relay)))
        .Select(pop => (Pop: pop, Ms: GetKnownPing(pop)))
        .Where(route => route.Ms != null)
        .Select(route => (route.Pop, route.Ms!.Value))
        .OrderBy(route => route.Value)
        .ToList();

    /// <summary>The fastest allowed route's ping, e.g. "17 ms", or "–" before any ping is known.</summary>
    public string BestPingText => AllowedPings.FirstOrDefault() is { Pop: not null } best ? Loc.T("Ping.Ms", best.Ms) : "–";

    /// <summary>The slowest route still allowed: the worst server matchmaking could still pick.</summary>
    public string WorstPingText => AllowedPings.LastOrDefault() is { Pop: not null } worst ? Loc.T("Ping.Ms", worst.Ms) : "–";

    public PingLevel BestPingLevel => AllowedPings.FirstOrDefault() is { Pop: not null } best ? PingDisplay.LevelOf(best.Ms) : PingLevel.None;
    public PingLevel WorstPingLevel => AllowedPings.LastOrDefault() is { Pop: not null } worst ? PingDisplay.LevelOf(worst.Ms) : PingLevel.None;

    /// <summary>E.g. "Fastest location you allow: Stockholm - Kista (Sweden)".</summary>
    public string BestPingTooltip => AllowedPings.FirstOrDefault() is { Pop: not null } best ? Loc.T("Routes.BestPingTooltip", best.Pop.Description) : "";
    public string WorstPingTooltip => AllowedPings.LastOrDefault() is { Pop: not null } worst ? Loc.T("Routes.WorstPingTooltip", worst.Pop.Description) : "";

    void NotifyHeadlineChanged()
    {
        OnPropertyChanged(nameof(BlockedRouteCount));
        OnPropertyChanged(nameof(AllowedRouteCount));
        NotifyPingHeadlineChanged();
    }

    void NotifyPingHeadlineChanged()
    {
        foreach (string name in new[] { nameof(BestPingText), nameof(WorstPingText), nameof(BestPingLevel), nameof(WorstPingLevel), nameof(BestPingTooltip), nameof(WorstPingTooltip) })
        {
            OnPropertyChanged(name);
        }
    }

    // ----- Loading -----

    public async Task InitializeAsync()
    {
        if (initialized) { return; }
        initialized = true;
        _ = CheckFirewallAsync();
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
        OnPropertyChanged(nameof(PresetsTitle));
        settings.LastAppId = value.AppId;
        settingsStore.Save(settings);
        if (initialized) { _ = LoadGameAsync(value, fromFirewall: false); }
    }

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    Task RefreshAsync()
    {
        _ = CheckFirewallAsync();
        return LoadGameAsync(SelectedGame, fromFirewall: true);
    }

    // ----- New locations -----

    /// <summary>Tells about locations Valve added since the game was last loaded.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanBlockNewRoutes))]
    string? newRoutesMessage;

    // The added locations that are still allowed, for "Block".
    List<Pop> newRoutes = new List<Pop>();

    public bool CanBlockNewRoutes => newRoutes.Count > 0;

    [RelayCommand]
    async Task BlockNewRoutesAsync()
    {
        HashSet<Relay> blocked = blockedRelays.ToHashSet();
        blocked.UnionWith(newRoutes.SelectMany(pop => pop.Relays));
        int count = newRoutes.Count;
        if (await ApplyBlockedAsync(blocked, Loc.Plural("NewRoutes.BlockedNow", count)))
        {
            newRoutes = new List<Pop>();
            NewRoutesMessage = null;
        }
    }

    [RelayCommand]
    void DismissNewRoutes() => NewRoutesMessage = null;

    /// <summary>Called when "Block new locations automatically" changes, so the preset list matches again.</summary>
    public void OnBlockNewRoutesChanged() => UpdateSelectedPreset();

    // ----- Windows Firewall state -----

    /// <summary>Shown while Windows Firewall is off for an active network, since the rules then block nothing.</summary>
    [ObservableProperty]
    string? firewallWarning;

    int disabledProfiles;

    /// <summary>Checks whether Windows Firewall is on; called at startup, on Refresh and when the window is activated.</summary>
    [RelayCommand]
    public async Task CheckFirewallAsync()
    {
        try
        {
            disabledProfiles = await Task.Run(firewall.GetDisabledActiveProfiles);
        }
        catch (Exception ex) when (FirewallService.IsFirewallError(ex) || ex is InvalidCastException || ex is Microsoft.CSharp.RuntimeBinder.RuntimeBinderException)
        {
            // Unknown state (e.g. a third-party firewall in charge): don't warn on a guess.
            disabledProfiles = 0;
        }
        UpdateFirewallWarning();
    }

    void UpdateFirewallWarning()
    {
        List<string> networks = new List<string>();
        if ((disabledProfiles & FirewallService.ProfileDomain) != 0) { networks.Add(Loc.T("Firewall.Domain")); }
        if ((disabledProfiles & FirewallService.ProfilePrivate) != 0) { networks.Add(Loc.T("Firewall.Private")); }
        if ((disabledProfiles & FirewallService.ProfilePublic) != 0) { networks.Add(Loc.T("Firewall.Public")); }
        FirewallWarning = networks.Count == 0 ? null : Loc.T("Firewall.Off", string.Join(", ", networks));
    }

    [RelayCommand]
    void OpenFirewallSettings() => Shell.Open("firewall.cpl");

    bool CanRefresh() => !IsLoading;

    /// <summary>
    /// Loads the game's routes and decides what's blocked: on launch (or refresh) the existing firewall rules, when switching
    /// games the game's saved profile. The rules are then synced so they match exactly, which also removes outdated rules.
    /// </summary>
    async Task LoadGameAsync(Game game, bool fromFirewall)
    {
        int load = ++loadVersion;
        IsLoading = true;
        CanUndo = false;
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
            ErrorMessage = Loc.T("Routes.LoadFailed", game.Name, ex.Message);
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
            appliedMessage = Loc.Plural("Routes.ProfileApplied", blockedRoutes, game.Name);
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

        // Locations Valve added since this game was last loaded: allowed (with a notice) or, if set, blocked.
        List<Pop> added = PresetLogic.FindNewRoutes(settings.KnownRoutes.GetValueOrDefault(game.AppId), loaded);
        if (added.Count > 0 && blocked != null && settings.BlockNewRoutes) { blocked.UnionWith(added.SelectMany(pop => pop.Relays)); }
        settings.KnownRoutes[game.AppId] = loaded.Select(pop => pop.Code).ToList();
        settingsStore.Save(settings);
        newRoutes = settings.BlockNewRoutes ? new List<Pop>() : added;
        string names = string.Join(", ", added.Select(pop => pop.Description));
        NewRoutesMessage = added.Count == 0 ? null
            : Loc.Plural(settings.BlockNewRoutes ? "NewRoutes.Blocked" : "NewRoutes.Found", added.Count, names);

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
        RegionOptions.Add(AllRegionsOption);
        foreach (string region in RegionClassifier.All.Where(region => pops.Any(pop => pop.Region == region))) { RegionOptions.Add(RegionOptionsByName[region]); }
        SelectedRegion = RegionOptions.Any(option => option.Key == selected) ? selected : AllRegions;
        LoadPresets();
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

    public const string SortRegion = "Region";
    public const string SortName = "Name";
    public const string SortPing = "Ping";
    public const string SortStatus = "Status";

    public IReadOnlyList<LocalizedOption> SortOptions { get; } =
    [
        new LocalizedOption(SortRegion, "Routes.SortRegion"),
        new LocalizedOption(SortName, "Routes.SortName"),
        new LocalizedOption(SortPing, "Routes.SortPing"),
        new LocalizedOption(SortStatus, "Routes.SortStatus"),
    ];

    [ObservableProperty]
    string selectedSort = SortRegion;

    partial void OnSelectedSortChanged(string value) => ApplySort();

    // Ping order isn't live (rows would jump while pings arrive); picking the sort again re-sorts.
    void ApplySort()
    {
        using (RoutesView.DeferRefresh())
        {
            RoutesView.SortDescriptions.Clear();
            string first = SelectedSort switch
            {
                SortName => nameof(RouteViewModel.Name),
                SortPing => nameof(RouteViewModel.PingSortValue),
                SortStatus => nameof(RouteViewModel.BlockedSortValue),
                _ => nameof(RouteViewModel.RegionOrder),
            };
            RoutesView.SortDescriptions.Add(new SortDescription(first, ListSortDirection.Ascending));
            if (first != nameof(RouteViewModel.Name)) { RoutesView.SortDescriptions.Add(new SortDescription(nameof(RouteViewModel.Name), ListSortDirection.Ascending)); }
        }
    }

    partial void OnGroupByRegionChanged(bool value)
    {
        settings.GroupByRegion = value;
        settingsStore.Save(settings);
        ApplyGrouping();
    }

    void ApplyGrouping()
    {
        RoutesView.GroupDescriptions.Clear();
        if (GroupByRegion) { RoutesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(RouteViewModel.RegionName))); }
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
            || route.Region.Contains(search, StringComparison.OrdinalIgnoreCase)
            || route.RegionName.Contains(search, StringComparison.OrdinalIgnoreCase);
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

        CanUndo = false;
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
        if (!Confirm(Loc.T("Routes.ConfirmBlockAll"))) { return; }
        await ApplyBlockedAsync(pops.SelectMany(pop => pop.Relays).ToHashSet(), Loc.T("Routes.BlockedAll"));
    }

    [RelayCommand(CanExecute = nameof(CanChangeRules))]
    Task UnblockAllAsync()
    {
        return ApplyBlockedAsync(new HashSet<Relay>(), Loc.T("Routes.UnblockedAll"));
    }

    /// <summary>Allows only the routes the current filters show, e.g. one region: every other route is blocked.</summary>
    [RelayCommand(CanExecute = nameof(CanChangeRules))]
    async Task BlockAllExceptShownAsync()
    {
        HashSet<RouteViewModel> shown = RoutesView.Cast<RouteViewModel>().ToHashSet();
        if (shown.Count == 0 || shown.Count == Routes.Count)
        {
            StatusMessage = Loc.T("Routes.ShowRoutesFirst");
            return;
        }
        int hidden = Routes.Count - shown.Count;
        if (!Confirm(Loc.T("Routes.ConfirmBlockAllExceptShown", hidden, shown.Count))) { return; }
        HashSet<Relay> blocked = Routes.Where(route => !shown.Contains(route)).SelectMany(route => route.Pop.Relays).ToHashSet();
        await ApplyBlockedAsync(blocked, Loc.T("Routes.BlockedAllExceptShown", shown.Count, hidden));
    }

    async Task<bool> ApplyBlockedAsync(HashSet<Relay> newBlocked, string message)
    {
        CanUndo = false;
        List<RouteViewModel> unblocked = Routes.Where(route => route.Pop.Relays.Any(relay => blockedRelays.Contains(relay) && !newBlocked.Contains(relay))).ToList();
        bool applied = await RunFirewallChangeAsync(() => firewall.SyncRules(pops, newBlocked));
        if (applied)
        {
            blockedRelays = newBlocked;
            SaveProfile();
            StatusMessage = message;
            _ = PingRoutesAsync(unblocked);
        }
        RefreshAllRoutes();
        return applied;
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
        ErrorMessage = Loc.T("Routes.FirewallError", ex.Message);
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
        CanUndo = false;
    }

    // ----- Undo -----

    /// <summary>True right after Auto select or a preset was applied, until anything else changes the blocked routes.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    bool canUndo;

    // What was blocked before the last Auto select or preset, for Undo.
    HashSet<Relay>? blockedBeforeUndo;

    void OfferUndo(HashSet<Relay> before)
    {
        blockedBeforeUndo = before;
        CanUndo = true;
    }

    [RelayCommand(CanExecute = nameof(CanUndo))]
    async Task UndoAsync()
    {
        if (blockedBeforeUndo == null) { return; }
        await ApplyBlockedAsync(blockedBeforeUndo, Loc.T("Routes.Restored"));
    }

    // ----- Presets -----

    /// <summary>The selected game's presets.</summary>
    public ObservableCollection<PresetViewModel> Presets { get; } = new ObservableCollection<PresetViewModel>();

    public bool HasPresets => Presets.Count > 0;

    /// <summary>E.g. "Counter-Strike 2 presets".</summary>
    public string PresetsTitle => Loc.T("Presets.ListTitle", SelectedGame.Name);

    /// <summary>The preset that matches what's blocked now, or null. Picking one in the list applies it.</summary>
    [ObservableProperty]
    PresetViewModel? selectedPreset;

    bool updatingSelectedPreset;

    /// <summary>Whether the panel to save, rename and delete presets is open.</summary>
    [ObservableProperty]
    bool isPresetsOpen;

    /// <summary>The name typed to save the current blocked routes under.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SavePresetLabel))]
    [NotifyCanExecuteChangedFor(nameof(SavePresetCommand))]
    string newPresetName = "";

    /// <summary>Feedback inside the presets panel, e.g. "Saved "EU blocked".".</summary>
    [ObservableProperty]
    string? presetMessage;

    /// <summary>"Update" when the typed name is an existing preset (it's then overwritten), otherwise "Save".</summary>
    public string SavePresetLabel => Loc.T(FindPreset(PresetLogic.CleanName(NewPresetName)) != null ? "Presets.Update" : "Presets.Save");

    List<GamePreset> GamePresets
    {
        get
        {
            if (!settings.Presets.TryGetValue(SelectedGame.AppId, out List<GamePreset>? presets))
            {
                presets = new List<GamePreset>();
                settings.Presets[SelectedGame.AppId] = presets;
            }
            return presets;
        }
    }

    PresetViewModel? FindPreset(string? name)
    {
        return name == null ? null : Presets.FirstOrDefault(preset => preset.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    void LoadPresets()
    {
        Presets.Clear();
        foreach (GamePreset preset in GamePresets) { Presets.Add(new PresetViewModel(this, preset)); }
        OnPropertyChanged(nameof(HasPresets));
        OnPropertyChanged(nameof(SavePresetLabel));
        SavePresetCommand.NotifyCanExecuteChanged();
        PresetMessage = null;
    }

    // Shows which preset (if any) matches the blocked routes, without applying it.
    void UpdateSelectedPreset()
    {
        updatingSelectedPreset = true;
        SelectedPreset = pops.Count == 0 ? null : Presets.FirstOrDefault(preset => PresetLogic.Matches(preset.Model, pops, blockedRelays, settings.BlockNewRoutes));
        updatingSelectedPreset = false;
    }

    partial void OnSelectedPresetChanged(PresetViewModel? value)
    {
        if (updatingSelectedPreset || value == null) { return; }
        _ = ApplyPresetAsync(value);
    }

    /// <summary>Blocks exactly what the preset says, with Undo.</summary>
    public async Task ApplyPresetAsync(PresetViewModel preset)
    {
        if (!CanChangeRules || pops.Count == 0)
        {
            UpdateSelectedPreset();
            return;
        }
        HashSet<Relay> before = blockedRelays.ToHashSet();
        HashSet<Relay> blocked = PresetLogic.Resolve(preset.Model, pops, settings.BlockNewRoutes);
        int routes = CountBlockedRoutes(pops, blocked);
        string message = Loc.Plural("Presets.Applied", routes, preset.Name);
        if (await ApplyBlockedAsync(blocked, message)) { OfferUndo(before); }
    }

    /// <summary>Saves what's blocked now under the typed name, as a new preset or over the preset with that name.</summary>
    [RelayCommand(CanExecute = nameof(CanSavePreset))]
    void SavePreset()
    {
        string? name = PresetLogic.CleanName(NewPresetName);
        if (name == null) { return; }
        GamePreset captured = PresetLogic.Capture(name, pops, blockedRelays);
        PresetViewModel? existing = FindPreset(name);
        if (existing != null)
        {
            existing.Model.BlockedRoutes = captured.BlockedRoutes;
            existing.Model.BlockedRelays = captured.BlockedRelays;
            existing.Refresh();
            PresetMessage = Loc.T("Presets.Updated", existing.Name);
        }
        else
        {
            GamePresets.Add(captured);
            Presets.Add(new PresetViewModel(this, captured));
            OnPropertyChanged(nameof(HasPresets));
            PresetMessage = Loc.T("Presets.Saved", name);
        }
        settingsStore.Save(settings);
        NewPresetName = "";
        UpdateSelectedPreset();
    }

    bool CanSavePreset() => pops.Count > 0 && PresetLogic.CleanName(NewPresetName) != null;

    /// <summary>Renames a preset. Empty names and names of other presets are refused; returns whether it was renamed.</summary>
    public bool RenamePreset(PresetViewModel preset, string newName)
    {
        string? name = PresetLogic.CleanName(newName);
        if (name == null)
        {
            PresetMessage = Loc.T("Presets.NeedsName");
            return false;
        }
        PresetViewModel? other = FindPreset(name);
        if (other != null && other != preset)
        {
            PresetMessage = Loc.T("Presets.NameTaken", other.Name);
            return false;
        }
        if (name == preset.Model.Name) { return true; }
        preset.Model.Name = name;
        settingsStore.Save(settings);
        PresetMessage = Loc.T("Presets.Renamed", name);
        OnPropertyChanged(nameof(SavePresetLabel));
        return true;
    }

    public void DeletePreset(PresetViewModel preset)
    {
        if (!Confirm(Loc.T("Presets.ConfirmDelete", preset.Name))) { return; }
        GamePresets.Remove(preset.Model);
        Presets.Remove(preset);
        settingsStore.Save(settings);
        OnPropertyChanged(nameof(HasPresets));
        OnPropertyChanged(nameof(SavePresetLabel));
        PresetMessage = Loc.T("Presets.Deleted", preset.Name);
        UpdateSelectedPreset();
    }

    // ----- Auto select -----

    /// <summary>How many of the fastest routes Auto select keeps.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AutoSelectPreview), nameof(AutoSelectLabel))]
    int autoRouteCount;

    public string AutoSelectLabel => Loc.T("AutoSelect.Label", AutoRouteCount);

    /// <summary>The − / + buttons next to the count: the parameter is the step, e.g. "1" or "-1".</summary>
    [RelayCommand]
    void ChangeAutoRouteCount(string step)
    {
        AutoRouteCount = Math.Clamp(AutoRouteCount + int.Parse(step, System.Globalization.CultureInfo.InvariantCulture), 1, 20);
    }

    partial void OnAutoRouteCountChanged(int value)
    {
        settings.AutoRouteCount = value;
        settingsStore.Save(settings);
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
        return AutoSelector.SelectFastest(pops.Select(pop => (pop, GetKnownPing(pop))), AutoRouteCount);
    }

    string DescribeRoutes(IReadOnlyList<Pop> routes, int shown)
    {
        string names = string.Join(", ", routes.Take(shown).Select(pop => Loc.T("AutoSelect.RouteWithPing", pop.Description, GetKnownPing(pop))));
        return routes.Count > shown ? Loc.T("AutoSelect.AndMore", names, routes.Count - shown) : names;
    }

    /// <summary>What Auto select would do, e.g. "Keeps Warsaw (Poland) (13 ms), ... Blocks the other 40 routes."</summary>
    public string AutoSelectPreview
    {
        get
        {
            if (pops.Count == 0) { return ""; }
            IReadOnlyList<Pop> kept = GetAutoSelectedRoutes();
            if (kept.Count == 0) { return Loc.T("AutoSelect.NoPings"); }

            string preview = Loc.T("AutoSelect.Preview", DescribeRoutes(kept, 10), pops.Count - kept.Count);
            int unknown = pops.Count(pop => GetKnownPing(pop) == null);
            if (unknown > 0) { preview += Loc.Plural("AutoSelect.Unknown", unknown); }
            if (IsPinging) { preview += Loc.T("AutoSelect.StillPinging"); }
            return preview;
        }
    }

    /// <summary>Keeps the fastest routes and blocks every other route, all relays included.</summary>
    [RelayCommand(CanExecute = nameof(CanApplyAutoSelect))]
    async Task ApplyAutoSelectAsync()
    {
        IReadOnlyList<Pop> kept = GetAutoSelectedRoutes();
        if (kept.Count == 0)
        {
            StatusMessage = Loc.T("AutoSelect.NeedsPing");
            return;
        }

        HashSet<Pop> keptSet = kept.ToHashSet();
        HashSet<Relay> blocked = pops.Where(pop => !keptSet.Contains(pop)).SelectMany(pop => pop.Relays).ToHashSet();
        HashSet<Relay> before = blockedRelays.ToHashSet();
        string message = Loc.Plural("AutoSelect.Kept", kept.Count, DescribeRoutes(kept, 5), pops.Count - kept.Count);
        if (await ApplyBlockedAsync(blocked, message)) { OfferUndo(before); }
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
        if (SelectedSort == SortPing) { RefreshView(); }
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
        OnPropertyChanged(nameof(AutoSelectPreview));
        NotifyPingHeadlineChanged();
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
