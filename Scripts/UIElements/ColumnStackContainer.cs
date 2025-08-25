using Godot;
using System;
using System.Linq;

[Tool]
public partial class ColumnStackContainer : Container
{
    [Export]
    bool limitFittedColumnsToChildCount = true;
    [Export]
    int columnMinWidth = 0;
    [Export(PropertyHint.Range, "1, 3, 1, or_greater")]
    int minColumns = 3;
    [Export(PropertyHint.Range, "0, 3, 1, or_greater")]
    int maxColumns = 0;
    [Export]
    Vector2I spacing = new(5, 5);
    [Export(PropertyHint.Range, "1, 20, 1")]
    int iterationsPerFrame = 3;

    public override Vector2 _GetMinimumSize()
    {
        int actualColumns = Mathf.Max(minColumns, GetFittingColumns());
        float[] heights = new float[actualColumns];
        var children = GetChildren().Cast<Control>();
        foreach (var item in children)
        {
            if (!item.Visible)
                continue;
            int thisIndex = GetSmallestIndex(heights);
            bool isFirst = heights[thisIndex] == 0;
            heights[thisIndex] += item.GetCombinedMinimumSize().Y + (isFirst ? 0 : spacing.Y);
        }
        return new(Mathf.Max(0, columnMinWidth), heights.Max());
    }

    int GetFittingColumns()
    {
        if (columnMinWidth <= 0)
            return 1;
        var fitColumns = Mathf.FloorToInt((Size.X + spacing.X) / (columnMinWidth + spacing.X));
        if (limitFittedColumnsToChildCount)
            fitColumns = Mathf.Min(fitColumns, GetChildren().Cast<Control>().Where(c => c.Visible).Count());
        if (maxColumns > 0)
            fitColumns = Mathf.Min(fitColumns, Mathf.Max(minColumns, maxColumns));
        return fitColumns;
    }

    static int GetSmallestIndex<T>(T[] heightArray) where T : IComparable<T>
    {
        if (heightArray.Length == 0)
            return 0;
        int currentSmallest = 0;
        for (int i = 1; i < heightArray.Length; i++)
        {
            if (heightArray[currentSmallest].CompareTo(heightArray[i]) > 0)
                currentSmallest = i;
        }
        return currentSmallest;
    }

    int frameIterations;
    public override void _Process(double delta)
    {
        bool resort = frameIterations <= 0;
        frameIterations = iterationsPerFrame;
        if (resort)
            _Notification((int)NotificationSortChildren);
    }

    public override void _Notification(int what)
    {
        var name = Name;
        if (what == NotificationSortChildren)
        {
            frameIterations--;
            if (frameIterations <= 0)
            {
                return;
            }
            int actualColumns = Mathf.Max(minColumns, GetFittingColumns());
            float cellWidth = (Size.X - (spacing.X * (actualColumns - 1))) / actualColumns;
            float[] heights = new float[actualColumns];
            int[] childCounts = new int[actualColumns];
            var children = GetChildren().Cast<Control>();
            Control[] lastChildren = new Control[actualColumns];
            foreach (var item in children)
            {
                if (!item.Visible)
                    continue;
                int thisIndex = GetSmallestIndex(heights);
                bool isFirst = childCounts[thisIndex] == 0;
                childCounts[thisIndex]++;

                item.Position = new(
                    (cellWidth + spacing.X) * thisIndex,
                    heights[thisIndex] + (isFirst ? 0 : spacing.Y)
                    );

                item.CustomMinimumSize = new(
                    cellWidth,
                    item.CustomMinimumSize.Y
                    );
                item.Size = item.GetCombinedMinimumSize();

                heights[thisIndex] += item.Size.Y + (isFirst ? 0 : spacing.Y);
                lastChildren[thisIndex] = item;
            }
            float maxHeight = heights.Max();
            for (int i = 0; i < actualColumns; i++)
            {
                if (lastChildren[i] is null)
                    break;
                if ((lastChildren[i].SizeFlagsVertical & SizeFlags.Expand) <= 0)
                    continue;
                var diff = maxHeight-heights[i];
                var newSize = lastChildren[i].Size;
                newSize.Y += diff;
                lastChildren[i].Size = newSize;
            }
        }
    }
}
