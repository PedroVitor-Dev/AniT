using AniT.Core;

namespace AniT.Tests;

public sealed class AnimeOrganizationLabelsTests
{
    [Fact]
    public void SerializeAndParse_NormalizeDuplicatesAndPreserveLabels()
    {
        var serialized = AnimeOrganizationLabels.Serialize([" Clássicos ", "Rever", "clássicos"]);

        Assert.Equal(["Clássicos", "Rever"], AnimeOrganizationLabels.Parse(serialized));
        Assert.True(AnimeOrganizationLabels.Contains(serialized, "CLÁSSICOS"));
    }

    [Fact]
    public void Parse_AcceptsLegacyNewlineSeparatedValues()
    {
        Assert.Equal(["Fim de semana", "Top pessoal"], AnimeOrganizationLabels.Parse("Top pessoal\r\nFim de semana"));
    }
}
