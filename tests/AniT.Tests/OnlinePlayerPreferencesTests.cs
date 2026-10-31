using AniT.Infrastructure;
using System.Text.Json;
namespace AniT.Tests;
public sealed class OnlinePlayerPreferencesTests
{
    [Theory]
    [InlineData("JPN","ja")][InlineData("pt-BR","pt-br")][InlineData("eng","en")][InlineData("und",null)][InlineData("bad\nvalue",null)][InlineData("off","off")]
    public void LanguagesAreNormalizedWithoutSavingTrackIds(string source,string? expected)=>Assert.Equal(expected,OnlinePlayerPreferences.NormalizeLanguage(source));
    [Fact]
    public void NonfiniteAndUnsupportedPreferencesFallBackToSafeValues()
    {
        var value=new OnlinePlayerPreferences(AudioLanguage:"off",Volume:double.NaN,Speed:50).Normalize();
        Assert.Null(value.AudioLanguage);Assert.Equal(100,value.Volume);Assert.Null(value.Speed);
        Assert.Equal(0,new OnlinePlayerPreferences(Volume:-1).Normalize().Volume);
    }
    [Fact]
    public void PreferencesRoundTripPerProfileAndRecoverTheLastBackup()
    {
        var root=Path.Combine(Path.GetTempPath(),"AniT.Player.Preferences",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var path=Path.Combine(root,"one","player-preferences.json");var first=new OnlinePlayerPreferencesStore(path);
        try {
            Assert.Equal(new OnlinePlayerPreferences(),first.Load());
            var expected=new OnlinePlayerPreferences(true,"jpn","off",42,true,1.25).Normalize();first.Save(expected);
            Assert.Equal(expected,first.Load());
            Assert.Equal(new OnlinePlayerPreferences(),new OnlinePlayerPreferencesStore(Path.Combine(root,"two","player-preferences.json")).Load());
            first.Save(expected with{Volume=55});File.WriteAllText(path,"broken");Assert.Equal(expected,first.Load());
            first.Save(expected with{Volume=64});File.WriteAllText(path,"broken again");Assert.Equal(expected,first.Load());
            Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(path)!,"*.tmp"));
            Assert.False(JsonDocument.Parse(File.ReadAllText(path+".bak")).RootElement.TryGetProperty("TrackId",out _));
        } finally {if(Path.GetFullPath(root).StartsWith(Path.Combine(Path.GetTempPath(),"AniT.Player.Preferences")+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))Directory.Delete(root,true);}
    }
    [Fact]
    public void OlderEmptyPreferencesKeepRememberEnabled()=>Assert.True(JsonSerializer.Deserialize<OnlinePlayerPreferences>("{}")!.RememberLanguages);
}
