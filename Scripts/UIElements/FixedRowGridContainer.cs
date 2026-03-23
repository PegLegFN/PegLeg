using Godot;
using System;
using System.Linq;

[Tool]
public partial class FixedRowGridContainer : Container
{
	[Export]
	int fixedRows = 2;
    [Export]
    Vector2 spacing;
    bool disableSort = false;

    public override Vector2 _GetMinimumSize()
    {
        var children = GetChildren().Cast<Control>().ToArray();
        int visibleChildCount = children.Count(c => c.Visible);
        if (visibleChildCount == 0)
            return Vector2.Zero;

        Vector2 firstChildMinSize = children[0].GetCombinedMinimumSize();
        int colCount = CalcGrid(visibleChildCount).X;

        Vector2 newMinSize = new(
            (firstChildMinSize.X * colCount) + (Mathf.Max(spacing.X, 0) * (colCount - 1)),
            (firstChildMinSize.Y * fixedRows) + (spacing.Y * (fixedRows - 1))
            );

        return newMinSize;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationSortChildren)
        {
            PerformSort();
        }
    }

    void PerformSort()
    {
        if (disableSort)
            return;

        CustomMinimumSize = _GetMinimumSize();

        var children = GetChildren().Cast<Control>().ToArray();
        int visibleChildCount = children.Count(c => c.Visible);
        if (visibleChildCount == 0)
            return;

        var grid = CalcGrid(visibleChildCount);
        int rowCount = grid.Y;
        Rect2 spacing = CalcSpacing(grid, children[0]);

        int validIndex = 0;
        foreach (Control c in children)
        {
            if (c is null || !c.IsVisibleInTree())
                continue;
            int row = validIndex % rowCount;
            int col = validIndex / rowCount;
            FitChildInRect(c, new Rect2(((spacing.Position + spacing.Size) * new Vector2(col, row)), spacing.Size));
            validIndex++;
        }
    }

    Vector2I CalcGrid(int visibleChildCount)
    {
        int rowCount = fixedRows;
        int colCount = Mathf.CeilToInt((float)visibleChildCount / rowCount);

        return new(colCount, rowCount);
    }

    Rect2 CalcSpacing(Vector2I grid, Control firstChild)
    {
        Vector2 finalSpacing = spacing;
        Vector2 baseSize = new(0, (Size.Y - (spacing.Y * (grid.Y - 1))) / grid.Y);

        var beforeSize = firstChild.Size;
        firstChild.Size = baseSize;

        Vector2 minSize = GetChild<Control>(0).GetCombinedMinimumSize();
        Vector2 finalSize = new(Mathf.Max(minSize.X, baseSize.X), Mathf.Max(minSize.Y, baseSize.Y));

        firstChild.Size = beforeSize;

        return new(finalSpacing, finalSize);
    }
}
