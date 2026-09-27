using AniT.Core;

namespace AniT.Tests;

public sealed class ProfilePinSecurityTests
{
    [Theory]
    [InlineData("1234")]
    [InlineData("12345")]
    [InlineData("123456")]
    public void CreateAndVerify_AcceptsSupportedPins(string pin)
    {
        var credential = ProfilePinSecurity.Create(pin);

        Assert.NotEqual(pin, credential.Hash);
        Assert.NotEqual(pin, credential.Salt);
        Assert.True(ProfilePinSecurity.Verify(pin, credential.Hash, credential.Salt, credential.Iterations));
        Assert.False(ProfilePinSecurity.Verify("9999", credential.Hash, credential.Salt, credential.Iterations));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("1234567")]
    [InlineData("12a4")]
    public void IsValidFormat_RejectsInvalidPins(string? pin) =>
        Assert.False(ProfilePinSecurity.IsValidFormat(pin));

    [Fact]
    public void Verify_RejectsCorruptedCredentials()
    {
        Assert.False(ProfilePinSecurity.Verify("1234", "não-é-base64", "também-não", ProfilePinSecurity.DefaultIterations));
        Assert.False(ProfilePinSecurity.Verify("1234", null, null, 0));
    }

    [Fact]
    public void Create_UsesUniqueSaltForSamePin()
    {
        var first = ProfilePinSecurity.Create("1234");
        var second = ProfilePinSecurity.Create("1234");

        Assert.NotEqual(first.Salt, second.Salt);
        Assert.NotEqual(first.Hash, second.Hash);
    }
}
