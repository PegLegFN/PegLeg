using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class CosmeticShopSection : Container
{
	[Export]
	PackedScene offerEntryScene;
	[Export]
	float headerHeight = 50;
	[Export]
	float rowWidth = 800;
	[Export]
	float rowHeight = 225;
	[Export]
	float rowSpacing = 28;
	[Export]
	float offerSpacing = 4;
	[Export]
	Vector2 extendViewportBounds = new(100, 100);
	[ExportGroup("Nodes")]
	[Export]
	Control viewportParent;
	[Export]
	Control headerRoot;
	[Export]
	Label headerLabel;
	[Export]
	Label subtitleLabel;
	[Export]
	Control headerJamTrackButton;

	public CosmeticShopInterfaceNew parentInterface;
	CosmeticSectionGroup? currentSection;
	Dictionary<string, CosmeticOfferEntryNew> activeOfferEntries = [];
	Queue<CosmeticOfferEntryNew> pooledOfferEntries = [];

	public override void _Ready()
	{
		ProcessPriority = 3;
		CheckLayout();
	}

	bool hasSetup;
	void Setup()
	{
		if (hasSetup)
			return;

		if (viewportParent is not null && !viewportParent.IsAncestorOf(this))
			viewportParent = null;
		if (viewportParent is null)
		{
			viewportParent = this;
			while (viewportParent.GetParent() is Control parentControl)
			{
				viewportParent = parentControl;
				if (viewportParent is ScrollContainer sc && sc.VerticalScrollMode != ScrollContainer.ScrollMode.Disabled)
					break;
			}
		}
		ItemRectChanged += MarkDirty;
		viewportParent.ItemRectChanged += MarkDirty;
		hasSetup = true;
	}

	bool isDirty = false;
	public void MarkDirty() => isDirty = true;
	public override void _Process(double delta)
	{
		CheckLayout();
	}
	public void SetPool(ref Queue<CosmeticOfferEntryNew> pool)
	{
		foreach (var entry in pooledOfferEntries.ToArray())
		{
			entry.QueueFree();
		}
		pooledOfferEntries.Clear();
		pooledOfferEntries = pool ?? [];
	}

	public void SetOfferSection(CosmeticSectionGroup? section)
	{
		if (currentSection == section)
			return;
		currentSection = section;
		headerJamTrackButton?.Visible = section?.rows.Any(r => r.offers.Length > 4) ?? false;
		ForceCheckLayout();
	}

	public override Vector2 _GetMinimumSize()
	{
		var headerHeight = string.IsNullOrWhiteSpace(currentSection?.sectionData?.displayName) ? 0 : headerRoot.GetMinimumSize().Y;
		var rowCount = currentSection?.rows.Length ?? 0;
		return new(rowWidth, headerHeight + (rowCount * (rowHeight + rowSpacing)));
	}

	Vector2 prevRelativePos;
	float offerCellWidth;
	void ForceCheckLayout() => CheckLayout(true);
	void CheckLayout() => CheckLayout(false);
	void CheckLayout(bool force)
	{
		Setup();
		if (currentSection is null)
		{
			if (activeOfferEntries.Count == 0)
				return;
			foreach (var oldOfferID in activeOfferEntries.Keys.ToArray())
			{
				if (!activeOfferEntries.TryGetValue(oldOfferID, out var offerEntry))
					continue;
				RemoveChild(offerEntry);
				offerEntry.ClearOffer();
				pooledOfferEntries.Enqueue(offerEntry);
				activeOfferEntries.Remove(oldOfferID);
			}
			Name = "Empty Section";
			return;
		}

		Vector2 relativePos = GlobalPosition - viewportParent.GlobalPosition;
		if (!force)
		{
			if (!IsVisibleInTree())
				return;
			if (!isDirty && Mathf.Abs(relativePos.Y - prevRelativePos.Y) < extendViewportBounds.Y / 2)
				return;
			//GD.Print($"Refresh section {GetIndex()} (dirty:{isDirty})(abs of {relativePos.Y}-{prevRelativePos.Y} is {relativePos.Y - prevRelativePos.Y}, greater than {extendViewportBounds.Y / 2})");
		}
		prevRelativePos = relativePos;
		isDirty = false;

		//force layout update to parents
		CustomMinimumSize += Vector2.One * 0.1f;
		CustomMinimumSize -= Vector2.One * 0.1f;

		var viewportRect = viewportParent.GetGlobalRect();
		var sectionRect = GetGlobalRect();

		NewRecycleListContainer.ScaleRects(ref sectionRect, ref viewportRect, Size);
		Rect2 relativeVisibleRect = NewRecycleListContainer.GetRelativeRect(sectionRect, viewportRect, extendViewportBounds);

		headerRoot.Visible = !string.IsNullOrWhiteSpace(currentSection?.sectionData?.displayName);
		headerLabel?.Text = currentSection?.sectionData?.displayName;
		subtitleLabel?.Visible = !string.IsNullOrWhiteSpace(currentSection?.sectionData?.subtitle);
		subtitleLabel?.Text = currentSection?.sectionData?.subtitle;

		offerCellWidth = (rowWidth - (offerSpacing * 3)) / 4;

		List<string> visibleOfferIDs = [];
		for (int i = 0; i < currentSection.Value.rows.Length; i++)
		{
			bool rowVisible = PlaceOfferRow(currentSection.Value.rows[i].offers, i, relativeVisibleRect);
			if (rowVisible)
				visibleOfferIDs.AddRange(currentSection.Value.rows[i].offers.Select(o => o.OfferId));
		}

		foreach (var oldOfferID in activeOfferEntries.Keys.Except(visibleOfferIDs).ToArray())
		{
			if (!activeOfferEntries.TryGetValue(oldOfferID, out var offerEntry))
				continue;
			RemoveChild(offerEntry);
			offerEntry.ClearOffer();
			pooledOfferEntries.Enqueue(offerEntry);
			activeOfferEntries.Remove(oldOfferID);
		}
		Name = $"Section {currentSection?.sectionId} ({activeOfferEntries.Count})";
	}

	bool PlaceOfferRow(GameOffer[] offers, int rowIdx, Rect2 visibleRect)
	{
		Vector2 rowOrigin = new(0, headerHeight + rowSpacing + (rowIdx * (rowHeight + rowSpacing)));
		bool onscreen = visibleRect.Position.Y < rowOrigin.Y + rowHeight && visibleRect.Position.Y + visibleRect.Size.Y > rowOrigin.Y;
		string[] entriesToRemove = [];

		if (!onscreen)
			return false;

		Vector2 offerOrigin = rowOrigin;
		int count = 0;
		foreach (var offer in offers.OrderByDescending(o => o.SortPriority))
		{
			count++;
			if (count > 4)
				break;
			if (activeOfferEntries.ContainsKey(offer.OfferId))
				continue;
			if (!pooledOfferEntries.TryDequeue(out var offerEntry))
			{
				offerEntry = offerEntryScene.Instantiate<CosmeticOfferEntryNew>();
				offerEntry.filterOutPredicate = parentInterface is not null ? parentInterface.FilterOffer : null;
				offerEntry.SizeFlagsHorizontal = SizeFlags.ExpandFill;
				offerEntry.SizeFlagsVertical = SizeFlags.ExpandFill;
			}

			AddChild(offerEntry);

			var cellUnits = offer.CosmeticTileSize.X;
			var width = cellUnits * offerCellWidth + ((cellUnits - 1) * offerSpacing);
			Rect2 offerRect = new(offerOrigin, new(width, rowHeight));
			FitChildInRect(offerEntry, offerRect);

			offerEntry.SetOffer(offer);
			activeOfferEntries.Add(offer.OfferId, offerEntry);

			offerOrigin.X += width + offerSpacing;
		}
		return true;
	}
	static void OpenJamTracks() => OS.ShellOpen("https://www.fortnite.com/item-shop/jam-tracks");
}
