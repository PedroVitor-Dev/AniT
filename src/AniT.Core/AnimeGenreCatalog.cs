namespace AniT.Core;

public sealed record AnimeGenreDefinition(string CanonicalName, string DisplayName, params string[] Aliases)
{
    public string SearchText => string.Join(' ', new[] { CanonicalName, DisplayName }.Concat(Aliases));
}

public static class AnimeGenreCatalog
{
    public static IReadOnlyList<AnimeGenreDefinition> All { get; } =
    [
        new("Action", "Ação", "acao"),
        new("Adventure", "Aventura"),
        new("Comedy", "Comédia", "comedia"),
        new("Drama", "Drama"),
        new("Ecchi", "Ecchi"),
        new("Fantasy", "Fantasia"),
        new("Horror", "Terror", "horror"),
        new("Isekai", "Isekai", "outro mundo"),
        new("Mahou Shoujo", "Garotas mágicas", "mahou shoujo", "magical girl"),
        new("Mecha", "Mecha"),
        new("Music", "Música", "musica"),
        new("Mystery", "Mistério", "misterio"),
        new("Psychological", "Psicológico", "psicologico"),
        new("Romance", "Romance"),
        new("Sci-Fi", "Ficção científica", "ficcao cientifica", "science fiction"),
        new("Slice of Life", "Slice of Life", "cotidiano"),
        new("Sports", "Esportes", "esporte"),
        new("Supernatural", "Sobrenatural"),
        new("Thriller", "Suspense", "thriller")
    ];

    public static IReadOnlyList<string> Parse(string? storedGenres) =>
        string.IsNullOrWhiteSpace(storedGenres)
            ? []
            : storedGenres.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

    public static string ToSearchText(string? storedGenres) => string.Join(
        ' ',
        Parse(storedGenres).SelectMany(genre =>
        {
            var definition = All.FirstOrDefault(item => string.Equals(item.CanonicalName, genre, StringComparison.OrdinalIgnoreCase));
            return definition is null ? [genre] : new[] { genre, definition.SearchText };
        }));

    public static string DisplayName(string canonicalName) =>
        All.FirstOrDefault(item => string.Equals(item.CanonicalName, canonicalName, StringComparison.OrdinalIgnoreCase))?.DisplayName
        ?? canonicalName;
}
