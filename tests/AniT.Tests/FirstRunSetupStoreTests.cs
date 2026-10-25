using AniT.Infrastructure;

namespace AniT.Tests;

public sealed class FirstRunSetupStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "anit-onboarding-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void NewInstallationNeedsSetupUntilCompletedAcrossRestarts()
    {
        var store = new FirstRunSetupStore(directory);
        Assert.True(store.NeedsSetup);
        store.Begin();
        File.WriteAllText(Path.Combine(directory, "profiles.json"), "{}");
        File.WriteAllText(Path.Combine(directory, "anit.db"), "partial setup");
        Assert.True(new FirstRunSetupStore(directory).NeedsSetup);
        store.Complete();
        Assert.False(new FirstRunSetupStore(directory).NeedsSetup);
    }

    [Theory]
    [InlineData("profiles.json")]
    [InlineData("profiles.json.bak")]
    [InlineData("profile.json")]
    [InlineData("profile.json.bak")]
    [InlineData("anit.db")]
    public void ExistingUserDataDoesNotTriggerSetup(string fileName)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), "existing data");
        Assert.False(new FirstRunSetupStore(directory).NeedsSetup);
    }

    [Fact]
    public void EmptyOrUnknownStateResumesSetup()
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "onboarding-state.txt"), "");
        Assert.True(new FirstRunSetupStore(directory).NeedsSetup);
    }

    public void Dispose()
    {
        if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
    }
}
