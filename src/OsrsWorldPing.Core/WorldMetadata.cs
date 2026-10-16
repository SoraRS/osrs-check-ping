namespace OsrsWorldPing.Core;

// Catalogue flag definitions: https://github.com/runelite/runelite/blob/master/runelite-api/src/main/java/net/runelite/api/WorldType.java
[Flags]
public enum WorldFlags : uint
{
    Members = 1, Pvp = 1 << 2, Bounty = 1 << 5, Arena = 1 << 6, SkillTotal = 1 << 7,
    Speedrunning = 1 << 8, HighRisk = 1 << 10, LastManStanding = 1 << 14,
    Beta = 1 << 16, NoSave = 1 << 25, Tournament = 1 << 26, Deadman = 1 << 29, Seasonal = 1 << 30
}

public enum ActivityIcon { World, Swords, Shield, Trophy, Trade, Skill, Quest, Experiment }

public sealed record WorldMetadata(string CountryCode, string Region, string Activity,
    ActivityIcon Icon, string? DangerReason)
{
    public bool IsDangerous => DangerReason is not null;

    public static WorldMetadata From(WorldInfo? world)
    {
        if (world is null) return new("", "Unavailable", "Missing from the world catalogue", ActivityIcon.World, null);
        var flags = (WorldFlags)world.Flags;
        bool Has(WorldFlags value) => (flags & value) != 0;
        string activity = string.IsNullOrWhiteSpace(world.Activity) || world.Activity == "-" ? "Standard world" : world.Activity;
        bool Mentions(string value) => activity.Contains(value, StringComparison.OrdinalIgnoreCase);
        const WorldFlags separate = WorldFlags.Deadman | WorldFlags.Seasonal | WorldFlags.Speedrunning |
            WorldFlags.Beta | WorldFlags.NoSave | WorldFlags.Tournament | WorldFlags.Arena | WorldFlags.LastManStanding;
        bool safeProfile = Has(separate) || Mentions("Deadman") || Mentions("League") || Mentions("Speedrunning") ||
            Mentions("Beta") || Mentions("Tournament") || Mentions("PvP Arena") || Mentions("Last Man Standing");
        string? danger = safeProfile ? null : Has(WorldFlags.HighRisk) || Mentions("High Risk") || Mentions("High-Risk")
            ? "High Risk: extra risk to your main-game character and items. Check the in-game world warning before joining."
            : Has(WorldFlags.Pvp) ? "PvP world: other players can attack your main-game character outside normal Wilderness areas."
            : Has(WorldFlags.Bounty) || Mentions("Bounty Hunter")
                ? "Bounty Hunter: altered PvP rules can put your main-game character and items at risk." : null;
        var icon = danger is not null || Has(WorldFlags.Pvp | WorldFlags.Deadman) ? ActivityIcon.Swords
            : Has(WorldFlags.Arena | WorldFlags.LastManStanding) ? ActivityIcon.Shield
            : Has(WorldFlags.Beta | WorldFlags.NoSave) ? ActivityIcon.Experiment
            : Has(WorldFlags.Speedrunning) || Mentions("Quest") ? ActivityIcon.Quest
            : Has(WorldFlags.Seasonal | WorldFlags.Tournament) ? ActivityIcon.Trophy
            : Mentions("Trad") ? ActivityIcon.Trade
            : Has(WorldFlags.SkillTotal) || Mentions("Skill") ? ActivityIcon.Skill : ActivityIcon.World;
        var (country, region) = RegionFor(world.Location);
        return new(country, region, activity, icon, danger);
    }

    // Location registry: https://github.com/runelite/api.runelite.net/blob/master/http-api/src/main/java/net/runelite/http/api/worlds/WorldRegion.java
    public static (string CountryCode, string Region) RegionFor(int location) => location switch
    {
        0 => ("US", "United States"), 1 => ("GB", "United Kingdom"),
        3 => ("AU", "Australia"), 7 => ("DE", "Germany"),
        8 => ("BR", "Brazil"), 9 => ("JP", "Japan"),
        10 => ("SG", "Singapore"), 12 => ("ZA", "South Africa"),
        _ => ("", $"Unknown region ({location})")
    };
}
