namespace AniT.Infrastructure;

/// <summary>Tracks first use separately from profiles so an interrupted setup can resume.</summary>
public sealed class FirstRunSetupStore(string dataDirectory)
{
    private string StatePath => Path.Combine(dataDirectory, "onboarding-state.txt");

    public bool NeedsSetup
    {
        get
        {
            if (File.Exists(StatePath)) return File.ReadAllText(StatePath).Trim() != "complete";
            // Existing installations and restored profiles must keep their normal entry flow.
            return !new[] { "profiles.json", "profiles.json.bak", "profile.json", "profile.json.bak", "anit.db" }
                .Any(name => File.Exists(Path.Combine(dataDirectory, name)));
        }
    }

    public void Begin() => WriteState("pending");
    public void Complete() => WriteState("complete");

    private void WriteState(string state)
    {
        Directory.CreateDirectory(dataDirectory);
        var temporaryPath = StatePath + ".tmp";
        File.WriteAllText(temporaryPath, state);
        File.Move(temporaryPath, StatePath, overwrite: true);
    }
}
