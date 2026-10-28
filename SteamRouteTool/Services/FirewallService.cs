using SteamRouteTool.Models;

namespace SteamRouteTool.Services;

/// <summary>
/// Manages the outbound Windows Firewall rules that block relays. Each blocked route gets one rule per protocol,
/// named "SteamRouteTool-&lt;PROTOCOL&gt;-&lt;route code&gt;". Requires administrator rights to make changes;
/// failures surface as COMException or UnauthorizedAccessException. Methods may be called from a background thread.
/// </summary>
public sealed class FirewallService
{
    const string RulePrefix = "SteamRouteTool-";
    const string RuleGroup = "SteamRouteTool";
    const string AnyPort = "*";

    static readonly (string Name, int Number, bool HasPorts)[] Protocols =
    {
        ("UDP", 17, true),
        ("TCP", 6, true),
        ("ICMP", 1, false)
    };

    // The Windows Firewall API (HNetCfg) is used late-bound, so no COM interop reference is needed to build.
    const int NET_FW_RULE_DIR_OUT = 2;
    const int NET_FW_ACTION_BLOCK = 0;

    /// <summary>The properties compared to decide whether an existing rule is already correct.</summary>
    internal sealed record RuleSpec(string Name, int Protocol, string RemotePorts, string RemoteAddresses, string? Grouping, bool Enabled, int Direction, int Action);

    /// <summary>Returns the relay addresses blocked by this tool's rules, keyed by route code. Rules in an unknown name format are ignored.</summary>
    public Dictionary<string, HashSet<string>> GetBlockedAddresses()
    {
        List<dynamic> toolRules = GetToolRules(GetPolicy());
        return ParseBlockedAddresses(toolRules.Select(rule => ((string)rule.Name, (string?)rule.RemoteAddresses)));
    }

    /// <summary>Removes every rule this tool owns, including ones in outdated formats.</summary>
    public void RemoveAllRules()
    {
        dynamic policy = GetPolicy();
        List<dynamic> toolRules = GetToolRules(policy);
        RemoveRules(policy, toolRules.Select(rule => (string)rule.Name).ToList());
    }

    /// <summary>
    /// Makes this tool's rules block exactly the relays of <paramref name="pops"/> that are in <paramref name="blockedRelays"/>.
    /// Rules that are already correct are left alone; outdated, duplicate or stale rules (routes or relays no longer in
    /// <paramref name="pops"/>) are removed.
    /// </summary>
    public void SyncRules(IEnumerable<Pop> pops, IReadOnlySet<Relay> blockedRelays)
    {
        dynamic policy = GetPolicy();
        List<dynamic> toolRules = GetToolRules(policy);
        ApplyChanges(policy, toolRules.Select(ReadRule).ToList(), GetDesiredRules(pops, blockedRelays));
    }

    /// <summary>Makes this tool's rules for one route block exactly the given relays. An empty collection unblocks the route.</summary>
    public void SetBlockedRelays(Pop pop, IReadOnlyCollection<Relay> relays)
    {
        dynamic policy = GetPolicy();
        List<dynamic> toolRules = GetToolRules(policy);
        List<RuleSpec> existing = toolRules.Select(ReadRule).Where(rule => ParseRouteCode(rule.Name) == pop.Code).ToList();
        ApplyChanges(policy, existing, GetDesiredRules([pop], relays.ToHashSet()));
    }

    /// <summary>Returns the relays in <paramref name="pops"/> whose address is blocked for their route.</summary>
    public static HashSet<Relay> MatchBlockedRelays(IEnumerable<Pop> pops, IReadOnlyDictionary<string, HashSet<string>> blockedAddresses)
    {
        HashSet<Relay> blockedRelays = new HashSet<Relay>();
        foreach (Pop pop in pops)
        {
            if (!blockedAddresses.TryGetValue(pop.Code, out HashSet<string>? addresses)) { continue; }
            blockedRelays.UnionWith(pop.Relays.Where(relay => addresses.Contains(relay.Address)));
        }
        return blockedRelays;
    }

    /// <summary>Groups the remote addresses of rules in the current name format by route code. Other rules are ignored.</summary>
    internal static Dictionary<string, HashSet<string>> ParseBlockedAddresses(IEnumerable<(string Name, string? RemoteAddresses)> rules)
    {
        Dictionary<string, HashSet<string>> blockedAddresses = new Dictionary<string, HashSet<string>>();
        foreach ((string name, string? remoteAddresses) in rules)
        {
            string? routeCode = ParseRouteCode(name);
            if (routeCode == null || remoteAddresses == null) { continue; }

            if (!blockedAddresses.ContainsKey(routeCode)) { blockedAddresses[routeCode] = new HashSet<string>(); }
            blockedAddresses[routeCode].UnionWith(SplitAddresses(remoteAddresses));
        }
        return blockedAddresses;
    }

    /// <summary>The rules that should exist for the given blocked relays: one per protocol per route with at least one blocked relay.</summary>
    internal static List<RuleSpec> GetDesiredRules(IEnumerable<Pop> pops, IReadOnlySet<Relay> blockedRelays)
    {
        List<RuleSpec> rules = new List<RuleSpec>();
        foreach (Pop pop in pops)
        {
            List<Relay> relays = pop.Relays.Where(blockedRelays.Contains).ToList();
            if (relays.Count == 0) { continue; }

            string remoteAddresses = NormalizeAddresses(string.Join(",", relays.Select(relay => relay.Address)));
            string remotePorts = GetRemotePorts(relays);
            foreach ((string name, int number, bool hasPorts) in Protocols)
            {
                rules.Add(new RuleSpec(GetRuleName(name, pop.Code), number, hasPorts ? remotePorts : AnyPort, remoteAddresses,
                    RuleGroup, true, NET_FW_RULE_DIR_OUT, NET_FW_ACTION_BLOCK));
            }
        }
        return rules;
    }

    /// <summary>
    /// Decides which existing rules to remove and which desired rules to add. A rule is kept only if it's the only rule with
    /// its name and matches exactly, so an unchanged launch writes nothing; duplicates are all removed and re-added.
    /// </summary>
    internal static (List<string> ToRemove, List<RuleSpec> ToAdd) PlanChanges(IReadOnlyCollection<RuleSpec> existing, IReadOnlyCollection<RuleSpec> desired)
    {
        Dictionary<string, RuleSpec> desiredByName = desired.ToDictionary(rule => rule.Name);
        HashSet<string> kept = new HashSet<string>();
        List<string> toRemove = new List<string>();
        foreach (IGrouping<string, RuleSpec> sameName in existing.GroupBy(rule => rule.Name))
        {
            if (sameName.Count() == 1 && desiredByName.TryGetValue(sameName.Key, out RuleSpec? wanted) && wanted == sameName.First())
            {
                kept.Add(sameName.Key);
                continue;
            }
            // Remove() deletes one rule per call, so duplicates need one call each.
            toRemove.AddRange(Enumerable.Repeat(sameName.Key, sameName.Count()));
        }
        return (toRemove, desired.Where(rule => !kept.Contains(rule.Name)).ToList());
    }

    // One rule covers all blocked relays of a route, so it spans their combined port range.
    internal static string GetRemotePorts(IReadOnlyCollection<Relay> relays)
    {
        return relays.Min(relay => relay.MinPort) + "-" + relays.Max(relay => relay.MaxPort);
    }

    internal static string GetRuleName(string protocol, string routeCode)
    {
        return RulePrefix + protocol + "-" + routeCode;
    }

    // Returns the route code of a rule in the current name format, or null if the name doesn't match it.
    internal static string? ParseRouteCode(string ruleName)
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

    // Windows reports addresses with a subnet mask, e.g. "1.2.3.4/255.255.255.255", in its own order.
    internal static string NormalizeAddresses(string remoteAddresses)
    {
        return string.Join(",", SplitAddresses(remoteAddresses).Order(StringComparer.Ordinal));
    }

    static IEnumerable<string> SplitAddresses(string remoteAddresses)
    {
        return remoteAddresses.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(address => address.Split('/')[0].Trim());
    }

    static RuleSpec ReadRule(dynamic rule)
    {
        string? remotePorts = rule.RemotePorts;
        string? remoteAddresses = rule.RemoteAddresses;
        return new RuleSpec((string)rule.Name, (int)rule.Protocol, string.IsNullOrEmpty(remotePorts) ? AnyPort : remotePorts,
            NormalizeAddresses(remoteAddresses ?? ""), (string?)rule.Grouping, (bool)rule.Enabled, (int)rule.Direction, (int)rule.Action);
    }

    static void ApplyChanges(dynamic policy, IReadOnlyCollection<RuleSpec> existing, IReadOnlyCollection<RuleSpec> desired)
    {
        (List<string> toRemove, List<RuleSpec> toAdd) = PlanChanges(existing, desired);
        RemoveRules(policy, toRemove);
        foreach (RuleSpec spec in toAdd)
        {
            dynamic rule = Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FWRule")!)!;
            rule.Name = spec.Name;
            rule.Grouping = spec.Grouping;
            rule.Enabled = spec.Enabled;
            rule.Direction = spec.Direction;
            rule.Action = spec.Action;
            // Protocol must be set before ports.
            rule.Protocol = spec.Protocol;
            if (spec.RemotePorts != AnyPort) { rule.RemotePorts = spec.RemotePorts; }
            rule.RemoteAddresses = spec.RemoteAddresses;
            policy.Rules.Add(rule);
        }
    }

    static dynamic GetPolicy()
    {
        return Activator.CreateInstance(Type.GetTypeFromProgID("HNetCfg.FwPolicy2")!)!;
    }

    // Rules can't be removed while enumerating, so they are collected first.
    static List<dynamic> GetToolRules(dynamic policy)
    {
        List<dynamic> toolRules = new List<dynamic>();
        foreach (dynamic rule in policy.Rules)
        {
            string? ruleName = rule.Name;
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
