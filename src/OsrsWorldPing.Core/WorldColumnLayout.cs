namespace OsrsWorldPing.Core;

/// <summary>Balanced numeric columns: read downward, then continue to the right.</summary>
public static class WorldColumnLayout
{
    public static (int Row, int Column) Position(int index, int count, int columns)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, count);
        int shortRows = count / columns, tallerColumns = count % columns;
        int tallItems = (shortRows + 1) * tallerColumns;
        return index < tallItems
            ? (index % (shortRows + 1), index / (shortRows + 1))
            : ((index - tallItems) % shortRows, tallerColumns + (index - tallItems) / shortRows);
    }
}
