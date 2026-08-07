using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

public partial class CosmeticShopInterfaceNew : Control
{
	static CosmeticShopInterfaceNew instance;
	public static event Action OnFiltersChanged;
	public static bool CurrentOfferFilter(GameOffer o) => instance?.FilterOffer(o) ?? true;

	[Export]
	PackedScene shopSectionScene;
	[Export]
	float baseSectionHeight = 50;
	[Export]
	float sectionHeightPerRow = 225 + 28;
	[Export]
	Vector2 extendViewportBounds = new(100, 100);

	[ExportGroup("Nodes")]
	[Export]
	Control buffering;
	[Export]
	NewRecycleListContainer compactCosmeticList;
	[Export]
	Control shopSectionRoot;
	[Export]
	ScrollContainer shopViewport;
	[Export]
	VirtualTabBar timeFilters;
	[Export]
	VirtualTabBar typeFilters;
	[Export]
	Tree navTree;
	[Export]
	Control navContainer;
	[Export]
	Control navToggle;
	[Export]
	Button sacButton;
	[Export]
	Control sacContent;

	Dictionary<string, CosmeticShopSection> activeSectionEntries = [];
	Queue<CosmeticShopSection> sectionEntryPool = [];
	Queue<CosmeticOfferEntryNew> offerEntryPool = [];

	public override void _Ready()
	{
		instance = this;
		shopSectionRoot.ItemRectChanged += CheckSections;
		shopViewport.ItemRectChanged += CheckSections;
		timeFilters.TabsChanged += FilterShop;
		typeFilters.TabsChanged += FilterShop;
		navTree.CellSelected += OnNavSelected;
		sacButton.Pressed += OpenSACPrompt;

		var compact = IsCompact;
		shopSectionRoot.Visible = !compact;
		compactCosmeticList.Visible = compact;
		navToggle.Visible = !compact;
		navContainer.Visible = compact ? false : NavVisible;

		includeCompactUnfiltered = PegLegResourceManager.MagicNumbers?["includeCompactUnfiltered"]?.GetValue<bool>() ?? includeCompactUnfiltered;
		compactCosmeticList.LinkListProvider(compactOfferList);

		sacContent.Visible = GameAccount.ActiveAccount.isOwned;
		ProcessPriority = 2;
		FetchShop();
		RefreshTimerController.OnHourChanged += FetchShop;
		AppConfig.OnConfigChanged += OnConfigChanged;
		GameAccount.ActiveAccountChanged += OnActiveAccountChanged;
		UpdateSAC().StartTask();
	}

	public override void _ExitTree()
	{
		instance = null;
		RefreshTimerController.OnHourChanged -= FetchShop;
		AppConfig.OnConfigChanged -= OnConfigChanged;
		GameAccount.ActiveAccountChanged -= OnActiveAccountChanged;
	}

	public static void GoToTab()
	{
		instance?.Visible = true;
		instance?.GetParent<Control>()?.Visible = true;
	}

	public bool IsCompact => AppConfig.Get("item_shop", "simple_cosmetics", false);
	public bool NavVisible => AppConfig.Get("item_shop", "navigation_visible", false);

	bool includeCompactUnfiltered = true;

	private void OnConfigChanged(string section, string key, JsonNode value)
	{
		if (section != "item_shop")
			return;
		if (key == "simple_cosmetics")
		{
			var compact = IsCompact;
			shopSectionRoot.Visible = !compact;
			compactCosmeticList.Visible = compact;
			navToggle.Visible = !compact;
			navContainer.Visible = compact ? false : NavVisible;
		}
		if (key == "navigation_visible")
		{
			if (IsCompact)
				return;
			navContainer.Visible = NavVisible;
		}
	}


	private async void OnActiveAccountChanged() => await UpdateSAC();
	private async Task UpdateSAC()
	{
		if (!GameAccount.ActiveAccount.isOwned)
			return;
		string currentSACCode = await GameAccount.ActiveAccount.GetSACCode(false);
		if (currentSACCode != "None" && await GameAccount.ActiveAccount.GetSACTime() > 1 && AppConfig.Get("automation", "creatorcode", false))
		{
			//GD.Print(currentSACCode);
			await GameAccount.ActiveAccount.SetSACCode(currentSACCode);
		}
		sacButton.Text = currentSACCode;
	}

	async void OpenSACPrompt()
	{
		string subtext = GD.Randf() > 0.85f ?
			"By enabling the \"Auto-apply creator code\" setting, PegLeg can automatically refresh the duration of your selected code when you load the shop!" :
			"Whoever you choose to support will recieve 5% of the cost of any Real-Money or VBuck purchases you make";
		var newCode = await GenericLineEditWindow.ShowLineEdit("Support A Creator!", subtext, sacButton.Text, "Who do you want to support?");
		if (newCode is null)
			return;

		using var _ = LoadingOverlay.CreateToken();

		await GameAccount.ActiveAccount.SetSACCode(newCode);
		sacButton.Text = await GameAccount.ActiveAccount.IsSACExpired() ? "None" : (await GameAccount.ActiveAccount.GetSACCode());
	}

	public bool FilterOffer(GameOffer offer)
	{
		if (offer.CosmeticLayoutId == "alc.0")
			return false;//legally obligated un-bundled purchases

		var timeData = offer.CosmeticTimeData;
		if (requireNew && !timeData.isRecentlyNew)
			return false;
		if (requireToday && !timeData.isAddedToday)
			return false;
		if (requireLeaving && !timeData.isLeavingSoon)
			return false;
		if (requireOld && timeData.lastSeenDaysAgo < 500)
			return false;

		if(activeTypeFilters.Count > 0 || unknownTypeFilterActive)//perform type check if either are active
		{
			var typeList = offer.itemGrants.Select(i => i.templateId.Split(':')[0]).Distinct();
			bool failTypeCheck = true;
			if (typeList.Any(activeTypeFilters.Contains))//pass if any types are in active filters
				failTypeCheck = false;
			if (unknownTypeFilterActive && !typeList.All(allFilterTypes.Contains))//pass if any types are unknown
				failTypeCheck = false;
			if (failTypeCheck)
				return false;
		}

		return true;
	}

	CosmeticSectionGroup[] sectionGroups = [];
	CosmeticSectionGroup[] filteredSectionGroups = [];
	float sectionOrigin;
	float[] sectionHeights = [];

	EntryList<GameOffer> compactOfferList = [];

	async void FetchShop()
	{
		buffering.Visible = true;
		shopViewport.Visible = false;
		navTree.Clear();
		await GameStorefront.FetchCosmeticDependancies();
		sectionGroups = GetGroupedOffers();

		buffering.Visible = false;
		shopViewport.Visible = true;

		FilterShop();
	}

	public static CosmeticSectionGroup[] GetGroupedOffers()
	{
		GameOffer[] offers = [.. GameStorefront.CosmeticDaily.Offers, .. GameStorefront.CosmeticWeekly.Offers];
		var sections = GameStorefront.cosmeticSectionsCache;
		return
		[.. offers.GroupBy(o => o.CosmeticSectionId).Select(sectionGroup =>
		{
			GameStorefront.CosmeticSectionData? sectionData = sections.TryGetValue(sectionGroup.Key, out var section) ? section : null;
			var sectionRowDict = sectionData?.metadata.offerGroups.DistinctBy(r=>r.offerGroupId).ToDictionary(r => r.offerGroupId) ?? [];
			var rowGroups = sectionGroup.GroupBy(o => o.CosmeticRowGroupId).Select(rowGroup => new CosmeticRowGroup()
			{
				offerGroupId = rowGroup.Key,
				offers = [.. rowGroup.OrderByDescending(o => o.SortPriority)],
				rowData = sectionRowDict.TryGetValue(rowGroup.Key, out var rowData) ? rowData : null
			}).OrderByDescending(s => s.Rank);
			return new CosmeticSectionGroup()
			{
				sectionId = sectionGroup.Key,
				rows = [.. rowGroups],
				sectionData = sectionData
			};
		}).OrderByDescending(s => s.Rank)];
	}

	static readonly HashSet<string>[] filterTypes =
	[
		[
			"AthenaCharacter",
			"AthenaBackpack"
		],
		[
			"CosmeticShoes"
		],
		[
			"AthenaPickaxe"
		],
		[
			"AthenaGlider",
			"AthenaSkyDiveContrail",
		],
		[
			"AthenaDance",
			"AthenaSpray",
		],
		[
			"AthenaItemWrap",
		],
		[
			"AthenaMusicPack",
			"SparksSong",
		],
		[
			"SparksGuitar",
			"SparksMic",
			"SparksDrum",//?
			"SparksDrums",
			"SparksBass",
			"SparksKeyboard",
		],
		[
			"VehicleCosmetics_Wheel",
			"VehicleCosmetics_Wheel",
			"VehicleCosmetics_Body",
			"VehicleCosmetics_Body",
			"VehicleCosmetics_Skin",
			"VehicleCosmetics_Skin",
			"VehicleCosmetics_Booster",
			"VehicleCosmetics_Booster",
			"VehicleCosmetics_DriftTrail",
		],
		[
			"CosmeticMimosa",
			"CosmeticCompanion",
			"Sidekick",
		],
		[
			"UnknownLegoType",
			"UnknownLegoType",
			"UnknownLegoType",
		],
	];
	static readonly HashSet<string> allFilterTypes = [.. filterTypes.SelectMany(f => f)];
	HashSet<string> activeTypeFilters = [];
	bool unknownTypeFilterActive = false;
	bool requireNew;
	bool requireToday;
	bool requireLeaving;
	bool requireOld;

	void FilterShop()
	{
		activeTypeFilters = [.. typeFilters.PressedTabIndexes.SelectMany(i => filterTypes.Length > i && i >= 0 ? filterTypes[i] : [])];
		unknownTypeFilterActive = typeFilters.PressedTabs.Any(t => t.Metadata == "Unknown");
		var timeFilterIndexes = timeFilters.PressedTabIndexes.ToHashSet();
		requireNew = timeFilterIndexes.Contains(0);
		requireToday = timeFilterIndexes.Contains(1);
		requireLeaving = timeFilterIndexes.Contains(2);
		requireOld = timeFilterIndexes.Contains(3);
		//GD.Print($"Active Filters: [{string.Join(", ", activeTypeFilters)}]");

		string focusSection = null;
		float focusOffset = 0;
		if (!IsCompact && activeSectionEntries.Count>0)
		{
			var orderedActiveSections = activeSectionEntries.Values.OrderBy(v => v.Position.Y).ToArray();
			var viewportRect = shopViewport.GetGlobalRect();
			var viewportCenter = viewportRect.GetCenter().Y;

			foreach (var entry in activeSectionEntries)
			{
				var sectionRect = entry.Value.GetGlobalRect();
				if (sectionRect.Position.Y < viewportRect.Position.Y && sectionRect.End.Y > viewportCenter)
				{
					//start is offscreen, end is at least halfway into viewport
					focusSection = entry.Key;
					break;
				}
				if (sectionRect.Position.Y > viewportRect.Position.Y)
				{
					//start is onscreen (and implicitly in the top half of the viewport)
					focusSection = entry.Key;
					break;
				}
			}
			focusSection ??= activeSectionEntries.Keys.FirstOrDefault();

			var focusRect = activeSectionEntries[focusSection].GetGlobalRect();
			focusOffset = viewportRect.Position.Y - focusRect.Position.Y;
		}

		filteredSectionGroups = [
			..sectionGroups
			.Select(s => s with {
				rows = [
					..s.rows
					//.Select(r => r with {
					//	offers = [..r.offers.Where(FilterOffer)]
					//})
					//.Where(r => r.offers.Length > 0)
					.Where(r => r.offers.Any(FilterOffer))
				]
			})
			.Where(s => s.rows.Length > 0)
		];
		sectionHeights = [..filteredSectionGroups.Select(s => baseSectionHeight + (s.rows.Length * sectionHeightPerRow))];
		shopSectionRoot.CustomMinimumSize = new(shopSectionRoot.CustomMinimumSize.X, sectionHeights.Sum());

		//re-pool all sections and their offers
		foreach (var key in activeSectionEntries.Keys.ToArray())
		{
			var entry = activeSectionEntries[key];
			entry.SetOfferSection(null);
			entry.Visible = false;
			sectionEntryPool.Enqueue(entry);
		}
		activeSectionEntries.Clear();

		if(focusSection is not null)
		{
			var target = filteredSectionGroups.FirstOrDefault(d => d.sectionId == focusSection);
			if(target.sectionId is not null)
			{
				sectionOrigin = shopSectionRoot.GlobalPosition.Y - (shopViewport.GetChildOrNull<Control>(0)?.GlobalPosition.Y ?? 0);
				var idx = Array.IndexOf(filteredSectionGroups, target);
				//inaccurate, this should be relative to viewport child, currently relative to section parent
				var startHeight = sectionOrigin + sectionHeights[..idx].Sum();
				shopViewport.ScrollVertical = (int)(startHeight + focusOffset);
				//do this thrice, as it may take a frame or two for the maximum scroll range to catch up
				Helpers.Defer(() => shopViewport.ScrollVertical = (int)(startHeight + focusOffset), 1);
				Helpers.Defer(() => shopViewport.ScrollVertical = (int)(startHeight + focusOffset), 2);
			}
		}

		//populate sidebar with sections
		//var sidebarGroups = filteredSectionGroups.GroupBy(g => g.sectionData?.category);
		//var ungrouped = sidebarGroups.FirstOrDefault(cg => cg.Key == null).ToArray();
		navTree.Clear();
		lastCategory = null;
		treeRoot = navTree.CreateItem();
		float navCurrentHeight = 0;
		Dictionary<string, TreeItem> categories = [];
		for (int i = 0; i < filteredSectionGroups.Length; i++)
		{
			var parent = treeRoot;
			var section = filteredSectionGroups[i];
			if (section.sectionData?.category is string cat)
			{
				if(!categories.TryGetValue(cat, out var catItem))
				{
					catItem = treeRoot.CreateChild();
					catItem.SetText(0, " "+cat);
					catItem.SetMetadata(0, navCurrentHeight);
					catItem.Collapsed = true;
					categories.Add(cat, catItem);
				}
				parent = catItem;
			}
			var secItem = parent.CreateChild();
			secItem.SetText(0, " "+(section.sectionData?.displayName ?? section.sectionId));
			secItem.SetMetadata(0, navCurrentHeight);
			navCurrentHeight += sectionHeights[i];
		}

		compactOfferList.Clear();
		compactOfferList.AddRange(filteredSectionGroups.SelectMany(s => s.rows.SelectMany(r => r.offers.Where(FilterOffer))));
		compactOfferList.AddRange(
			sectionGroups
				.SelectMany(s => s.rows.SelectMany(r => r.offers))
				.Except(compactOfferList)
				.Where(o => o.CosmeticLayoutId != "alc.0")
		);
		compactCosmeticList.MarkListDirty();


		MarkListDirty();
	}

	TreeItem treeRoot = null;
	TreeItem lastCategory = null;
	private void OnNavSelected()
	{
		TreeItem secItem = navTree.GetSelected();

		TreeItem category = secItem.GetParent();
		if (category == treeRoot)
			category = null;
		if (secItem.GetChildCount() > 0)
			category = secItem;

		if (category != lastCategory)
		{
			category?.Collapsed = false;
			lastCategory?.Collapsed = true;
			lastCategory = category;
		}

		var height = (float)secItem.GetMetadata(0);
		var scrollTween = GetTree().CreateTween().SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Expo);
		scrollTween.TweenProperty(shopViewport, "scroll_vertical", height, 0.3f);
	}

	public override void _Process(double delta)
	{
		CheckSections();
	}

	bool listDirty = false;
	public void MarkListDirty() => listDirty = true;

	Vector2 prevRelativePos;
	private void ForceCheckSections() => CheckSections(true);
	private void CheckSections() => CheckSections(false);
	private void CheckSections(bool force)
	{
		//return if in compact mode
		if (AppConfig.Get("item_shop", "simple_cosmetics", false))
			return;

		Vector2 relativePos = shopSectionRoot.GlobalPosition - shopViewport.GlobalPosition;
		if (!force)
		{
			if (!IsVisibleInTree())
				return;
			if (!listDirty && Mathf.Abs(relativePos.Y - prevRelativePos.Y) < extendViewportBounds.Y/2)
				return;
			//GD.Print($"Refresh Shop (listDirty:{listDirty}) (abs of {relativePos.Y}-{prevRelativePos.Y} is {relativePos.Y - prevRelativePos.Y}, greater than {extendViewportBounds.Y / 2})");
		}
		prevRelativePos = relativePos;
		listDirty = false;

		var viewportRect = shopViewport.GetGlobalRect();
		var sectionParentRect = shopSectionRoot.GetGlobalRect();

		NewRecycleListContainer.ScaleRects(ref sectionParentRect, ref viewportRect, shopSectionRoot.Size);
		Rect2 relativeVisibleRect = NewRecycleListContainer.GetRelativeRect(sectionParentRect, viewportRect, extendViewportBounds);
		var visibleStart = relativeVisibleRect.Position.Y;
		var visibleEnd = visibleStart + relativeVisibleRect.Size.Y;

		float progress = 0;
		List<string> activeSectionIDs = [];
		for (int i = 0; i < filteredSectionGroups.Length; i++)
		{
			float currentSectionStart = progress;
			float currentSectionEnd = currentSectionStart + sectionHeights[i];
			progress += sectionHeights[i];

			if (currentSectionStart > visibleEnd)
				break;//all future sections will be beyond here anyway
			if (currentSectionEnd < visibleStart)
				continue;

			var section = filteredSectionGroups[i];
			activeSectionIDs.Add(section.sectionId);

			if (activeSectionEntries.ContainsKey(section.sectionId))
				continue;

			if (!sectionEntryPool.TryDequeue(out var sectionEntry))
			{
				sectionEntry = shopSectionScene.Instantiate<CosmeticShopSection>();
				sectionEntry.SetPool(ref offerEntryPool);
				shopSectionRoot.AddChild(sectionEntry);
			}

			sectionEntry.Visible = true;
			sectionEntry.Position = new(0, currentSectionStart);
			sectionEntry.SetOfferSection(section);
			activeSectionEntries.Add(section.sectionId, sectionEntry);
		}
		var sectionIdsToRemove = activeSectionEntries.Keys.Except(activeSectionIDs).ToArray();
		foreach (var item in sectionIdsToRemove)
		{
			//re-pool offers
			var sectionEntry = activeSectionEntries[item];
			sectionEntry.Visible = false;
			sectionEntry.SetOfferSection(null);
			activeSectionEntries.Remove(item);
			sectionEntryPool.Enqueue(sectionEntry);
		}
	}
}

public record struct CosmeticSectionGroup
{
	public string sectionId { get; init; }
	public CosmeticRowGroup[] rows { get; init; }
	public GameStorefront.CosmeticSectionData? sectionData { get; init; }
	public int Rank => sectionData?.RankValue ?? 0;
}

public record struct CosmeticRowGroup
{
	public string offerGroupId { get; init; }
	public GameOffer[] offers { get; init; }
	public GameStorefront.CosmeticSectionData.Row? rowData { get; init; }
	public int Rank => rowData?.RankValue ?? (int.TryParse(offerGroupId, out var subrank) ? subrank : 0);
}