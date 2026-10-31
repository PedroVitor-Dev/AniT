namespace AniT.Core;
public static class MomentSpoilerProtection
{
    public static bool CanReveal(double seconds,double? current,bool enabled=true,bool explicitlyRevealed=false)
        => !enabled||explicitlyRevealed||(double.IsFinite(seconds)&&seconds>=0&&current is {} at&&double.IsFinite(at)&&at>=seconds-.25);
}
