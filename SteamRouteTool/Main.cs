using System.Runtime.InteropServices;
using SteamRouteTool.Models;
using SteamRouteTool.Services;

namespace SteamRouteTool;

public partial class Main : Form
{
    static readonly HttpClient httpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(15) };
    readonly SdrConfigClient configClient = new SdrConfigClient(httpClient);
    readonly FirewallService firewall = new FirewallService();
    readonly PingService pingService = new PingService();
    readonly PingCache pingCache = new PingCache(PingCache.DefaultPath);

    IReadOnlyList<Pop> pops = Array.Empty<Pop>();
    // View state: which relays are blocked (mirrors the firewall rules) and which routes show each relay as its own row.
    readonly HashSet<Relay> blockedRelays = new HashSet<Relay>();
    readonly HashSet<Pop> expandedPops = new HashSet<Pop>();
    // The route and relay shown in each grid row. Every relay has a row; collapsed routes only show their first one.
    readonly List<(Pop Pop, int RelayIndex)> rows = new List<(Pop Pop, int RelayIndex)>();
    // This session's ping result per relay (null = no reply) and relays with a ping queued or in progress.
    readonly Dictionary<Relay, int?> pingResults = new Dictionary<Relay, int?>();
    readonly HashSet<Relay> pendingPings = new HashSet<Relay>();
    // Last successful ping per relay address from earlier sessions, shown greyed until a fresh result arrives.
    readonly Dictionary<string, int> cachedPings;
    // Firewall changes from clicks wait until the rules have been synced on launch.
    bool firewallReady;

    public Main()
    {
        InitializeComponent();
        btn_ClearRules.Enabled = false;
        cachedPings = pingCache.Load();
        Shown += async (sender, e) => await LoadRoutesAsync();
        FormClosing += (sender, e) => SavePingCache();
    }

    private static bool IsFirewallError(Exception ex)
    {
        return ex is COMException || ex is UnauthorizedAccessException;
    }

    private static void ShowFirewallError(Exception ex)
    {
        MessageBox.Show("Couldn't update the Windows Firewall rules:" + Environment.NewLine + ex.Message + Environment.NewLine + Environment.NewLine +
            "Make sure the Windows Defender Firewall service is running and the tool is running as administrator.",
            "Steam Route Tool - Firewall Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }

    // Runs a firewall change, showing an error instead of crashing if Windows Firewall rejects it.
    private static bool TryFirewall(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex) when (IsFirewallError(ex))
        {
            ShowFirewallError(ex);
            return false;
        }
    }

    private static async Task<bool> TryFirewallAsync(Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (Exception ex) when (IsFirewallError(ex))
        {
            ShowFirewallError(ex);
            return false;
        }
    }

    private async Task LoadRoutesAsync()
    {
        while (true)
        {
            try
            {
                pops = await configClient.GetPopsAsync();
                break;
            }
            catch (Exception ex)
            {
                DialogResult retry = MessageBox.Show("Couldn't load the Steam network config:" + Environment.NewLine + ex.Message,
                    "Steam Route Tool - Getting Routes Failed", MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);
                if (retry != DialogResult.Retry)
                {
                    Close();
                    return;
                }
            }
        }

        lb_GettingRoutes.Visible = false;
        btn_About.Visible = true;
        BuildGrid();

        // Reading the rules is quick, so the checkboxes are right before pings start. Syncing the rules with the
        // current config then runs in the background and only rewrites rules that changed.
        bool rulesRead = await TryFirewallAsync(ShowBlockedRoutesAsync);
        _ = PingAllAsync();
        if (rulesRead)
        {
            HashSet<Relay> blocked = blockedRelays.ToHashSet();
            await TryFirewallAsync(() => Task.Run(() => firewall.SyncRules(pops, blocked)));
        }
        firewallReady = true;
        btn_ClearRules.Enabled = true;
    }

    private void BuildGrid()
    {
        routeDataGrid.Rows.Clear();
        rows.Clear();
        foreach (Pop pop in pops)
        {
            for (int i = 0; i < pop.Relays.Count; i++)
            {
                routeDataGrid.Rows.Add();
                rows.Add((pop, i));
            }
        }
        RefreshGrid();
    }

    // Updates names, visibility, pings and checkboxes from the view state.
    private void RefreshGrid()
    {
        // The current cell can't be in a row that gets hidden.
        routeDataGrid.CurrentCell = null;
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            (Pop pop, int relayIndex) = rows[rowIndex];
            bool expanded = expandedPops.Contains(pop);
            DataGridViewRow row = routeDataGrid.Rows[rowIndex];
            row.Visible = expanded || relayIndex == 0;
            row.Cells[colName.Index].Value = GetRowName(pop, relayIndex, expanded);
            row.Cells[colBlocked.Index].Value = expanded ? ToCheckState(blockedRelays.Contains(pop.Relays[relayIndex])) : GetBlockedState(pop);
            RefreshPingCell(rowIndex);
        }
    }

    // An expanded row shows its relay's ping; a collapsed route shows the best ping of any of its relays.
    private void RefreshPingCell(int rowIndex)
    {
        (Pop pop, int relayIndex) = rows[rowIndex];
        IReadOnlyList<Relay> relays = expandedPops.Contains(pop) ? [pop.Relays[relayIndex]] : pop.Relays;
        DataGridViewCell cell = routeDataGrid.Rows[rowIndex].Cells[colPing.Index];

        // Fresh results win; while pinging (or before the first ping) the last session's value is shown greyed.
        int? best = relays.Where(pingResults.ContainsKey).Select(relay => pingResults[relay]).Min();
        int? cached = relays.Select(relay => cachedPings.TryGetValue(relay.Address, out int ping) ? ping : (int?)null).Min();
        bool pending = relays.Any(pendingPings.Contains);
        if (best != null) { (cell.Value, cell.Style.ForeColor) = (best.ToString(), GetPingColor(best)); }
        else if (pending || !relays.Any(pingResults.ContainsKey)) { (cell.Value, cell.Style.ForeColor) = (cached?.ToString() ?? (pending ? "…" : ""), Color.Gray); }
        else { (cell.Value, cell.Style.ForeColor) = ("-1", GetPingColor(null)); }
    }

    private void RefreshPingCells(Pop pop)
    {
        for (int rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            if (rows[rowIndex].Pop == pop) { RefreshPingCell(rowIndex); }
        }
    }

    // Routes with several relays can be expanded to block relays individually: "▸ Name" collapsed, "▾ Name 1" / "   Name 2" expanded.
    private static string GetRowName(Pop pop, int relayIndex, bool expanded)
    {
        if (pop.Relays.Count == 1) { return pop.Description; }
        if (!expanded) { return "▸ " + pop.Description; }
        return (relayIndex == 0 ? "▾ " : "    ") + pop.Description + " " + (relayIndex + 1);
    }

    private static CheckState ToCheckState(bool isChecked)
    {
        return isChecked ? CheckState.Checked : CheckState.Unchecked;
    }

    // A collapsed route shows as checked when all its relays are blocked, and indeterminate when only some are.
    private CheckState GetBlockedState(Pop pop)
    {
        int blockedCount = pop.Relays.Count(blockedRelays.Contains);
        if (blockedCount == 0) { return CheckState.Unchecked; }
        return blockedCount == pop.Relays.Count ? CheckState.Checked : CheckState.Indeterminate;
    }

    // Ticks the relays that the existing rules block. Rules in an unknown format, for removed routes,
    // or for relays that no longer exist don't match anything and are removed by the sync that follows.
    private async Task ShowBlockedRoutesAsync()
    {
        Dictionary<string, HashSet<string>> blockedAddresses = await Task.Run(firewall.GetBlockedAddresses);
        blockedRelays.UnionWith(FirewallService.MatchBlockedRelays(pops, blockedAddresses));
        foreach (Pop pop in pops)
        {
            if (GetBlockedState(pop) == CheckState.Indeterminate) { expandedPops.Add(pop); }
        }
        RefreshGrid();
    }

    // Blocks exactly the given relays of a route. The view state only changes if the firewall accepted it.
    private void SetBlockedRelays(Pop pop, List<Relay> relays)
    {
        if (TryFirewall(() => firewall.SetBlockedRelays(pop, relays)))
        {
            blockedRelays.ExceptWith(pop.Relays);
            blockedRelays.UnionWith(relays);
        }
        RefreshGrid();
    }

    private void ToggleRelayBlocked(Pop pop, Relay relay)
    {
        List<Relay> relays = pop.Relays.Where(blockedRelays.Contains).ToList();
        if (!relays.Remove(relay)) { relays.Add(relay); }
        SetBlockedRelays(pop, relays);
    }

    private void TogglePopBlocked(Pop pop)
    {
        bool allBlocked = pop.Relays.All(blockedRelays.Contains);
        SetBlockedRelays(pop, allBlocked ? new List<Relay>() : pop.Relays.ToList());
    }

    // The header toggles between "everything blocked" and "nothing blocked".
    private void ToggleAllBlocked()
    {
        bool blockAll = !pops.All(pop => pop.Relays.All(blockedRelays.Contains));
        if (blockAll)
        {
            DialogResult confirm = MessageBox.Show("Block every route? You won't be able to connect to any Valve server until you unblock some.",
                "Steam Route Tool - Block All", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) { return; }
        }

        HashSet<Relay> newBlocked = blockAll ? pops.SelectMany(pop => pop.Relays).ToHashSet() : new HashSet<Relay>();
        if (TryFirewall(() => firewall.SyncRules(pops, newBlocked)))
        {
            blockedRelays.Clear();
            blockedRelays.UnionWith(newBlocked);
        }
        RefreshGrid();
    }

    private static Color GetPingColor(int? responseTime)
    {
        if (responseTime == null) { return Color.DarkRed; }
        if (responseTime <= 50) { return Color.Green; }
        if (responseTime <= 100) { return Color.Orange; }
        return Color.Red;
    }

    // Pings routes in two rounds (see PingService), routes on screen first. Awaited on the UI thread, so the grid is only
    // updated from the UI thread.
    // 1. Quick: the first relay of each collapsed route (every relay of an expanded one), several at a time. On networks
    //    without ICMP limits this is all it takes.
    // 2. Careful: whatever didn't answer is retried one at a time; collapsed routes try further relays until one answers.
    private async Task PingPopsAsync(IEnumerable<Pop> popsToPing)
    {
        List<Pop> ordered = OrderOnScreenFirst(popsToPing);
        foreach (Pop pop in ordered)
        {
            foreach (Relay relay in pop.Relays) { pingResults.Remove(relay); }
            pendingPings.UnionWith(pop.Relays);
            RefreshPingCells(pop);
        }

        await Task.WhenAll(ordered.SelectMany(pop => GetQuickRoundRelays(pop).Select(relay => PingRelayAsync(pop, relay, quick: true))));
        foreach (Pop pop in ordered)
        {
            await RetryCarefullyAsync(pop);
            if (IsDisposed) { return; }
            pendingPings.ExceptWith(pop.Relays);
            RefreshPingCells(pop);
        }
    }

    private IEnumerable<Relay> GetQuickRoundRelays(Pop pop)
    {
        return expandedPops.Contains(pop) ? pop.Relays : [pop.Relays[0]];
    }

    private async Task RetryCarefullyAsync(Pop pop)
    {
        if (expandedPops.Contains(pop))
        {
            foreach (Relay relay in pop.Relays.Where(relay => pingResults.GetValueOrDefault(relay) == null).ToList())
            {
                await PingRelayAsync(pop, relay, quick: false);
            }
        }
        else if (!pop.Relays.Any(relay => pingResults.GetValueOrDefault(relay) != null))
        {
            foreach (Relay relay in pop.Relays)
            {
                await PingRelayAsync(pop, relay, quick: false);
                if (pingResults.GetValueOrDefault(relay) != null) { break; }
            }
        }
    }

    private async Task PingRelayAsync(Pop pop, Relay relay, bool quick)
    {
        int? responseTime = quick ? await pingService.PingQuicklyAsync(relay.Address) : await pingService.PingCarefullyAsync(relay.Address);
        if (IsDisposed) { return; }
        pingResults[relay] = responseTime;
        RefreshPingCells(pop);
    }

    // Re-pings one relay of an expanded route, carefully.
    private async Task PingSingleRelayAsync(Pop pop, Relay relay)
    {
        pingResults.Remove(relay);
        pendingPings.Add(relay);
        RefreshPingCells(pop);
        await PingRelayAsync(pop, relay, quick: false);
        if (IsDisposed) { return; }
        pendingPings.Remove(relay);
        RefreshPingCells(pop);
    }

    private List<Pop> OrderOnScreenFirst(IEnumerable<Pop> popsToOrder)
    {
        HashSet<Pop> onScreen = new HashSet<Pop>();
        int rowIndex = routeDataGrid.FirstDisplayedScrollingRowIndex;
        int remaining = routeDataGrid.DisplayedRowCount(includePartialRow: true);
        for (; rowIndex >= 0 && rowIndex < rows.Count && remaining > 0; rowIndex++)
        {
            if (!routeDataGrid.Rows[rowIndex].Visible) { continue; }
            onScreen.Add(rows[rowIndex].Pop);
            remaining--;
        }
        return popsToOrder.OrderBy(pop => onScreen.Contains(pop) ? 0 : 1).ToList();
    }

    private async Task PingAllAsync()
    {
        btn_PingRoutes.Enabled = false;
        await PingPopsAsync(pops);
        if (IsDisposed) { return; }
        btn_PingRoutes.Enabled = true;
        SavePingCache();
    }

    private void SavePingCache()
    {
        foreach ((Relay relay, int? responseTime) in pingResults)
        {
            if (responseTime != null) { cachedPings[relay.Address] = responseTime.Value; }
        }
        pingCache.Save(cachedPings);
    }

    private void Btn_ClearRules_Click(object? sender, EventArgs e)
    {
        if (!TryFirewall(firewall.RemoveAllRules)) { return; }

        blockedRelays.Clear();
        RefreshGrid();
        MessageBox.Show("You have cleared all firewall rules created by this tool.", "Steam Route Tool - Rules Clear");
    }

    private async void Btn_PingRoutes_Click(object? sender, EventArgs e)
    {
        await PingAllAsync();
    }

    // The Blocked column is read-only: clicks are handled here so the checkboxes always reflect the firewall state.
    private void RouteDataGrid_CellContentClick(object? sender, DataGridViewCellEventArgs e)
    {
        if (e.RowIndex == -1)
        {
            if (e.ColumnIndex == colBlocked.Index && firewallReady) { ToggleAllBlocked(); }
            return;
        }

        (Pop pop, int relayIndex) = rows[e.RowIndex];
        if (e.ColumnIndex == colName.Index && pop.Relays.Count > 1)
        {
            // Expanding pings each relay; collapsing just shows the best of the results already known.
            bool expanding = expandedPops.Add(pop);
            if (!expanding) { expandedPops.Remove(pop); }
            RefreshGrid();
            if (expanding) { _ = PingPopsAsync([pop]); }
        }
        else if (e.ColumnIndex == colPing.Index)
        {
            if (expandedPops.Contains(pop)) { _ = PingSingleRelayAsync(pop, pop.Relays[relayIndex]); }
            else { _ = PingPopsAsync([pop]); }
        }
        else if (e.ColumnIndex == colBlocked.Index && firewallReady)
        {
            if (expandedPops.Contains(pop)) { ToggleRelayBlocked(pop, pop.Relays[relayIndex]); }
            else { TogglePopBlocked(pop); }
        }
    }

    private void Btn_About_Click(object? sender, EventArgs e)
    {
        MessageBox.Show("Version: " + ProductVersion + Environment.NewLine + "Steam Route Tool was created by Froody (2019)." + Environment.NewLine + "Updated by eyyMinda (2026).", "About", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }
}
