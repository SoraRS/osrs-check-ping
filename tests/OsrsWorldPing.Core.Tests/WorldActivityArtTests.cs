using OsrsWorldPing.Core;

namespace OsrsWorldPing.Core.Tests;

public sealed class WorldActivityArtTests
{
    [Theory]
    [InlineData("2200 skill total", "overall")]
    [InlineData("2350 SKILL TOTAL", "overall")]
    [InlineData("Wintertodt", "wintertodt")]
    [InlineData("Tempoross", "tempoross")]
    [InlineData("Zalcano", "zalcano")]
    [InlineData("Nex FFA", "nex")]
    [InlineData("Nightmare of Ashihama", "nightmare")]
    [InlineData("Guardians of the Rift", "runecraft")]
    [InlineData("Zeah Runecrafting", "runecraft")]
    [InlineData("Ourania Altar", "runecraft")]
    [InlineData("Forestry", "woodcutting")]
    [InlineData("Sulliuscep cutting", "woodcutting")]
    [InlineData("Agility Training", "agility")]
    [InlineData("Brimhaven Agility Arena", "agility")]
    [InlineData("Blast Furnace", "smithing")]
    [InlineData("Volcanic Mine", "mining")]
    [InlineData("Fishing Trawler", "fishing")]
    [InlineData("Mastering Mixology", "herblore")]
    [InlineData("Pyramid Plunder", "thieving")]
    [InlineData("Sorceress's Garden", "thieving")]
    [InlineData("House Party, Gilded Altar", "prayer")]
    [InlineData("Salvaging", "sailing")]
    [InlineData("Tombs of Amascut", "combat")]
    [InlineData("Theatre of Blood", "combat")]
    [InlineData("Royal Titans", "combat")]
    [InlineData("Yama", "combat")]
    [InlineData("PvP World - Free", "pvp")]
    [InlineData("Bounty Hunter World", "pvp")]
    [InlineData("Deadman - Permanent", "deadman")]
    [InlineData("Leagues", "league")]
    [InlineData("Speedrunning World", "speedrunning")]
    [InlineData("Clan Recruitment", "grouping")]
    [InlineData("Castle Wars - Free", "grouping")]
    [InlineData("PvP Arena (Legacy Duels)", "grouping")]
    [InlineData("LMS Competitive", "grouping")]
    [InlineData("Fresh Start", "quest")]
    [InlineData("An unfamiliar activity", null)]
    [InlineData("Next event", null)]
    [InlineData("-", null)]
    public void ActivitySelectsARealBundledSpriteOrTheNeutralFallback(string activity, string? sprite) =>
        Assert.Equal(sprite, WorldActivityArt.SpriteFor(new(301, 1, "catalogue.example", activity, 0, 10)));

    [Fact]
    public void SkillTotalFlagUsesTheExperienceGraphEvenWithoutAnActivityLabel()
    {
        Assert.Equal("overall", WorldActivityArt.SpriteFor(new(301, (uint)WorldFlags.SkillTotal, "catalogue.example", "-", 0, 10)));
        Assert.Null(WorldActivityArt.SpriteFor(null));
    }
}
