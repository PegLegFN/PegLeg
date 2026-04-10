using Godot;

public partial class NewRecycleListContainer : Container
{
	[Export]
	Control viewportParent;
	[Export]
	PackedScene recycleElementScene;

	public override void _Ready()
	{
		ProcessPriority = 2;
		if(!viewportParent.IsAncestorOf(this))
			viewportParent = null;
		if (viewportParent == null)
		{
			viewportParent = this;
			while (viewportParent.GetParent() is Control parentControl)
				viewportParent = parentControl;
		}
		ItemRectChanged += MarkListDirty;
		viewportParent.ItemRectChanged += MarkListDirty;
		CheckItems();
	}

	public void SetListProvider(IListProvider newProvider)
	{
		currentSource = newProvider;
		//set entries providers
		//update spawned entries
	}

	IListProvider currentSource;
	bool listDirty = false;

	public void MarkListDirty() => listDirty = true;

	public override void _Process(double delta)
	{
		if (!listDirty)
			return;
		listDirty = false;
		CheckItems();
	}

	void CheckItems()
	{
		var viewportRect = viewportParent.GetGlobalRect();
		var listRect = GetGlobalRect();
		Vector2 relativeViewportPos = viewportRect.Position - listRect.Position;
		Vector2 clampedViewportPos = new(
			Mathf.Max(relativeViewportPos.X, 0),
			Mathf.Max(relativeViewportPos.Y, 0)
		);
		Rect2 relativeVisibleRect = new(
			clampedViewportPos,
			new(
				Mathf.Min(relativeViewportPos.X + viewportRect.Size.X, listRect.Size.X) - clampedViewportPos.X,
				Mathf.Min(relativeViewportPos.Y + viewportRect.Size.Y, listRect.Size.Y) - clampedViewportPos.Y
			)
		);

		//compare rect bounds to see which elements should be visible
		//despawn entries that have gone out of bounds
		//spawn entries that have gone in bounds
	}
}