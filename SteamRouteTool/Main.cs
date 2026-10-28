using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SteamRouteTool
{
    public partial class Main : Form
    {
        public List<Route> routes = new List<Route>();
        int rowCount = 0;
        bool firstLoad = true;
        string networkconfigURL = @"https://api.steampowered.com/ISteamApps/GetSDRConfig/v1?appid=730";
        static readonly HttpClient httpClient = new HttpClient() { Timeout = TimeSpan.FromSeconds(15) };
        const int PingTimeoutMs = 2000;

        // Firewall rules are named "SteamRouteTool-<PROTOCOL>-<route>", one per protocol per blocked route.
        const string RulePrefix = "SteamRouteTool-";
        const string GamePorts = "27015-27068";
        static readonly Dictionary<string, int> RuleProtocols = new Dictionary<string, int>
        {
            { "UDP", 17 },
            { "TCP", 6 },
            { "ICMP", 1 }
        };

        // The Windows Firewall API (HNetCfg) is used late-bound, so no COM interop reference is needed to build.
        const int NET_FW_RULE_DIR_OUT = 2;
        const int NET_FW_ACTION_BLOCK = 0;

        public Main()
        {
            InitializeComponent();
            Shown += async (sender, e) => await LoadRoutesAsync();
        }

        private static dynamic GetFirewallPolicy()
        {
            return Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2"));
        }

        private static string GetRuleName(string protocol, string routeName)
        {
            return RulePrefix + protocol + "-" + routeName;
        }

        // Returns the route name of a rule in the current naming format, or null if the name doesn't match it.
        private static string ParseRouteName(string ruleName)
        {
            foreach (string protocol in RuleProtocols.Keys)
            {
                string protocolPrefix = GetRuleName(protocol, "");
                if (ruleName.StartsWith(protocolPrefix) && ruleName.Length > protocolPrefix.Length)
                {
                    return ruleName.Substring(protocolPrefix.Length);
                }
            }
            return null;
        }

        // Rules can't be removed while enumerating, so they are collected first.
        private static List<dynamic> GetToolRules(dynamic fwPolicy2)
        {
            List<dynamic> toolRules = new List<dynamic>();
            foreach (dynamic rule in fwPolicy2.Rules)
            {
                string ruleName = rule.Name;
                if (ruleName != null && ruleName.StartsWith(RulePrefix)) { toolRules.Add(rule); }
            }
            return toolRules;
        }

        private static List<string> GetRuleNames(List<dynamic> rules)
        {
            return rules.Select(rule => (string)rule.Name).ToList();
        }

        // Removing a name that doesn't exist is a no-op, so any exception here is a real failure (e.g. access denied).
        private static void RemoveRules(dynamic fwPolicy2, IEnumerable<string> ruleNames)
        {
            foreach (string ruleName in ruleNames)
            {
                fwPolicy2.Rules.Remove(ruleName);
            }
        }

        private static void RemoveRouteRules(dynamic fwPolicy2, string routeName)
        {
            RemoveRules(fwPolicy2, RuleProtocols.Keys.Select(protocol => GetRuleName(protocol, routeName)));
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
                    string raw = await httpClient.GetStringAsync(networkconfigURL);
                    ParseRoutes(raw);
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

            btn_PingRoutes.Enabled = true;
            lb_GettingRoutes.Visible = false;
            btn_About.Visible = true;
            PopulateRouteDataGrid();
        }

        // Parses into new collections first, so a malformed config doesn't leave routes half loaded.
        private void ParseRoutes(string raw)
        {
            List<Route> parsedRoutes = new List<Route>();
            int parsedRowCount = 0;
            JsonObject config = JsonNode.Parse(raw).AsObject();

            foreach (KeyValuePair<string, JsonNode> rc in config["pops"].AsObject())
            {
                JsonObject pop = rc.Value.AsObject();
                if (pop["relays"] is JsonArray relays && !pop.ToJsonString().Contains("cloud-test"))
                {
                    Route route = new Route();
                    route.name = rc.Key;
                    if (pop["desc"] != null) { route.desc = pop["desc"].GetValue<string>(); }
                    route.ranges = new Dictionary<string, string>();
                    route.row_index = new List<int>();
                    foreach (JsonNode range in relays)
                    {
                        route.ranges.Add(range["ipv4"].GetValue<string>(), range["port_range"].ToJsonString());
                        route.row_index.Add(parsedRowCount);
                        parsedRowCount++;
                    }
                    route.pw = pop["partners"] != null && pop["partners"].GetValue<int>() == 2;
                    route.extended = false;
                    route.all_check = false;
                    parsedRoutes.Add(route);
                }
            }

            routes = parsedRoutes;
            rowCount = parsedRowCount;
        }

        private void PopulateRouteDataGrid()
        {
            if (routeDataGrid.RowCount == 0)
            {
                for (int i = 0; i < rowCount; i++)
                {
                    routeDataGrid.Rows.Add();
                    routeDataGrid.Rows[i].Cells[2].Value = false;
                }
            }

            foreach (Route route in routes)
            {
                for (int i = 0; i < route.ranges.Count; i++)
                {
                    if (route.desc != null) { routeDataGrid.Rows[route.row_index[i]].Cells[0].Value = route.desc + " " + (i + 1); }
                    else { routeDataGrid.Rows[route.row_index[i]].Cells[0].Value = route.name + " " + (i + 1); }
                    if (route.extended == false)
                    {
                        if (route.desc != null) { routeDataGrid.Rows[route.row_index[0]].Cells[0].Value = route.desc; }
                        else { routeDataGrid.Rows[route.row_index[0]].Cells[0].Value = route.name; }
                    }
                    if (i > 0 && route.extended == false) { routeDataGrid.Rows[route.row_index[i]].Visible = false; }
                    else { routeDataGrid.Rows[route.row_index[i]].Visible = true; }

                }
            }

            if (firstLoad)
            {
                _ = PingRoutesAsync();
                TryFirewall(RestoreBlockedRoutes);
            }
            firstLoad = false;
        }

        // Returns the round-trip time in ms, or null if the relay didn't answer in time.
        private static async Task<long?> PingHostAsync(string host)
        {
            try
            {
                using (Ping ping = new Ping())
                {
                    PingReply reply = await ping.SendPingAsync(host, PingTimeoutMs);
                    if (reply.Status == IPStatus.Success) { return reply.RoundtripTime; }
                }
            }
            catch (PingException) { }
            return null;
        }

        private static Color GetPingColor(long? responseTime)
        {
            if (responseTime == null) { return Color.DarkRed; }
            if (responseTime <= 50) { return Color.Green; }
            if (responseTime <= 100) { return Color.Orange; }
            return Color.Red;
        }

        // Awaited on the UI thread, so the grid is only ever updated from the UI thread.
        private async Task PingRelayAsync(Route route, int relayIndex)
        {
            DataGridViewCell cell = routeDataGrid.Rows[route.row_index[relayIndex]].Cells[1];
            cell.Style.BackColor = Color.Black;
            long? responseTime = await PingHostAsync(route.ranges.Keys.ElementAt(relayIndex));
            if (IsDisposed) { return; }
            cell.Style.ForeColor = GetPingColor(responseTime);
            cell.Value = responseTime?.ToString() ?? "-1";
            cell.Style.BackColor = Color.White;
        }

        private Task PingRouteAsync(Route route)
        {
            return Task.WhenAll(Enumerable.Range(0, route.ranges.Count).Select(i => PingRelayAsync(route, i)));
        }

        private async Task PingRoutesAsync()
        {
            btn_PingRoutes.Enabled = false;
            await Task.WhenAll(routes.Select(PingRouteAsync));
            btn_PingRoutes.Enabled = true;
        }

        // Reads the blocked addresses from existing rules, removes every rule this tool owns,
        // then re-applies fresh rules for routes that still exist in the current network config.
        // Rules in an unknown format, for removed routes, or for relays that no longer exist are dropped.
        private void RestoreBlockedRoutes()
        {
            dynamic fwPolicy2 = GetFirewallPolicy();
            List<dynamic> toolRules = GetToolRules(fwPolicy2);

            Dictionary<string, HashSet<string>> blockedAddresses = new Dictionary<string, HashSet<string>>();
            foreach (dynamic rule in toolRules)
            {
                string routeName = ParseRouteName(rule.Name);
                string remoteAddresses = rule.RemoteAddresses;
                if (routeName == null || remoteAddresses == null) { continue; }

                if (!blockedAddresses.ContainsKey(routeName)) { blockedAddresses[routeName] = new HashSet<string>(); }
                foreach (string address in remoteAddresses.Split(',')) { blockedAddresses[routeName].Add(address.Split('/')[0]); }
            }

            // Show what is currently blocked first, so the list stays accurate even if re-applying the rules fails.
            List<Route> blockedRoutes = new List<Route>();
            foreach (Route route in routes)
            {
                if (!blockedAddresses.ContainsKey(route.name)) { continue; }

                HashSet<string> addresses = blockedAddresses[route.name];
                string[] relays = route.ranges.Keys.ToArray();
                bool anyBlocked = false;
                for (int i = 0; i < relays.Length; i++)
                {
                    if (addresses.Contains(relays[i]))
                    {
                        routeDataGrid.Rows[route.row_index[i]].Cells[2].Value = true;
                        anyBlocked = true;
                    }
                }
                if (!anyBlocked) { continue; }

                route.extended = !relays.All(relay => addresses.Contains(relay));
                if (route.extended)
                {
                    foreach (int index in route.row_index) { routeDataGrid.Rows[index].Visible = true; }
                }
                blockedRoutes.Add(route);
            }

            TryFirewall(() =>
            {
                RemoveRules(fwPolicy2, GetRuleNames(toolRules));
                foreach (Route route in blockedRoutes)
                {
                    SetRule(route);
                }
            });
        }

        private void Btn_ClearRules_Click(object sender, EventArgs e)
        {
            bool cleared = TryFirewall(() =>
            {
                dynamic fwPolicy2 = GetFirewallPolicy();
                RemoveRules(fwPolicy2, GetRuleNames(GetToolRules(fwPolicy2)));
            });
            if (!cleared) { return; }

            SetAllBlocked(false);
            MessageBox.Show("You have cleared all firewall rules created by this tool.", "Steam Route Tool - Rules Clear");
        }

        private async void Btn_PingRoutes_Click(object sender, EventArgs e)
        {
            await PingRoutesAsync();
        }

        void RouteDataGrid_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (routeDataGrid.CurrentCell != null && routeDataGrid.CurrentCell.ColumnIndex == 2 && routeDataGrid.IsCurrentCellDirty)
            {
                routeDataGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            }
        }

        private bool AreAllBlocked()
        {
            return routeDataGrid.Rows.Cast<DataGridViewRow>().All(row => (bool)row.Cells[2].Value);
        }

        private void SetAllBlocked(bool blocked)
        {
            for (int i = 0; i < routeDataGrid.Rows.Count; i++)
            {
                routeDataGrid.Rows[i].Cells[2].Value = blocked;
            }
        }

        private void RouteDataGrid_CellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.ColumnIndex == 0 && e.RowIndex != -1)
            {
                Route currentRoute = routes.Where(x => x.row_index.Contains(e.RowIndex)).First();
                if (currentRoute.all_check && !currentRoute.extended)
                {
                    bool blocked = false;
                    for (int i = 0; i < currentRoute.row_index.Count; i++)
                    {
                        if (i == 0)
                        {
                            blocked = Convert.ToBoolean(routeDataGrid.Rows[currentRoute.row_index[i]].Cells[2].Value);
                        }
                        else
                        {
                            routeDataGrid.Rows[currentRoute.row_index[i]].Cells[2].Value = blocked;
                        }
                    }
                }

                currentRoute.extended ^= true;
                currentRoute.all_check ^= true;
                PopulateRouteDataGrid();
                _ = PingRouteAsync(currentRoute);
            }

            if (e.ColumnIndex == 1 && e.RowIndex != -1)
            {
                Route currentRoute = routes.Where(x => x.row_index.Contains(e.RowIndex)).First();
                _ = PingRelayAsync(currentRoute, currentRoute.row_index.IndexOf(e.RowIndex));
            }

            if (e.ColumnIndex == 2 && e.RowIndex != -1)
            {
                Route currentRoute = routes.Where(x => x.row_index.Contains(e.RowIndex)).First();
                if (!currentRoute.extended) { currentRoute.all_check = true; }
                else { currentRoute.all_check = false; }
                if (!TryFirewall(() => SetRule(currentRoute)))
                {
                    DataGridViewCell cell = routeDataGrid.Rows[e.RowIndex].Cells[2];
                    cell.Value = !(bool)cell.Value;
                }
            }

            if (e.ColumnIndex == 2 && e.RowIndex == -1)
            {
                // The header toggles between "all blocked" and "none blocked", based on the rows' current state.
                bool blockAll = !AreAllBlocked();
                if (blockAll)
                {
                    DialogResult confirm = MessageBox.Show("Block every route? You won't be able to connect to any Valve server until you unblock some.",
                        "Steam Route Tool - Block All", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                    if (confirm != DialogResult.Yes) { return; }
                }

                SetAllBlocked(blockAll);
                TryFirewall(() =>
                {
                    foreach (Route route in routes)
                    {
                        SetRule(route);
                    }
                });
            }
        }

        private void SetRule(Route route)
        {
            dynamic fwPolicy2 = GetFirewallPolicy();
            RemoveRouteRules(fwPolicy2, route.name);

            List<string> blockedRelays = new List<string>();
            string[] relays = route.ranges.Keys.ToArray();
            for (int i = 0; i < relays.Length; i++)
            {
                if ((bool)routeDataGrid.Rows[route.row_index[i]].Cells[2].Value)
                {
                    // A checked collapsed route row blocks every relay at that location.
                    if (i == 0 && route.all_check)
                    {
                        blockedRelays = relays.ToList();
                        break;
                    }
                    blockedRelays.Add(relays[i]);
                }
            }
            if (blockedRelays.Count == 0) { return; }

            string remoteAddresses = string.Join(",", blockedRelays);
            foreach (KeyValuePair<string, int> protocol in RuleProtocols)
            {
                dynamic rule = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule"));
                rule.Name = GetRuleName(protocol.Key, route.name);
                rule.Enabled = true;
                rule.Direction = NET_FW_RULE_DIR_OUT;
                rule.Action = NET_FW_ACTION_BLOCK;
                // Protocol must be set before ports; ICMP has no ports.
                rule.Protocol = protocol.Value;
                if (protocol.Key != "ICMP") { rule.RemotePorts = GamePorts; }
                rule.RemoteAddresses = remoteAddresses;
                fwPolicy2.Rules.Add(rule);
            }
        }

        private void Btn_About_Click(object sender, EventArgs e)
        {
            MessageBox.Show("Version: " + ProductVersion + Environment.NewLine + "Steam Route Tool was created by Froody (2019)." + Environment.NewLine + "Updated by eyyMinda (2026).", "About", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
