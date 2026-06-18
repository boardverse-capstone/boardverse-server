namespace BoardVerse.Core.Enum
{
    /// <summary>
    /// Danh mục chuẩn cấu phần trong hộp board game (dùng cho kiểm kho / phạt thiếu linh kiện).
    /// </summary>
    public enum BoardGameComponentKind
    {
        GameBoard = 0,
        Rulebook = 1,
        CardDeck = 2,
        PlayerBoard = 3,
        Tile = 4,
        Token = 5,
        Meeple = 6,
        Pawn = 7,
        Die = 8,
        ScoreTrack = 9,
        Marker = 10,
        Money = 11,
        Timer = 12,
        Other = 99
    }
}
