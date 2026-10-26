using System;
using System.Collections.Generic;
using System.Linq;

namespace AniT.App;

public sealed record LibraryPosterRow(IReadOnlyList<AnimeLibraryItem> Items, int Columns);

internal static class LibraryPosterRows
{
    public static int Columns(double availableWidth, double cardWidth) => Math.Max(1, (int)(Math.Max(1, availableWidth) / (cardWidth + 16)));
    public static IReadOnlyList<LibraryPosterRow> Build(IEnumerable<AnimeLibraryItem> items, double availableWidth, double cardWidth)
    {
        var columns = Columns(availableWidth, cardWidth);
        return items.Chunk(columns).Select(chunk => new LibraryPosterRow(chunk, columns)).ToArray();
    }
}
