using OsrsWorldPing.Core;

namespace OsrsWorldPing.Core.Tests;

public sealed class WorldMetadataTests
{
    private static WorldInfo World(WorldFlags flags = 0, string activity = "-", int location = 0) => new(301, (uint)flags, "catalogue.example", activity, location, 10);
    [Theory]
    [InlineData(WorldFlags.Pvp)]
    [InlineData(WorldFlags.HighRisk)]
    [InlineData(WorldFlags.Bounty)]
    public void MainCharacterRisksHaveAnExplanation(WorldFlags flags)
    {
        var metadata = WorldMetadata.From(World(flags));
        Assert.True(metadata.IsDangerous);
        Assert.False(string.IsNullOrEmpty(metadata.DangerReason));
        Assert.Equal(ActivityIcon.Swords, metadata.Icon);
    }
    [Theory]
    [InlineData(WorldFlags.Deadman)]
    [InlineData(WorldFlags.Seasonal)]
    [InlineData(WorldFlags.Speedrunning)]
    [InlineData(WorldFlags.Beta)]
    [InlineData(WorldFlags.NoSave)]
    [InlineData(WorldFlags.Tournament)]
    [InlineData(WorldFlags.Arena)]
    [InlineData(WorldFlags.LastManStanding)]
    public void SeparateProfilesAndSafeCombatDoNotWarnAboutMainCharacter(WorldFlags flags) =>
        Assert.False(WorldMetadata.From(World(flags | WorldFlags.Pvp | WorldFlags.HighRisk)).IsDangerous);

    [Theory]
    [InlineData("Bounty Hunter", true)]
    [InlineData("High Risk", true)]
    [InlineData("PvP Arena", false)]
    [InlineData("Leagues", false)]
    [InlineData("Unrecognized PvP event", false)]
    public void ActivityFallbacksAreSpecificAndUnknownNamesDoNotInventRisk(string activity, bool dangerous) =>
        Assert.Equal(dangerous, WorldMetadata.From(World(activity: activity)).IsDangerous);

    [Theory]
    [InlineData(0, "US")]
    [InlineData(1, "GB")]
    [InlineData(3, "AU")]
    [InlineData(7, "DE")]
    [InlineData(8, "BR")]
    [InlineData(9, "JP")]
    [InlineData(10, "SG")]
    [InlineData(12, "ZA")]
    [InlineData(250, "")]
    public void VerifiedRegionsAndUnknownFallback(int location, string country) =>
        Assert.Equal(country, WorldMetadata.From(World(location: location)).CountryCode);
    [Fact]
    public void UnknownActivitiesAndMissingFavoritesUseFallbackMetadata()
    {
        var metadata = WorldMetadata.From(World(activity: "A newly introduced activity"));
        Assert.Equal(ActivityIcon.World, metadata.Icon);
        Assert.Equal("A newly introduced activity", metadata.Activity);
        Assert.Equal("Standard world", WorldMetadata.From(World()).Activity);
        Assert.Equal("Unavailable", WorldMetadata.From(null).Region);
        Assert.True(World(WorldFlags.Members).Members);
        Assert.False(World().Members);
    }
}
