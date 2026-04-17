using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

public partial class NewRecycleListContainer : Container, IListHandler
{
	enum RecycleLayoutMode
	{
		VList,
		HList,
		DynamicGrid,
	}

	[Export]
	Control viewportParent;
	[Export]
	PackedScene recycleEntryScene;
	[Export]
	ColorRect blinkRect;
	[Export]
	RecycleLayoutMode layoutMode;
	[Export]
	Vector2 spacing = new(5, 5);
	[Export]
	Vector2 shift = new(0.5f, 0.5f);
	[Export]
	Vector2 extendViewportBounds = new(100,100);//todo: extend the effective bounds of the viewport by this amount, to compensate for elastic scrolling on mobile
	[Export]
	bool debug;

	Queue<IListEntry> pooledEntries = [];
	Dictionary<int, IListEntry> activeEntries = [];
	IListProvider currentListProvider;
	IListEntry basisEntry;
	LayoutProvider currentLayoutProvider;
	LayoutProvider.LayoutInfo currentLayoutInfo;

	public override void _Ready()
	{
		CheckItems();
	}

	void Setup()
	{
		if (currentLayoutProvider is not null)
			return;
		currentLayoutProvider = layoutMode switch
		{
			RecycleLayoutMode.VList => new VListLayoutProvider(),
			RecycleLayoutMode.HList => new HListLayoutProvider(),
			RecycleLayoutMode.DynamicGrid => new DynamicGridLayoutProvider(),
			_ => new VListLayoutProvider(),
		};

		ProcessPriority = 2;
		if (viewportParent is not null && !viewportParent.IsAncestorOf(this))
			viewportParent = null;
		if (viewportParent is null)
		{
			viewportParent = this;
			while (viewportParent.GetParent() is Control parentControl)
			{
				viewportParent = parentControl;
				if (viewportParent is ScrollContainer sc &&
						(
							sc.HorizontalScrollMode != ScrollContainer.ScrollMode.Disabled ||
							sc.VerticalScrollMode != ScrollContainer.ScrollMode.Disabled
						)
					)
					break;
			}
		}
		ItemRectChanged += MarkListDirty;

		basisEntry = recycleEntryScene.Instantiate<IListEntry>();
		var basisNode = basisEntry.Node;
		AddChild(basisNode);
		basisNode.Visible = false;
		basisNode.Size = Vector2.Zero;
		basisNode.Name = $"{Name}.BasisNode";

		viewportParent.ItemRectChanged += MarkListDirty;
	}

	public void LinkListProvider(IListProvider newProvider)
	{
		currentListProvider = newProvider;
		for (int i = 0; i < activeEntries.Count; i++)
		{
			activeEntries[i].SetListProvider(newProvider);
		}
		currentLayoutInfo = CreateLayoutInfo();
		CheckItems();
	}

	bool listDirty = false;

	public void MarkListDirty() => listDirty = true;

	Vector2 prevRelativePos;
	public override void _Process(double delta)
	{
		CheckItems();
	}

	public override Vector2 _GetMinimumSize() => currentLayoutProvider?.GetMinSize(currentLayoutInfo, GetGlobalRect(), true) ?? Vector2.Zero;

	LayoutProvider.LayoutInfo CreateLayoutInfo() => new()
	{
		listProvider = currentListProvider,
		basisEntry = basisEntry,
		spacing = spacing,
		shift = shift,
	};

	int[] lastIndices = [];
	Vector2 lastListSize = Vector2.Zero;
	bool wasEnclosed = false;
	int lastCount = 0;

	void IListHandler.UpdateList() => CheckItems(true);

	void CheckItems() => CheckItems(false);
	void ForceCheckItems() => CheckItems(true);
	void CheckItems(bool force)
	{
		Setup();
		if (currentListProvider is null)
			return; //TODO: remove any active entries, in case there used to be a list provider
		Vector2 relativePos = GlobalPosition - viewportParent.GlobalPosition;
		if (!force)
		{
			if (!IsVisibleInTree() && !force)
				return;
			if (!listDirty && relativePos == prevRelativePos)
				return;
		}
		prevRelativePos = relativePos;
		listDirty = false;
		
		var viewportRect = viewportParent.GetGlobalRect();
		var listRect = GetGlobalRect();

		Vector2 sizeScale = Vector2.One;
		if (listRect.Size.X > 0)
			sizeScale.X = Size.X / listRect.Size.X;
		if (listRect.Size.Y > 0)
			sizeScale.Y = Size.Y / listRect.Size.Y;

		viewportRect.Size *= sizeScale;
		listRect.Size *= sizeScale;

		viewportRect.Size += extendViewportBounds;
		viewportRect.Position -= extendViewportBounds / 2;

		CustomMinimumSize += Vector2.One * 0.1f;
		CustomMinimumSize -= Vector2.One * 0.1f;

		if (currentLayoutInfo.listProvider != currentListProvider)
			currentLayoutInfo = CreateLayoutInfo();

		//update list rect using layout provider min size
		listRect.Size = currentLayoutProvider.GetMinSize(currentLayoutInfo, listRect, false);

		//saves computation when the list is fully within the viewport (such as small lists in big viewports)
		//bool isEnclosed = viewportRect.Encloses(listRect);
		//if (wasEnclosed && isEnclosed && lastCount == currentListProvider.ListItemCount && !force)
		//	return;
		//wasEnclosed = isEnclosed;
		//lastCount = currentListProvider.ListItemCount;

		Vector2 relativeViewportPos = viewportRect.Position - listRect.Position;
		Vector2 clampedViewportPos = new(
			Mathf.Max(relativeViewportPos.X, 0),
			Mathf.Max(relativeViewportPos.Y, 0)
		);
		Rect2 relativeVisibleRect = new(
			clampedViewportPos,
			new(
				Mathf.Max(Mathf.Min(relativeViewportPos.X + viewportRect.Size.X, listRect.Size.X) - clampedViewportPos.X, 0),
				Mathf.Max(Mathf.Min(relativeViewportPos.Y + viewportRect.Size.Y, listRect.Size.Y) - clampedViewportPos.Y, 0)
			)
		);


		LayoutProvider.EntryLayout[] entriesToUse =
			(relativeVisibleRect.Size.X == 0 || relativeVisibleRect.Size.Y == 0) ? [] :
			currentLayoutProvider.GetVisibleEntryLayouts(currentLayoutInfo, relativeVisibleRect, listRect);

		int[] currentIndexes = [.. entriesToUse.Select(e => e.index).Order()];
		bool indicesMatch = Enumerable.SequenceEqual(currentIndexes, lastIndices);

		if (!force && listRect.Size == lastListSize && indicesMatch)
			return;

		//if (listRect.Size != lastListSize)
		if (debug)
			GD.Print($"Size: {lastListSize}=>{listRect.Size}");
			//if (!indicesMatch)
		if (debug)
			GD.Print($"Indices: \n{string.Join(", ", lastIndices)}\n=>\n{string.Join(", ", currentIndexes)}");

		lastListSize = listRect.Size;
		lastIndices = currentIndexes;

		var lostIndexes = activeEntries.Keys.Except(entriesToUse.Select(e => e.index)).ToArray();
		for (int i = 0; i < lostIndexes.Length; i++)
		{
			var entry = activeEntries[lostIndexes[i]];
			activeEntries.Remove(lostIndexes[i]);
			entry.Node.Visible = false;
			pooledEntries.Enqueue(entry);
		}

		foreach (var layoutEntry in entriesToUse)
		{
			if (!activeEntries.TryGetValue(layoutEntry.index, out var listEntry))
			{
				if (pooledEntries.TryDequeue(out var pooledEntry))
				{
					listEntry = pooledEntry;
				}
				else
				{
					var instantiatedEntry = recycleEntryScene.Instantiate<IListEntry>();
					AddChild(instantiatedEntry.Node);
					instantiatedEntry.SetListProvider(currentListProvider);
					listEntry = instantiatedEntry;
				}
				activeEntries.Add(layoutEntry.index, listEntry);
			}
			listEntry.Node.Visible = true;
			listEntry.SetTargetListIndex(layoutEntry.index);
			FitChildInRect(listEntry.Node, layoutEntry.rect);
		}
		PerformBlink();
		//GD.Print("active: " + activeEntries.Count);
		//FitChildInRect(demoRect, relativeVisibleRect);
	}

	SemaphoreSlim blinkSemaphore = new(1);
	async void PerformBlink()
	{
		if (blinkSemaphore.CurrentCount == 0 || blinkRect is null)
			return;
		using var _ = await blinkSemaphore.AwaitToken();
		blinkRect.Color = Colors.Red.Lerp(Colors.Transparent, 0.5f);
		await Helpers.WaitForTimer(0.05f);
		blinkRect.Color = Colors.Transparent;
	}

	public abstract class LayoutProvider
	{
		public record struct LayoutInfo(IListProvider listProvider, IListEntry basisEntry, Vector2 spacing, Vector2 shift);
		public abstract Vector2 GetMinSize(LayoutInfo layoutInfo, Rect2 totalRect, bool compressed);

		public record struct EntryLayout(Rect2 rect, int index);
		public abstract EntryLayout[] GetVisibleEntryLayouts(LayoutInfo layoutInfo, Rect2 visibleRect, Rect2 totalRect);
	}

	public abstract class BaseListLayoutProvider : LayoutProvider
	{
		protected abstract Vector2 RelevantAxis { get; }
		private Vector2 IrrelevantAxis => Vector2.One - RelevantAxis;

		public sealed override Vector2 GetMinSize(LayoutInfo layoutInfo, Rect2 totalRect, bool compressed)
		{
			if (layoutInfo.basisEntry is null)
				return Vector2.Zero;
			var nodeSize = layoutInfo.basisEntry.Node.GetCombinedMinimumSize();
			var nodeCount = layoutInfo.listProvider.ListItemCount;
			Vector2 scaledSize = (nodeCount * (RelevantAxis * nodeSize)) + ((nodeCount - 1) * (RelevantAxis * layoutInfo.spacing));
			float cappedSize = Mathf.Max(0, Mathf.Max(scaledSize.X, scaledSize.Y));
			return (IrrelevantAxis * (compressed ? nodeSize : totalRect.Size)) + (RelevantAxis * cappedSize);
		}

		public sealed override EntryLayout[] GetVisibleEntryLayouts(LayoutInfo layoutInfo, Rect2 visibleRect, Rect2 totalRect)
		{
			var itemCount = layoutInfo.listProvider.ListItemCount;
			if (itemCount == 0)
				return [];
			var nodeSize = layoutInfo.basisEntry.Node.GetCombinedMinimumSize();

			var relevantStartVec = RelevantAxis * (visibleRect.Position + layoutInfo.spacing);
			var relevantEndVec = relevantStartVec + (RelevantAxis * visibleRect.Size);
			var relevantDivisorVec = RelevantAxis * (nodeSize + layoutInfo.spacing);

			var start = relevantStartVec.X == 0 ? relevantStartVec.Y : relevantStartVec.X;
			var end = relevantEndVec.X == 0 ? relevantEndVec.Y : relevantEndVec.X;
			var divisor = relevantDivisorVec.X == 0 ? relevantDivisorVec.Y : relevantDivisorVec.X;

			int startingIndex = Mathf.FloorToInt(start / divisor);
			int endingIndex = Mathf.CeilToInt(end / divisor);

			startingIndex = Mathf.Max(startingIndex, 0);
			endingIndex = Mathf.Min(endingIndex, itemCount);

			if (startingIndex > endingIndex)
				return [];

			int[] indices = [.. Enumerable.Range(startingIndex, endingIndex - startingIndex)];
			var indexIncrement = RelevantAxis * (nodeSize + layoutInfo.spacing);
			return [.. indices.Select(i => new EntryLayout(new(i * indexIncrement, (RelevantAxis * nodeSize) + (IrrelevantAxis * totalRect.Size)), i))];
		}
	}

	public class VListLayoutProvider : BaseListLayoutProvider
	{
		protected override Vector2 RelevantAxis => Vector2.Down;
	}

	public class HListLayoutProvider : BaseListLayoutProvider
	{
		protected override Vector2 RelevantAxis => Vector2.Right;
	}
	
	public class DynamicGridLayoutProvider : LayoutProvider
	{
		public override Vector2 GetMinSize(LayoutInfo layoutInfo, Rect2 totalRect, bool compressed)
		{
			if (layoutInfo.basisEntry is null)
				return Vector2.Zero;
			var nodeSize = layoutInfo.basisEntry.Node.GetCombinedMinimumSize();

			int colCount = Mathf.Max(Mathf.FloorToInt((totalRect.Size.X + layoutInfo.spacing.X) / (nodeSize.X + layoutInfo.spacing.X)), 1);
			int rowCount = Mathf.CeilToInt((float)layoutInfo.listProvider.ListItemCount / colCount);
			float totalHeight = (rowCount * nodeSize.Y) + ((rowCount - 1) * layoutInfo.spacing.Y);

			return new(
				compressed ? nodeSize.X : totalRect.Size.X,
				totalHeight
			);
		}

		public override EntryLayout[] GetVisibleEntryLayouts(LayoutInfo layoutInfo, Rect2 visibleRect, Rect2 totalRect)
		{
			var itemCount = layoutInfo.listProvider.ListItemCount;
			if (itemCount == 0)
				return [];
			var nodeSize = layoutInfo.basisEntry.Node.GetCombinedMinimumSize();

			int colCount = Mathf.Max(Mathf.FloorToInt((totalRect.Size.X + layoutInfo.spacing.X) / (nodeSize.X + layoutInfo.spacing.X)), 1);
			int rowCount = Mathf.CeilToInt((float)itemCount / colCount);
			colCount = Mathf.Min(colCount, itemCount);


			Vector2 gridSpacing = layoutInfo.spacing;
			gridSpacing.X = Mathf.Max(gridSpacing.X, 0);
			gridSpacing.Y = Mathf.Max(gridSpacing.Y, 0);
			Vector2 gridOrigin = new(0, 0);
			float extraSpace = totalRect.Size.X - ((nodeSize.X * colCount) + (gridSpacing.X * (colCount - 1)));
			if (layoutInfo.shift.X >= 0)
				gridOrigin.X = extraSpace * layoutInfo.shift.X;
			else
				gridSpacing.X += extraSpace / (colCount - 1);

			int minRow = Mathf.FloorToInt((visibleRect.Position.Y + gridSpacing.Y - gridOrigin.Y) / (nodeSize.Y + gridSpacing.Y));
			int maxRow = Mathf.CeilToInt((visibleRect.Position.Y + visibleRect.Size.Y + gridSpacing.Y - gridOrigin.Y) / (nodeSize.Y + gridSpacing.Y));
			int minCol = Mathf.FloorToInt((visibleRect.Position.X + gridSpacing.X - gridOrigin.X) / (nodeSize.X + gridSpacing.X));
			int maxCol = Mathf.CeilToInt((visibleRect.Position.X + visibleRect.Size.X + gridSpacing.X - gridOrigin.X) / (nodeSize.X + gridSpacing.X));

			minRow = Mathf.Max(minRow, 0);
			minCol = Mathf.Max(minCol, 0);
			maxRow = Mathf.Min(maxRow, rowCount);
			maxCol = Mathf.Min(maxCol, colCount);

			List<EntryLayout> layouts = [];
			Vector2 increment = nodeSize + gridSpacing;

			for (int c = minCol; c < maxCol; c++)
			{
				for (int r = minRow; r < maxRow; r++)
				{
					int idx = (r * colCount) + c;
					if (idx >= itemCount)
						continue;
					layouts.Add(
						new(
							new(
								gridOrigin + (increment * new Vector2(c, r)), 
								nodeSize
							), 
							(r * colCount) + c
						)
					);
				}
			}

			return [.. layouts];
		}
	}

	/*
	public class VListLayoutProvider : LayoutProvider
	{
		public override Vector2 GetMinSize(LayoutInfo layoutInfo)
		{
			var nodeSize = layoutInfo.basisEntry.Node.GetCombinedMinimumSize();
			var nodeCount = layoutInfo.listProvider.ListItemCount;
			return new(nodeSize.X, Mathf.Max((nodeCount * nodeSize.Y) + ((nodeCount - 1) * layoutInfo.spacing.Y), 0));
		}

		public override Rect2 GetRelativeRectForItem(LayoutInfo layoutInfo, int itemIndex)
		{
			var nodeSize = layoutInfo.basisEntry.Node.GetCombinedMinimumSize();
			float offset = (nodeSize.Y + layoutInfo.spacing.Y) * itemIndex;
			return new(
				Vector2.Up * offset,
				nodeSize
			);
		}

		public override int[] GetIndicesForRect(LayoutInfo layoutInfo, Rect2 visibleRect, Rect2 totalRect)
		{
			var nodeSize = layoutInfo.basisEntry.Node.GetCombinedMinimumSize();
			int startingIndex = Mathf.FloorToInt((visibleRect.Position.Y + layoutInfo.spacing.Y) / (nodeSize.Y + layoutInfo.spacing.Y));
			int endingIndex = Mathf.CeilToInt((visibleRect.Position.Y + visibleRect.Size.Y + layoutInfo.spacing.Y) / (nodeSize.Y + layoutInfo.spacing.Y));
			if (startingIndex == endingIndex)
				return [];
			return [.. Enumerable.Range(startingIndex, endingIndex)];
		}
	}

	public class HListLayoutProvider : LayoutProvider
	{
		public override Vector2 GetMinSize(LayoutInfo layoutInfo)
		{
			var nodeSize = layoutInfo.basisEntry.Node.GetCombinedMinimumSize();
			var nodeCount = layoutInfo.listProvider.ListItemCount;
			return new(Mathf.Max((nodeCount * nodeSize.X) + ((nodeCount - 1) * layoutInfo.spacing.X), 0), nodeSize.Y);
		}

		public override Rect2 GetRelativeRectForItem(LayoutInfo layoutInfo, int itemIndex)
		{
			var nodeSize = layoutInfo.basisEntry.Node.GetCombinedMinimumSize();
			float offset = (nodeSize.X + layoutInfo.spacing.X) * itemIndex;
			return new(
				Vector2.Right * offset,
				nodeSize
			);
		}

		public override int[] GetIndicesForRect(LayoutInfo layoutInfo, Rect2 visibleRect, Rect2 totalRect)
		{
			var nodeSize = layoutInfo.basisEntry.Node.GetCombinedMinimumSize();
			int startingIndex = Mathf.FloorToInt((visibleRect.Position.X + layoutInfo.spacing.X) / (nodeSize.X + layoutInfo.spacing.X));
			int endingIndex = Mathf.CeilToInt((visibleRect.Position.X + visibleRect.Size.X + layoutInfo.spacing.X) / (nodeSize.X + layoutInfo.spacing.X));
			if (startingIndex == endingIndex)
				return [];
			return [.. Enumerable.Range(startingIndex, endingIndex)];
		}
	}
	*/
}