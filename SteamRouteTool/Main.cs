using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text.Json.Nodes;
using System.Threading;
using System.Windows.Forms;

namespace SteamRouteTool
{
    public partial class Main : Form
    {
        public List<Route> routes = new List<Route>();
        int rowCount = 0;
        bool columnChecked = false;
        bool firstLoad = true;
        string networkconfigURL = @"https://api.steampowered.com/ISteamApps/GetSDRConfig/v1?appid=730";
        static readonly HttpClient httpClient = new HttpClient();

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
            Thread populateRoutesThread = new Thread(new ThreadStart(PopulateRoutes));
            populateRoutesThread.Start();
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

        private static void RemoveRules(dynamic fwPolicy2, IEnumerable<string> ruleNames)
        {
            foreach (string ruleName in ruleNames)
            {
                try { fwPolicy2.Rules.Remove(ruleName); }
                catch { }
            }
        }

        private static void RemoveRouteRules(dynamic fwPolicy2, string routeName)
        {
            RemoveRules(fwPolicy2, RuleProtocols.Keys.Select(protocol => GetRuleName(protocol, routeName)));
        }

        private void PopulateRoutes()
        {
            string raw = httpClient.GetStringAsync(networkconfigURL).GetAwaiter().GetResult();
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
                        route.row_index.Add(rowCount);
                        rowCount++;
                    }
                    route.pw = pop["partners"] != null && pop["partners"].GetValue<int>() == 2;
                    route.extended = false;
                    route.all_check = false;
                    routes.Add(route);
                }
            }

            btn_PingRoutes.BeginInvoke(new MethodInvoker(() =>
            {
                btn_PingRoutes.Enabled = true;
            }));
            lb_GettingRoutes.BeginInvoke(new MethodInvoker(() =>
            {
                lb_GettingRoutes.Visible = false;
            }));
            btn_About.BeginInvoke(new MethodInvoker(() =>
            {
                btn_About.Visible = true;
            }));

            if (InvokeRequired) { Invoke((Action)PopulateRouteDataGrid); }
            else { PopulateRouteDataGrid(); }
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
                PingRoutes();
                RestoreBlockedRoutes();
            }
            firstLoad = false;
        }

        private void PingSingleRoute(Route route)
        {
            Thread thread = new Thread(() =>
            {
                for (int i = 0; i < route.ranges.Count; i++)
                {
                    routeDataGrid.Rows[route.row_index[i]].Cells[1].Style.BackColor = Color.Black;
                    string responseTime = PingHost(route.ranges.Keys.ToArray()[i]);
                    if (responseTime != "-1")
                    {
                        if (Convert.ToInt32(responseTime) <= 50) { routeDataGrid.Rows[route.row_index[i]].Cells[1].Style.ForeColor = Color.Green; }
                        if (Convert.ToInt32(responseTime) > 50 && Convert.ToInt32(responseTime) <= 100) { routeDataGrid.Rows[route.row_index[i]].Cells[1].Style.ForeColor = Color.Orange; }
                        if (Convert.ToInt32(responseTime) > 100) { routeDataGrid.Rows[route.row_index[i]].Cells[1].Style.ForeColor = Color.Red; }
                    }
                    else
                    {
                        routeDataGrid.Rows[route.row_index[i]].Cells[1].Style.ForeColor = Color.DarkRed;
                    }
                    routeDataGrid.Rows[route.row_index[i]].Cells[1].Value = responseTime;
                    routeDataGrid.Rows[route.row_index[i]].Cells[1].Style.BackColor = Color.White;
                }
            });

            thread.Start();
        }

        private void PingRoutes()
        {
            foreach (Route route in routes)
            {
                Thread thread = new Thread(() =>
                {
                    for (int i = 0; i < route.ranges.Count; i++)
                    {
                        routeDataGrid.Rows[route.row_index[i]].Cells[1].Style.BackColor = Color.Black;
                        string responseTime = PingHost(route.ranges.Keys.ToArray()[i]);
                        if (responseTime != "-1")
                        {
                            if (Convert.ToInt32(responseTime) <= 50) { routeDataGrid.Rows[route.row_index[i]].Cells[1].Style.ForeColor = Color.Green; }
                            if (Convert.ToInt32(responseTime) > 50 && Convert.ToInt32(responseTime) <= 100) { routeDataGrid.Rows[route.row_index[i]].Cells[1].Style.ForeColor = Color.Orange; }
                            if (Convert.ToInt32(responseTime) > 100) { routeDataGrid.Rows[route.row_index[i]].Cells[1].Style.ForeColor = Color.Red; }
                        }
                        else
                        {
                            routeDataGrid.Rows[route.row_index[i]].Cells[1].Style.ForeColor = Color.DarkRed;
                        }
                        routeDataGrid.Rows[route.row_index[i]].Cells[1].Value = responseTime;
                        routeDataGrid.Rows[route.row_index[i]].Cells[1].Style.BackColor = Color.White;
                    }
                });

                thread.Start();
            }
        }

        public static string PingHost(string host)
        {
            try
            {
                Ping ping = new Ping();
                PingReply pingreply = ping.Send(host);
                if (pingreply.RoundtripTime == 0) { return "-1"; }
                else { return pingreply.RoundtripTime.ToString(); }
            }
            catch { return "-1"; }
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

            RemoveRules(fwPolicy2, GetRuleNames(toolRules));

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
                SetRule(route);
            }
        }

        private void Btn_ClearRules_Click(object sender, EventArgs e)
        {
            dynamic fwPolicy2 = GetFirewallPolicy();
            RemoveRules(fwPolicy2, GetRuleNames(GetToolRules(fwPolicy2)));
            for (int i = 0; i < routeDataGrid.Rows.Count; i++) { routeDataGrid.Rows[i].Cells[2].Value = false; }
            columnChecked = false;
            MessageBox.Show("You have cleared all firewall rules created by this tool.", "Steam Route Tool - Rules Clear");
        }

        private void Btn_PingRoutes_Click(object sender, EventArgs e)
        {
            PingRoutes();
        }

        void RouteDataGrid_CurrentCellDirtyStateChanged(object sender, EventArgs e)
        {
            if (routeDataGrid.CurrentCell.ColumnIndex == 2 && routeDataGrid.IsCurrentCellDirty)
            {
                routeDataGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
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
                PingSingleRoute(currentRoute);
            }

            if (e.ColumnIndex == 1 && e.RowIndex != -1)
            {
                Thread thread = new Thread(() =>
                {
                    Route currentRoute = routes.Where(x => x.row_index.Contains(e.RowIndex)).First();
                    for (int i = 0; i < currentRoute.row_index.Count; i++)
                    {
                        if (currentRoute.row_index[i] == e.RowIndex)
                        {
                            routeDataGrid.Rows[e.RowIndex].Cells[1].Style.BackColor = Color.Black;
                            string responseTime = PingHost(currentRoute.ranges.Keys.ToArray()[i]);

                            if (responseTime != "-1")
                            {
                                if (Convert.ToInt32(responseTime) <= 50)
                                {
                                    routeDataGrid.Rows[e.RowIndex].Cells[1].Style.ForeColor = Color.Green;
                                }
                                if (Convert.ToInt32(responseTime) > 50 && Convert.ToInt32(responseTime) <= 100)
                                {
                                    routeDataGrid.Rows[e.RowIndex].Cells[1].Style.ForeColor = Color.Orange;
                                }
                                if (Convert.ToInt32(responseTime) > 100)
                                {
                                    routeDataGrid.Rows[e.RowIndex].Cells[1].Style.ForeColor = Color.Red;
                                }
                            }
                            else
                            {
                                routeDataGrid.Rows[e.RowIndex].Cells[1].Style.ForeColor = Color.DarkRed;
                            }


                            routeDataGrid.Rows[e.RowIndex].Cells[1].Value = responseTime;
                            routeDataGrid.Rows[e.RowIndex].Cells[1].Style.BackColor = Color.White;
                        }
                    }
                });
                thread.Start();
            }

            if (e.ColumnIndex == 2 && e.RowIndex != -1)
            {
                Route currentRoute = routes.Where(x => x.row_index.Contains(e.RowIndex)).First();
                if (!currentRoute.extended) { currentRoute.all_check = true; }
                else { currentRoute.all_check = false; }
                SetRule(currentRoute);
            }

            if (e.ColumnIndex == 2 && e.RowIndex == -1)
           { 
                if (!columnChecked)
                {
                    for (int i = 0; i < routeDataGrid.Rows.Count; i++)
                    {
                        routeDataGrid.Rows[i].Cells[2].Value = true;
                    }
                    foreach (Route route in routes)
                    {
                        SetRule(route);
                    }
                    columnChecked = true;
                }
                else if (columnChecked)
                {
                    for (int i = 0; i < routeDataGrid.Rows.Count; i++)
                    {
                        routeDataGrid.Rows[i].Cells[2].Value = false;
                    }
                    foreach (Route route in routes)
                    {
                        SetRule(route);
                    }
                    columnChecked = false;
                }
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
