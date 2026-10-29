namespace AniT.Infrastructure;

internal static class OnlinePieceRange
{
    internal static bool IsAvailable(long fileOffset, long fileLength, int pieceLength, long offset, int count, Func<int, bool> hasPiece)
    {
        if (fileOffset < 0 || fileLength < 0 || pieceLength <= 0 || offset < 0 || count < 0 || offset > fileLength - count) return false;
        if (count == 0) return true;
        try {
            var first = checked((int)(checked(fileOffset + offset) / pieceLength));
            // The last byte belongs to this request. A boundary must not require the next piece.
            var last = checked((int)(checked(fileOffset + offset + count - 1) / pieceLength));
            for (var piece = first; ; piece++) { if (!hasPiece(piece)) return false; if (piece == last) return true; }
        } catch (OverflowException) { return false; }
    }
}
