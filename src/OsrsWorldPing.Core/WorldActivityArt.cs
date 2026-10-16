namespace OsrsWorldPing.Core;

// Identifiers only: the portable core never loads images or references a UI framework.
public static class WorldActivityArt
{
    public static string? SpriteFor(WorldInfo? world)
    {
        if (world is null) return null;
        string activity = world.Activity;
        bool Has(string name) => activity.Contains(name, StringComparison.OrdinalIgnoreCase);
        if (((WorldFlags)world.Flags & WorldFlags.SkillTotal) != 0 || Has("skill total")) return "overall";
        if (Has("Wintertodt")) return "wintertodt";
        if (Has("Tempoross")) return "tempoross";
        if (Has("Zalcano")) return "zalcano";
        if (activity.Equals("Nex", StringComparison.OrdinalIgnoreCase) || activity.StartsWith("Nex ", StringComparison.OrdinalIgnoreCase)) return "nex";
        if (Has("Nightmare")) return "nightmare";
        if (Has("Guardians of the Rift") || Has("Runecraft") || Has("Ourania")) return "runecraft";
        if (Has("Forestry") || Has("Sulliuscep") || Has("Woodcut")) return "woodcutting";
        if (Has("Agility")) return "agility";
        if (Has("Blast Furnace") || Has("Smith")) return "smithing";
        if (Has("Volcanic Mine") || Has("Mining")) return "mining";
        if (Has("Fishing")) return "fishing";
        if (Has("Mixology") || Has("Herblore")) return "herblore";
        if (Has("Pyramid Plunder") || Has("Sorceress") || Has("Thiev")) return "thieving";
        if (Has("Gilded Altar") || Has("Prayer")) return "prayer";
        if (Has("House Party") || Has("Construction")) return "construction";
        if (Has("Salvaging") || Has("Sailing")) return "sailing";
        if (Has("Cooking") || Has("Trouble Brewing")) return "cooking";
        if (Has("Crafting")) return "crafting";
        if (Has("Hunter")) return "hunter";
        if (Has("Farming")) return "farming";
        if (Has("Firemaking")) return "firemaking";
        if (Has("Slayer")) return "slayer";
        if (Has("Magic")) return "magic";
        if (Has("Yama") || Has("Royal Titans") || Has("Theatre of Blood") || Has("Tombs of Amascut") ||
            Has("ToA FFA") || Has("PvM") || Has("Barbarian Assault") || Has("Pest Control") || Has("Soul Wars")) return "combat";
        return null; // Unknown activities retain a generic vector icon and their full activity label.
    }
}
