namespace Bandit.UI.Rendering;

public sealed class BrailleCanvas
{
    private static readonly byte[] DotBits =
    [
        0b00000001, 0b00001000,
        0b00000010, 0b00010000,
        0b00000100, 0b00100000,
        0b01000000, 0b10000000,
    ];

    private readonly byte[,] _cells;

    public BrailleCanvas(int cellWidth, int cellHeight)
    {
        CellWidth = Math.Max(1, cellWidth);
        CellHeight = Math.Max(1, cellHeight);
        _cells = new byte[CellWidth, CellHeight];
    }

    public int CellWidth { get; }
    public int CellHeight { get; }
    public int DotWidth => CellWidth * 2;
    public int DotHeight => CellHeight * 4;

    public void SetDot(int dotX, int dotY)
    {
        if ((uint)dotX >= (uint)DotWidth || (uint)dotY >= (uint)DotHeight) return;
        int cx = dotX >> 1;
        int cy = dotY >> 2;
        int sub = ((dotY & 3) << 1) | (dotX & 1);
        _cells[cx, cy] |= DotBits[sub];
    }

    public void FillBelow(int dotX, int dotYTop)
    {
        if ((uint)dotX >= (uint)DotWidth) return;
        if (dotYTop < 0) dotYTop = 0;
        for (int y = dotYTop; y < DotHeight; y++)
            SetDot(dotX, y);
    }

    public char? GetCell(int cellX, int cellY)
    {
        byte mask = _cells[cellX, cellY];
        return mask == 0 ? null : (char)(0x2800 | mask);
    }
}