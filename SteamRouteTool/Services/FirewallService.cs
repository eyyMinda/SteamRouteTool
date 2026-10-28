using System;
using System.Collections.Generic;
using System.Linq;
using SteamRouteTool.Models;

namespace SteamRouteTool.Services
{
    /// <summary>
    /// Manages the outbound Windows Firewall rules that block relays. Each blocked route gets one rule per protocol,
    /// named "SteamRouteTool-&lt;PROTOCOL&gt;-&lt;route code&gt;". Requires administrator rights to make changes;
    /// failures surface as COMException or UnauthorizedAccessException.
    /// </summary>
    public sealed class FirewallService
    {
        const string RulePrefix = "SteamRouteTool-";
        const string RuleGroup = "SteamRouteTool";

        static readonly (string Name, int Number, bool HasPorts)[] Protocols =
        {
            ("UDP", 17, true),
            ("TCP", 6, true),
            ("ICMP", 1, false)
        };

        // The Windows Firewall API (HNetCfg) is used late-bound, so no COM interop reference is needed to build.
        const int NET_FW_RULE_DIR_OUT = 2;
        const int NET_FW_ACTION_BLOCK = 0;

        /// <summary>Returns the relay addresses blocked by this tool's rules, keyed by route code. Rules in an unknown name format are ignored.</summary>
        public Dictionary<string, HashSet<string>> GetBlockedAddresses()
        {
            Dictionary<string, HashSet<string>> blockedAddresses = new Dictionary<string, HashSet<string>>();
            foreach (dynamic rule in GetToolRules(GetPolicy()))
            {
                string routeCode = ParseRouteCode(rule.Name);
                string remoteAddresses = rule.RemoteAddresses;
                if (routeCode == null || remoteAddresses == null) { continue; }

                if (!blockedAddresses.ContainsKey(routeCode)) { blockedAddresses[routeCode] = new HashSet<string>(); }
                foreach (string address in remoteAddresses.Split(',')) { blockedAddresses[routeCode].Add(address.Split('/')[0]); }
            }
            return blockedAddresses;
        }

        /// <summary>Removes every rule this tool owns, including ones in outdated formats.</summary>
        public void RemoveAllRules()
        {
            dynamic policy = GetPolicy();
            List<dynamic> toolRules = GetToolRules(policy);
            RemoveRules(policy, toolRules.Select(rule => (string)rule.Name).ToList());
        }

        /// <summary>
        /// Removes every rule this tool owns, then blocks the relays of <paramref name="pops"/> that are in <paramref name="blockedRelays"/>.
        /// Rules for routes or relays that are no longer in <paramref name="pops"/> are dropped.
        /// </summary>
        public void ReplaceAllRules(IEnumerable<Pop> pops, IReadOnlySet<Relay> blockedRelays)
        {
            RemoveAllRules();
            foreach (Pop pop in pops)
            {
                List<Relay> relays = pop.Relays.Where(blockedRelays.Contains).ToList();
                if (relays.Count > 0) { SetBlockedRelays(pop, relays); }
            }
        }

        /// <summary>Replaces this tool's rules for one route with rules blocking the given relays. An empty collection unblocks the route.</summary>
        public void SetBlockedRelays(Pop pop, IReadOnlyCollection<Relay> relays)
        {
            dynamic policy = GetPolicy();
            RemoveRules(policy, Protocols.Select(protocol => GetRuleName(protocol.Name, pop.Code)));
            if (relays.Count == 0) { return; }

            string remoteAddresses = string.Join(",", relays.Select(relay => relay.Address));
            // One rule covers all blocked relays of the route, so it spans their combined port range.
            string remotePorts = relays.Min(relay => relay.MinPort) + "-" + relays.Max(relay => relay.MaxPort);
            foreach ((string name, int number, bool hasPorts) in Protocols)
            {
                dynamic rule = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule"));
                rule.Name = GetRuleName(name, pop.Code);
                rule.Grouping = RuleGroup;
                rule.Enabled = true;
                rule.Direction = NET_FW_RULE_DIR_OUT;
                rule.Action = NET_FW_ACTION_BLOCK;
                // Protocol must be set before ports.
                rule.Protocol = number;
                if (hasPorts) { rule.RemotePorts = remotePorts; }
                rule.RemoteAddresses = remoteAddresses;
                policy.Rules.Add(rule);
            }
        }

        static dynamic GetPolicy()
        {
            return Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2"));
        }

        static string GetRuleName(string protocol, string routeCode)
        {
            return RulePrefix + protocol + "-" + routeCode;
        }

        // Returns the route code of a rule in the current name format, or null if the name doesn't match it.
        static string ParseRouteCode(string ruleName)
        {
            foreach ((string name, _, _) in Protocols)
            {
                string protocolPrefix = GetRuleName(name, "");
                if (ruleName.StartsWith(protocolPrefix) && ruleName.Length > protocolPrefix.Length)
                {
                    return ruleName.Substring(protocolPrefix.Length);
                }
            }
            return null;
        }

        // Rules can't be removed while enumerating, so they are collected first.
        static List<dynamic> GetToolRules(dynamic policy)
        {
            List<dynamic> toolRules = new List<dynamic>();
            foreach (dynamic rule in policy.Rules)
            {
                string ruleName = rule.Name;
                if (ruleName != null && ruleName.StartsWith(RulePrefix)) { toolRules.Add(rule); }
            }
            return toolRules;
        }

        // Removing a name that doesn't exist is a no-op, so any exception here is a real failure (e.g. access denied).
        static void RemoveRules(dynamic policy, IEnumerable<string> ruleNames)
        {
            foreach (string ruleName in ruleNames)
            {
                policy.Rules.Remove(ruleName);
            }
        }
    }
}
