using AniT.Core;

namespace AniT.Tests;

public sealed class AnimeGenreCatalogTests
{
    [Theory]
    [InlineData("Ecchi", "Ecchi", "ecchi")]
    [InlineData("Horror", "Horror", "terror")]
    [InlineData("Adventure", "Aventura", "aventura")]
    [InlineData("Sports", "Esportes", "esporte")]
    public void ExploreGenres_HaveLocalizedSearchableDefinitions(
        string canonicalName,
        string displayName,
        string searchTerm)
    {
        var genre = Assert.Single(AnimeGenreCatalog.All, item => item.CanonicalName == canonicalName);

        Assert.Equal(displayName, genre.DisplayName);
        Assert.Contains(searchTerm, genre.SearchText, StringComparison.CurrentCultureIgnoreCase);
    }
}
