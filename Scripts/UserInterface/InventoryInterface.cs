using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

public partial class AppConfig
{
	public string inspectedAccount;
}

public partial class InventoryInterface : Control, IRecyclableElementProvider<GameItem>
{
	[Export]
	RecycleListContainer itemList;
	[Export]
	LineEdit searchBox;
	[Export]
	LineEdit targetUser;
	[Export]
	Control devAllPanel;
	[Export]
	CheckButton devAllButton;
	[Export]
	VirtualTabBar tabBar;
	[Export]
	string targetProfile;
	[Export(PropertyHint.ArrayType)]
	string[] typeFilters;
	[Export]
	bool sortByName = false;
	[Export]
	bool allowDevMode = true;
	[Export]
	Control creatorImageParent;
	AnimationPlayer creatorAnimations;
	Dictionary<string, Control> creatorImages;
	[Export]
	Control inMissionIndicator;
	[Export]
	Control heavySearchWarning;
	[Export]
	HomebasePowerLevel powerLevel;
	[Export]
	Control researchTokenArea;
	[Export]
	Button researchTokenButton;
	[Export]
	string autoDismantleRules;

	Control currentCreatorImage;
	AnimationPlayer currentCreatorAnimation;

	public override void _Ready()
	{
		creatorImages = creatorImageParent.GetChildren().OfType<Control>().ToDictionary(c => c.Name.ToString(), c => (Control)c);
		creatorAnimations = creatorImageParent.GetChildren().OfType<AnimationPlayer>().FirstOrDefault();
		foreach (var item in creatorImages.Values)
		{
			item.Visible = false;
		}
		if (heavySearchWarning is not null)
			heavySearchWarning.Visible = false;
		GameAccount.ActiveAccountChanged += UpdateAccount;
		itemList.SetProvider(this);
		searchBox.TextChanged += _ => LightweithtApplyFilters();
		searchBox.TextSubmitted += _ => ApplyFilters();
		var dev = AppConfig.Get("advanced", "developer", false) && allowDevMode;
		if (targetUser is not null)
		{
			targetUser.TextSubmitted += t =>
			{
				AppConfig.Set("inventory", "customUser", t);
				UpdateAccount();
			};
			targetUser.Visible = dev;
			targetUser.Text = dev ? AppConfig.Get("inventory", "customUser", "") : "";
		}
		if (devAllPanel is not null)
			devAllPanel.Visible = dev;
		if (devAllButton is not null)
			devAllButton.Toggled += SetTypeFilter;
		if (researchTokenButton is not null)
			researchTokenButton.Pressed += ShowResearchTokenMenu;
		if (researchTokenArea is not null)
			researchTokenArea.Visible = false;

		if (!string.IsNullOrWhiteSpace(autoDismantleRules))
		{
			autoDismantleInstructions = PLSearch.GenerateSearchInstructions(autoDismantleRules);
		}

		tabBar.SetTabPressed(0);
		tabBar.TabsChanged += SetTypeFilter;
		AppConfig.OnConfigChanged += OnConfigChanged;
		RefreshTimerController.OnMinuteChanged += TryAutoDismantle;
		VisibilityChanged += TryFilter;
		SetTypeFilter();
		UpdateAccount();
	}

	PLSearch.Instruction[] autoDismantleInstructions;
	DateTime lastAutoDismantleAttempt = DateTime.MinValue;

	private async void TryAutoDismantle()
	{
		if (autoDismantleInstructions is null || !OS.HasFeature("editor"))
			return;

		if (targetProfile != FnProfileTypes.Backpack || currentProfile?.hasProfile != true || !currentProfile.account.isOwned)
			return;

		if (lastAutoDismantleAttempt.AddMinutes(10) > DateTime.UtcNow)
			return;

		await currentProfile.Query(true);
		if (currentProfile.IsLocked)
			return;

		lastAutoDismantleAttempt = DateTime.UtcNow;

		var toDismantle = currentProfile.GetItems(i => i.template?.Undismantlable == false && PLSearch.EvaluateInstructions(autoDismantleInstructions, i.RawData ?? []));

		if ((toDismantle?.Length ?? 0) <= 0)
			return;


		GD.Print($"Auto-Dismantling {toDismantle.Length} junk items from the backpack of {currentProfile.account.DisplayName}...");
		JsonObject content = new()
		{
			["targetItemIdAndQuantityPairs"] = new JsonArray(
				[.. toDismantle.Select(item => new JsonObject(){
					["itemId"] = item.uuid,
					["quantity"] = item.quantity,
				})]
			)
		};
		var res = await currentProfile.PerformOperation("DisassembleWorldItems", content, silent: true);
		if (res is not null)
			return;
		if (currentProfile.lastOp?["numericErrorCode"]?.GetValue<int>() != 12821)
		{
			GD.Print($"Unknown Dismantle Error:\n{currentProfile.lastOp}".FixNewlines());
		}
		GD.Print("Backpack locked, retrying in 1 minute");

	}

	public override void _ShortcutInput(InputEvent @event)
	{
		if
		(
			IsVisibleInTree() &&
			@event.DevTextKeybindPressed() &&
			ModalWindow.StackEmpty() &&
			currentProfile is not null
		)
		{
			DevTextOverlay.ShowText(currentProfile.statAttributes.ToString());
		}
	}

	private void OnConfigChanged(string section, string key, JsonNode val)
	{
		if (!(section == "advanced" && key == "developer") && !(section == "inventory" && key == "customUser"))
			return;

		bool dev = AppConfig.Get("advanced", "developer", false) && allowDevMode;
		if (devAllPanel is not null)
			devAllPanel.Visible = dev;
		if (targetUser is not null)
		{
			targetUser.Visible = dev;
			targetUser.Text = dev ? AppConfig.Get("inventory", "customUser", "") : "";
			if (!dev && string.IsNullOrEmpty(currentTypeFilter))
			{
				currentTypeFilter = typeFilters[0];
			}
			UpdateAccount();
		}
	}

	public override void _ExitTree()
	{
		GameAccount.ActiveAccountChanged -= UpdateAccount;
		AppConfig.OnConfigChanged -= OnConfigChanged;
		if (currentProfile is not null)
			currentProfile.OnProfileChanged -= UpdateProfile;
	}

	bool filterNew;
	public void SetNewFilter(bool value)
	{
		filterNew = value;
		ApplyFilters();
	}

	bool filterFavorite;
	public void SetFavoriteFilter(bool value)
	{
		filterFavorite = value;
		ApplyFilters();
	}

	void SetTypeFilter(bool _) => SetTypeFilter();
	void SetTypeFilter()
	{
		int index = tabBar.LatestTab;
		if (index < 0 || index >= typeFilters.Length)
			return;
		currentTypeFilter = (index == 0 && devAllButton?.ButtonPressed == true) ? "" : typeFilters[index];
		ApplyFilters();
	}

	public void ToggleSortMode() => SetSortMode(!sortByName);
	public void SetSortMode(bool sortByName)
	{
		if (sortByName == this.sortByName)
			return;
		this.sortByName = sortByName;
		ApplySorting();
	}

	GameItem[] filteredItems;
	GameItem[] currentItems;
	string currentTypeFilter = "";
	public int GetRecycleElementCount() => currentItems?.Length ?? 0;
	public GameItem GetRecycleElement(int index) => currentItems?[index];
	GameProfile currentProfile;

	async void UpdateAccount()
	{
		//accountDirty = true;
		//if (!IsVisibleInTree())
		//    return;
		//accountDirty = false;

		filteredItems = [];
		ApplySorting();
		var account = GameAccount.ActiveAccount;
		if (!string.IsNullOrEmpty(targetUser?.Text) && allowDevMode)
		{
			if (targetUser.Text.Length == 32)
				account = GameAccount.GetOrCreateAccount(targetUser.Text);
			else
				account = (await GameAccount.SearchForAccount(targetUser?.Text)) ?? account;
		}
		if (allowDevMode)
		{
			GD.Print("Inventory target: " + account?.accountId);
		}
		if (targetProfile != FnProfileTypes.AccountItems && !await account.Authenticate())
			return;

		if (researchTokenArea is not null)
			researchTokenArea.Visible = false;

		if (currentCreatorImage is not null)
		{
			currentCreatorImage.Visible = false;
		}
		creatorAnimations.Stop();
		if (creatorImages.TryGetValue(account.accountId, out var image))
		{
			currentCreatorImage = image;
			image.Visible = true;
			if (creatorAnimations.HasAnimation(account.accountId))
				creatorAnimations.Play(account.accountId);
		}

		if (currentProfile is not null)
		{
			currentProfile.OnProfileChanged -= UpdateProfile;
		}
		currentProfile = await account.GetProfile(targetProfile).Query();

		inMissionIndicator.Visible = !account.isOwned && currentProfile.statAttributes["quest_manager"]?["objectiveDeferral"] is not null;

		powerLevel?.SetAccount(account);

		if (!account.isOwned)
		{
			var custom = account.RatingData with { backpackRating = 144 };
			custom.Print("Profile Target with 144 backpack");
		}

		currentProfile.OnProfileChanged += UpdateProfile;
		UpdateProfile();

		if (currentProfile.account.isOwned)
		{
			lastAutoDismantleAttempt = DateTime.MinValue;
			TryAutoDismantle();
		}
	}

	void UpdateProfile()
	{
		if (researchTokenArea is not null)
			researchTokenArea.Visible = false;
		ApplyFilters();
		if (researchTokenArea is null || targetProfile != FnProfileTypes.AccountItems || currentProfile?.hasProfile != true || !currentProfile.account.isOwned)
			return;

		var researchToken = currentProfile.GetFirstTemplateItem("Token:campaignresearchtoken");
		researchTokenArea.Visible = researchToken is not null;
	}

	public async void BulkRecycle()
	{
		if (targetProfile != FnProfileTypes.AccountItems || currentProfile?.hasProfile != true || !currentProfile.account.isOwned)
			return;

		if (filteredItems.Length == 0)
			return;

		//foreach (var item in filteredItems)
		//{
		//    item.GetSearchTags();
		//    item.GenerateRawData();
		//}
		var config = !Input.IsKeyPressed(Key.Shift) ? SimpleItemSelector.RecycleConfig : SimpleItemSelector.RecycleConfig with
		{
			autoselectFilter = _ => true
		};
		var toRecycle = await SimpleItemSelector.OpenMultiSelector(filteredItems, config);
		if ((toRecycle?.Length ?? 0) > 0)
		{
			JsonObject content = new()
			{
				["targetItemIds"] = new JsonArray(toRecycle.Select(item => (JsonNode)item.uuid).ToArray())
			};
			using var _ = LoadingOverlay.CreateToken();
			await currentProfile.PerformOperation("RecycleItemBatch", content);
			ApplyFilters();
		}
	}

	public void BulkMarkSeen()
	{
		if (targetProfile != FnProfileTypes.AccountItems || currentProfile?.hasProfile != true)
			return;
		if (filteredItems.Length == 0)
			return;
		var unseenItems = filteredItems.Where(i => !i.IsSeen).ToArray();
		currentProfile.MarkItemsSeen(unseenItems);
	}

	public async void TestHeroSelector()
	{
		if (targetProfile != FnProfileTypes.AccountItems || currentProfile?.hasProfile != true || !currentProfile.account.isOwned)
			return;
		var heroes = currentItems.Where(i => i.templateId.StartsWith("Hero:")).ToArray();
		var selected = await HeroItemSelector.OpenSelector(heroes, HeroItemSelector.SupportConfig);
		if (selected is null)
			GD.Print("Selection Cancelled");
		else if (selected == GameItem.Empty)
			GD.Print("Empty Selected");
		else
			GD.Print($"Selected {selected?.uuid ?? "<None>"} ({selected?.templateId ?? "<None>"})");
	}

	public async void BulkDismantle()
	{
		//implement item amount selection in recycling

		if (targetProfile != FnProfileTypes.Backpack || currentProfile?.hasProfile != true || !currentProfile.account.isOwned || filteredItems.Length == 0)
			return;
		var toDismantle = await SimpleItemSelector.OpenMultiQuantitySelector(filteredItems, SimpleItemSelector.DismantleConfig);
		if ((toDismantle?.Length ?? 0) <= 0)
			return;
		lastAutoDismantleAttempt = DateTime.UtcNow;
		JsonObject content = new()
		{
			["targetItemIdAndQuantityPairs"] = new JsonArray(
				[.. toDismantle.Select(kvp => new JsonObject(){
						["itemId"] = kvp.Key.uuid,
						["quantity"] = kvp.Value,
				})]
			)
		};
		using var _ = LoadingOverlay.CreateToken();
		await currentProfile.PerformOperation("DisassembleWorldItems", content);
	}

	bool accountDirty = false;
	bool itemsDirty = false;

	async void TryFilter()
	{
		await Helpers.WaitForFrame();
		if (accountDirty)
		{
			itemsDirty = false;
			totalItemCount = null;
			UpdateAccount();
		}
		else if (itemsDirty)
		{
			ApplyFilters();
		}
	}

	int? totalItemCount;
	void LightweithtApplyFilters()
	{
		totalItemCount ??= currentProfile?.GetItems().Length;
		if ((totalItemCount ?? 0) < 3500)
			ApplyFilters();
		else if (heavySearchWarning is not null)
			heavySearchWarning.Visible = true;
	}

	static bool EvaluateTypeFilter(GameItem item, string filter)
	{
		if (filter.Contains(':'))
		{
			var splitFilter = filter.Split(':');
			return item.template?.Type == splitFilter[0] && item.template?.Category == splitFilter[1];
		}
		return item.template?.Type == filter;
	}

	bool isFiltering = false;
	async void ApplyFilters()
	{
		itemsDirty = true;
		if (isFiltering || currentProfile?.hasProfile != true || !IsVisibleInTree())
			return;
		totalItemCount = null;
		itemsDirty = false;

		if (heavySearchWarning is not null)
			heavySearchWarning.Visible = false;

		var possibleTypes =
			currentTypeFilter
			.Split(',')
			.Select(s => s.Trim())
			.Where(s => !string.IsNullOrEmpty(s));
		if (!possibleTypes.Any())
			possibleTypes = null;

		var instructions = PLSearch.GenerateSearchInstructions(searchBox.Text);
		var allItems = currentProfile.GetItems();
		totalItemCount = allItems.Length;
		currentItems = [];
		itemList.UpdateList(true);
		GameItem[] resultItems = [];
		GameItem[] FilterFunc() =>
			[.. allItems
				.Where(item =>
					(item.template is not null || AppConfig.Get("advanced", "developer", false)) &&
					(!filterNew || !item.IsSeen) &&
					(!filterFavorite || item.IsFavourited) &&
					(possibleTypes?.Any(f=>EvaluateTypeFilter(item,f)) ?? true) &&
					PLSearch.EvaluateInstructions(instructions, item.RawData)
				)
			];
		isFiltering = true;
		if ((totalItemCount ?? 0) < 3500)
			resultItems = FilterFunc();
		else
			await Task.Run(() => resultItems = FilterFunc());
		isFiltering = false;
		filteredItems = resultItems;
		ApplySorting();
	}

	public void ApplySorting()
	{
		var resultItems = filteredItems
			.OrderBy(i => i.template is null)
			.ThenBy(i => !(i.attributes?["favorite"]?.GetValue<bool>() ?? false))
			.ThenBy(i => !i.template?.HasLevel)
			.ThenBy(i => i.template?.Type);

		if (sortByName)
			resultItems = resultItems.ThenBy(i => i.template?.SortingDisplayName);

		resultItems = resultItems
			//.ThenBy(i => i.template.Category)
			.ThenBy(i => -i.Rating)
			.ThenBy(i => -i.template?.RarityLevel)
			.ThenBy(i => i.template?.Type == "Ingredient" ? -i.TotalQuantity : 1)
			.ThenBy(i => -i.quantity);

		if (!sortByName)
			resultItems = resultItems.ThenBy(i => i.template?.SortingDisplayName);


		currentItems = [.. resultItems];
		itemList.UpdateList(true);
	}

	public void OnElementSelected(int index, string context)
	{
		GameItemViewer.Instance.ShowItem(currentItems[index]);
	}

	static bool recycleNextTokenResearch = false;
	static async Task<bool> ShowResearchTokenConfirmation(KeyValuePair<GameItem, int>[] selectedItem)
	{
		if (selectedItem.Length == 0)
			return true;
		var result = await GenericConfirmationWindow.ShowConfirmation(
			$"Research \"{selectedItem[0].Key.template.DisplayName}\"?",
			"Research",
			"Recycle"
		);
		if (result is null)
			return false;
		recycleNextTokenResearch = !result.Value;
		return true;
	}

	struct ResearchItem
	{
		public string itemId;
		public string[] itemPerks;
		public int itemLevel;

		public GameItem CreateItem()
		{
			JsonObject attributes = [];
			attributes["level"] = Mathf.Max(1, itemLevel);
			if (itemPerks is not null && itemPerks.Length > 0)
			{
				attributes["alterationDefinitions"] = JsonSerializer.SerializeToNode(itemPerks);
			}
			return GameItemTemplate.Get(itemId)?.CreateInstance(attributes: attributes);
		}
	}

	public async void ShowResearchTokenMenu()
	{
		if (currentProfile.account != GameAccount.ActiveAccount)
			return;
		var researchToken = currentProfile.GetFirstTemplateItem("Token:campaignresearchtoken");
		var researchItems = researchToken.attributes["items_for_schematic_creation_data"].Deserialize<ResearchItem[]>(Helpers.JsonOptions.Fields);
		GameItem[] possibleItems = [.. researchItems.Select(r => r.CreateItem())];

		recycleNextTokenResearch = false;
		var resultItem = await SimpleItemSelector.OpenSelector(possibleItems, SimpleItemSelector.DefaultConfig with
		{
			confirmationTaskProvider = ShowResearchTokenConfirmation
		});
		var recycleThisTokenResearch = recycleNextTokenResearch;
		if (resultItem is null)
			return;
		var idx = Array.IndexOf(possibleItems, resultItem);

		//using var _ = LoadingOverlay.CreateToken();

		if (!await currentProfile.TryQuery(true))
			return;

		var notifs = await currentProfile.PerformOperation("RedeemResearchToken", $@"{{""index"":{idx}}}");

		if (!recycleThisTokenResearch || notifs is null)
			return;
		var itemAddedChange = currentProfile.lastOp["profileChanges"].AsArray().FirstOrDefault(n => n["changeType"].ToString() == "itemAdded");
		var newId = itemAddedChange["itemId"].ToString();

		await currentProfile.PerformOperation("RecycleItem", $@"{{""targetItemId"": ""{newId}""}}");
	}
}
