namespace AniT.Core;
public sealed record SubtitleAppearanceSettings(double FontSize=48,double Position=100,double Outline=2,string Color="#FFFFFF",string Font="Segoe UI",bool OverrideAss=false)
{
    public SubtitleAppearanceSettings Normalize()=>this with {
        FontSize=double.IsFinite(FontSize)?Math.Clamp(FontSize,16,96):48,
        Position=double.IsFinite(Position)?Math.Clamp(Position,0,100):100,
        Outline=double.IsFinite(Outline)?Math.Clamp(Outline,0,6):2,
        Color=Color is {Length:7}&&Color[0]=='#'&&Color.Skip(1).All(Uri.IsHexDigit)?Color.ToUpperInvariant():"#FFFFFF",
        Font=string.IsNullOrWhiteSpace(Font)?"Segoe UI":CleanFont(Font)
    };
    static string CleanFont(string value){var clean=new string(value.Where(c=>!char.IsControl(c)).Take(80).ToArray()).Trim();return string.IsNullOrWhiteSpace(clean)?"Segoe UI":clean;}
}
