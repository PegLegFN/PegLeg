using Godot;
using System;
using System.Linq;

[Tool]
public partial class DynamicGridContainer : Container
{
	[Export(PropertyHint.Range, "1, 10, or_greater")]
	int MinCols
	{
		get => minCols;
		set
		{
			minCols = value;
			UpdateLayout();
		}
	}
	int minCols = 1;
	[Export]
	bool AutoColWidth
	{
		get => autoColWidth;
		set
		{
			autoColWidth = value;
			UpdateLayout();
		}
	}
	bool autoColWidth = true;
	[Export]
	float ManualColWidth
	{
		get => manualColWidth;
		set
		{
			manualColWidth = value;
			UpdateLayout();
		}
	}
	float manualColWidth;
	[Export]
	bool CompressSpacing
	{
		get => compressSpacing;
		set
		{
			compressSpacing = value;
			UpdateLayout();
		}
	}
	bool compressSpacing;
	[Export(PropertyHint.Range, "0, 1")]
	float CompressTowards
	{
		get => compressTowards;
		set
		{
			compressTowards = value;
			UpdateLayout();
		}
	}
	float compressTowards = 0.5f;
	[Export]
	Vector2 Spacing
	{
		get => spacing;
		set
		{
			spacing = value;
			UpdateLayout();
		}
	}
	Vector2 spacing = Vector2.One * 5;
	[Export]
	bool UseManualColumnCounts
	{
		get => useManualColCounts;
		set
		{
			useManualColCounts = value;
			UpdateLayout();
		}
	}
	bool useManualColCounts;
	[Export]
	bool UseLargestChild
	{
		get => useLargestChild;
		set
		{
			useLargestChild = value;
			UpdateLayout();
		}
	}
	bool useLargestChild;
	[Export]
	int[] ColumnCounts
	{
		get => manualColumnCounts;
		set
		{
			manualColumnCounts = value;
			UpdateLayout();
		}
	}
	int[] manualColumnCounts;

	//get col count from col width
	//derive rows from col count
	//min height of each row is the largest child min height

	//public override void _Ready()
	//{
	//    SortChildren += UpdateLayout;
	//}

	bool lockMinSize = false;
	public override Vector2 _GetMinimumSize()
	{
		if (lockMinSize)
			return Vector2.Zero;
		try
		{
			lockMinSize = true;
			(var sizeChild, var children) = GetRelevantChildren();
			if (children.Length == 0)
			{
				lockMinSize = false;
				return Vector2.Zero;
			}

			int colCount = GetColCount(sizeChild.GetCombinedMinimumSize().X, out var colWidth);
			//int rowCount = Mathf.CeilToInt((float)children.Length / colCount);
			//float totalHeight = GetRowHeights(children, colCount).Sum();

			Vector2 newMinSize = new(
				(colWidth * minCols) + (Mathf.Max(spacing.X, 0) * (minCols - 1)),
				GetRowHeights(children, colCount).Sum()
			);
			return newMinSize;
		}
		finally
		{
			lockMinSize = false;
		}
	}

	Control[] GetControlChildren() => [.. GetChildren().OfType<Control>()];
	Control PrimaryChild(Control[] ofChildren) => useLargestChild ? (ofChildren ?? []).OrderBy(c => c.GetCombinedMinimumSize().X).LastOrDefault() : ofChildren?.FirstOrDefault();

	(Control, Control[]) GetRelevantChildren()
	{
		var children = GetControlChildren();
		if (children.Length == 0)
			return (null, []);
		return (PrimaryChild(children), children.Where(c => c.Visible).ToArray());
	}

	public int GetColCount(float? givenChildWidth = null) => GetColCount(givenChildWidth, out var _);
	public int GetColCount(float? givenChildWidth, out float colWidth)
	{
		colWidth = autoColWidth ? (givenChildWidth ?? PrimaryChild(GetControlChildren()).GetCombinedMinimumSize().X) : manualColWidth;
		int colCount = Mathf.Max(Mathf.FloorToInt((Size.X + spacing.X) / (colWidth + spacing.X)), minCols);

		if (useManualColCounts && manualColumnCounts is not null)
		{
			int selectedColCount = 1;
			for (int i = 0; i < manualColumnCounts.Length; i++)
			{
				if (manualColumnCounts[i] <= colCount)
					selectedColCount = manualColumnCounts[i];
				else
					break;
			}
			colCount = Mathf.Max(selectedColCount, minCols);
		}
		return colCount;
	}


	bool disableSort = false;
	public void SetDisableSort(bool value)
	{
		disableSort = value;
		if (!disableSort)
			UpdateLayout();
	}

	public float[] GetRowHeights(Control[] children, int colCount)
	{
		int rowCount = Mathf.CeilToInt((float)children.Length / colCount);
		float[] heights = new float[rowCount];
		float curHeight = 0;
		for (int i = 0; i < children.Length; i++)
		{
			curHeight = Mathf.Max(curHeight, children[i].GetCombinedMinimumSize().Y);

			if (i % colCount == colCount - 1)
			{
				heights[i / colCount] = curHeight + spacing.Y;
				curHeight = 0;
			}
		}
		if (children.Length % colCount != 0)
		{
			heights[^1] = curHeight;
		}
		else
		{
			heights[^1] -= spacing.Y;
		}
		return heights;
	}

	bool lockLayout = false;
	public override void _Notification(int what)
	{
		if (what == NotificationSortChildren)
			UpdateLayout();
	}

	void UpdateLayout()
	{
		if (disableSort || lockLayout)
			return;
		try
		{
			lockLayout = true;

			//force a minimum size change so that parent containers will check this containers minimum size again
			CustomMinimumSize += Vector2.One * 0.1f;
			CustomMinimumSize -= Vector2.One * 0.1f;

			var children = GetControlChildren();
			int visibleChildCount = children.Count(c => c.Visible);
			if (visibleChildCount == 0)
			{
				lockLayout = false;
				return;
			}

			int colCount = GetColCount(null, out var colWidth);
			int rowCount = Mathf.CeilToInt((float)children.Length / colCount);

			int compressedCols = Mathf.Min(colCount, visibleChildCount);
			Vector2 gridSpacing = spacing;
			gridSpacing.X = Mathf.Max(gridSpacing.X, 0);
			gridSpacing.Y = Mathf.Max(gridSpacing.Y, 0);
			Vector2 gridOrigin = new(0, 0);
			float extraSpace = Size.X - ((colWidth * compressedCols) + (gridSpacing.X * (compressedCols - 1)));
			if (compressSpacing)
			{
				gridOrigin.X = extraSpace * compressTowards;
			}
			else
			{
				gridSpacing.X += extraSpace / (colCount - 1);
			}

			float[] rowHeights = GetRowHeights(children, colCount);

			int validIndex = 0;
			foreach (var c in children)
			{
				if (!c.IsVisibleInTree())
					continue;
				int row = validIndex / colCount;
				int col = validIndex % colCount;
				float rowOffset = rowHeights[..row].Sum();
				FitChildInRect(c,
					new Rect2(
						gridOrigin + new Vector2((colWidth + gridSpacing.X) * col, rowOffset),
						new Vector2(colWidth, c.GetCombinedMinimumSize().Y)
					)
				);
				validIndex++;
			}
		}
		finally
		{
			lockLayout = false;
		}
	}
}
