using AniT.Core;
using AniT.Infrastructure;
namespace AniT.Tests;
public sealed class SubtitleAppearanceTests
{
    [Fact]public void InvalidStylesAreNormalizedAndAssOriginalIsTheDefault(){var style=new SubtitleAppearanceSettings(double.NaN,-8,99,"invalid","",true).Normalize();Assert.Equal(48,style.FontSize);Assert.Equal(0,style.Position);Assert.Equal(6,style.Outline);Assert.Equal("#FFFFFF",style.Color);Assert.Equal("Segoe UI",style.Font);Assert.False(new SubtitleAppearanceSettings().OverrideAss);}
    [Fact]public void SubtitleStyleRoundTripsWithoutSavingEpisodeDelay(){var root=Path.Combine(Path.GetTempPath(),"AniT.Subtitles.Tests",Guid.NewGuid().ToString("N"));var store=new OnlinePlayerPreferencesStore(Path.Combine(root,"preferences.json"));var style=new SubtitleAppearanceSettings(64,90,3,"#FFF176",OverrideAss:true);store.Save(new(SubtitleAppearance:style));Assert.Equal(style,store.Load().SubtitleAppearance);}
}
