using System.Runtime.InteropServices;
using SteamRouteTool.Models;
using SteamRouteTool.Services;

namespace SteamRouteTool;

public partial class Main : Form
{
    static readonly HttpClient httpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(15) };
    readonly SdrConfigClient configClient = new SdrConfigClient(httpClient);
    readonly FirewallService firewall = new FirewallService();
    readonly PingService pingService = new PingService(TimeSpan.FromSeconds(1));

    IReadOnlyList<Pop> pops = Array.Empty<Pop>();
    // View state: which relays are blocked (mirrors the firewall rules) and which routes show each relay as its own row.
    readonly HashSet<Relay> blockedRelays = new HashSet<Relay>();
    readonly HashSet<Pop> expandedPops = new HashSet<Pop>();
    // The route and relay shown in each grid row. Every relay has a row; collapsed routes only show their first one.
    readonly List<(Pop Pop, int RelayIndex)> rows = new List<(Pop Pop, int RelayIndex)>();
    // Last ping result per relay (null = no reply) and relays with a ping queued or in progress.
    readonly Dictionary<Relay, int?> pingResults = new Dictionary<Relay, int?>();
    readonly HashSet<Relay> pendingPings = new HashSet<Relay>();
    // Firewall changes from clicks wait until the rules have been synced on launch.
    bool firewallReady;

    public Main()
    {
        InitializeComponent();
        btn_ClearRules.Enabled = false;
        Shown += async (sender, e) => await LoadRoutesAsync();
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

        int? best = relays.Where(pingResults.ContainsKey).Select(relay => pingResults[relay]).Min();
        if (best != null) { (cell.Value, cell.Style.ForeColor) = (best.ToString(), GetPingColor(best)); }
        else if (relays.Any(pendingPings.Contains)) { (cell.Value, cell.Style.ForeColor) = ("…", Color.Gray); }
        else if (relays.Any(pingResults.ContainsKey)) { (cell.Value, cell.Style.ForeColor) = ("-1", GetPingColor(null)); }
        else { cell.Value = ""; }
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

    // Pings the given relays of a route in order. Awaited on the UI thread, so the grid is only updated from the UI thread.
    // With stopAtFirstReply, relays are only tried until one answers: enough for a collapsed route, which shows its best ping.
    private async Task PingRelaysAsync(Pop pop, IReadOnlyList<Relay> relays, bool stopAtFirstReply)
    {
        foreach (Relay relay in relays)
        {
            pingResults.Remove(relay);
            pendingPings.Add(relay);
        }
        RefreshPingCells(pop);

        foreach (Relay relay in relays)
        {
            int? responseTime = await pingService.PingAsync(relay.Address);
            if (IsDisposed) { return; }
            pendingPings.Remove(relay);
            pingResults[relay] = responseTime;
            RefreshPingCells(pop);
            if (stopAtFirstReply && responseTime != null) { break; }
        }

        pendingPings.ExceptWith(relays);
        RefreshPingCells(pop);
    }

    // An expanded route pings every relay; a collapsed one stops at the first relay that answers.
    private Task PingPopAsync(Pop pop)
    {
        return PingRelaysAsync(pop, pop.Relays, stopAtFirstReply: !expandedPops.Contains(pop));
    }

    // Pings go out one at a time (see PingService), so routes are pinged top to bottom.
    private async Task PingAllAsync()
    {
        btn_PingRoutes.Enabled = false;
        await Task.WhenAll(pops.Select(PingPopAsync));
        if (IsDisposed) { return; }
        btn_PingRoutes.Enabled = true;
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
            if (expanding) { _ = PingPopAsync(pop); }
        }
        else if (e.ColumnIndex == colPing.Index)
        {
            if (expandedPops.Contains(pop)) { _ = PingRelaysAsync(pop, [pop.Relays[relayIndex]], stopAtFirstReply: false); }
            else { _ = PingPopAsync(pop); }
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
