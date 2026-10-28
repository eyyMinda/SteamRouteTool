using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using SteamRouteTool.Models;
using SteamRouteTool.Services;

namespace SteamRouteTool
{
    public partial class Main : Form
    {
        static readonly HttpClient httpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(15) };
        readonly SdrConfigClient configClient = new SdrConfigClient(httpClient);
        readonly FirewallService firewall = new FirewallService();
        readonly PingService pingService = new PingService(TimeSpan.FromSeconds(2));

        IReadOnlyList<Pop> pops = Array.Empty<Pop>();
        // View state: which relays are blocked (mirrors the firewall rules) and which routes show each relay as its own row.
        readonly HashSet<Relay> blockedRelays = new HashSet<Relay>();
        readonly HashSet<Pop> expandedPops = new HashSet<Pop>();
        // The route and relay shown in each grid row. Every relay has a row; collapsed routes only show their first one.
        readonly List<(Pop Pop, int RelayIndex)> rows = new List<(Pop Pop, int RelayIndex)>();

        public Main()
        {
            InitializeComponent();
            Shown += async (sender, e) => await LoadRoutesAsync();
        }

        // Runs a firewall change, showing an error instead of crashing if Windows Firewall rejects it.
        private static bool TryFirewall(Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception ex) when (ex is COMException || ex is UnauthorizedAccessException)
            {
                MessageBox.Show("Couldn't update the Windows Firewall rules:" + Environment.NewLine + ex.Message + Environment.NewLine + Environment.NewLine +
                    "Make sure the Windows Defender Firewall service is running and the tool is running as administrator.",
                    "Steam Route Tool - Firewall Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            _ = PingAllAsync();
            RestoreBlockedRoutes();
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

        // Updates names, visibility and checkboxes from the view state.
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
                row.Cells[Name.Index].Value = expanded ? pop.Description + " " + (relayIndex + 1) : pop.Description;
                row.Cells[Blocked.Index].Value = expanded ? ToCheckState(blockedRelays.Contains(pop.Relays[relayIndex])) : GetBlockedState(pop);
            }
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

        // Reads which relays are currently blocked, shows them, then re-applies fresh rules from the current network config.
        // Rules in an unknown format, for removed routes, or for relays that no longer exist are dropped.
        private void RestoreBlockedRoutes()
        {
            TryFirewall(() =>
            {
                Dictionary<string, HashSet<string>> blockedAddresses = firewall.GetBlockedAddresses();
                foreach (Pop pop in pops)
                {
                    if (!blockedAddresses.TryGetValue(pop.Code, out HashSet<string> addresses)) { continue; }

                    List<Relay> relays = pop.Relays.Where(relay => addresses.Contains(relay.Address)).ToList();
                    blockedRelays.UnionWith(relays);
                    if (relays.Count > 0 && relays.Count < pop.Relays.Count) { expandedPops.Add(pop); }
                }

                // Show what is currently blocked first, so the list stays accurate even if re-applying the rules fails.
                RefreshGrid();
                firewall.ReplaceAllRules(pops, blockedRelays);
            });
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
            if (TryFirewall(() => firewall.ReplaceAllRules(pops, newBlocked)))
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

        // Awaited on the UI thread, so the grid is only ever updated from the UI thread.
        private async Task PingRowAsync(int rowIndex)
        {
            (Pop pop, int relayIndex) = rows[rowIndex];
            DataGridViewCell cell = routeDataGrid.Rows[rowIndex].Cells[Ping.Index];
            cell.Style.BackColor = Color.Black;
            int? responseTime = await pingService.PingAsync(pop.Relays[relayIndex].Address);
            if (IsDisposed) { return; }
            cell.Style.ForeColor = GetPingColor(responseTime);
            cell.Value = responseTime?.ToString() ?? "-1";
            cell.Style.BackColor = Color.White;
        }

        private Task PingPopAsync(Pop pop)
        {
            return Task.WhenAll(Enumerable.Range(0, rows.Count).Where(rowIndex => rows[rowIndex].Pop == pop).Select(PingRowAsync));
        }

        private async Task PingAllAsync()
        {
            btn_PingRoutes.Enabled = false;
            await Task.WhenAll(Enumerable.Range(0, rows.Count).Select(PingRowAsync));
            if (IsDisposed) { return; }
            btn_PingRoutes.Enabled = true;
        }

        private void Btn_ClearRules_Click(object sender, EventArgs e)
        {
            if (!TryFirewall(firewall.RemoveAllRules)) { return; }

            blockedRelays.Clear();
            RefreshGrid();
            MessageBox.Show("You have cleared all firewall rules created by this tool.", "Steam Route Tool - Rules Clear");
        }

        private async void Btn_PingRoutes_Click(object sender, EventArgs e)
        {
            await PingAllAsync();
        }

        // The Blocked column is read-only: clicks are handled here so the checkboxes always reflect the firewall state.
        private void RouteDataGrid_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex == -1)
            {
                if (e.ColumnIndex == Blocked.Index) { ToggleAllBlocked(); }
                return;
            }

            (Pop pop, int relayIndex) = rows[e.RowIndex];
            if (e.ColumnIndex == Name.Index)
            {
                if (!expandedPops.Remove(pop)) { expandedPops.Add(pop); }
                RefreshGrid();
                _ = PingPopAsync(pop);
            }
            else if (e.ColumnIndex == Ping.Index)
            {
                _ = PingRowAsync(e.RowIndex);
            }
            else if (e.ColumnIndex == Blocked.Index)
            {
                if (expandedPops.Contains(pop)) { ToggleRelayBlocked(pop, pop.Relays[relayIndex]); }
                else { TogglePopBlocked(pop); }
            }
        }

        private void Btn_About_Click(object sender, EventArgs e)
        {
            MessageBox.Show("Version: " + ProductVersion + Environment.NewLine + "Steam Route Tool was created by Froody (2019)." + Environment.NewLine + "Updated by eyyMinda (2026).", "About", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
