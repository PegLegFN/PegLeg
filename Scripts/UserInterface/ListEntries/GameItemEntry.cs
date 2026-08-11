using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using XmppDotNet.Xmpp.Jingle.Apps.Rtp;

public partial class GameItemEntry : Control, IRecyclableEntry, IListEntry<GameItem>
{
	[Signal]
	public delegate void ItemDoesExistEventHandler(bool value);

	[Signal]
	public delegate void ItemDoesNotExistEventHandler(bool value);

	[Signal]
	public delegate void NameRelevantEventHandler(bool value);

	[Signal]
	public delegate void NameChangedEventHandler(string name);

	[Signal]
	public delegate void DescriptionChangedEventHandler(string description);

	[Signal]
	public delegate void TooltipChangedEventHandler(string tooltip);

	[Signal]
	public delegate void IconChangedEventHandler(Texture2D icon);

	[Signal]
	public delegate void IconFitEventHandler(bool value);

	[Signal]
	public delegate void TypeChangedEventHandler(string type);

	[Signal]
	public delegate void SubtypeIconChangedEventHandler(Texture2D icon);

	[Signal]
	public delegate void PackIconChangedEventHandler(Texture2D icon);

	[Signal]
	public delegate void AmmoIconChangedEventHandler(Texture2D icon); //also used for trap subtype

	[Signal]
	public delegate void PersonalityIconChangedEventHandler(Texture2D icon);

	[Signal]
	public delegate void SurvivorBoostIconChangedEventHandler(Texture2D icon); //squad synergy for leads, set bonus for non-leads

	[Signal]
	public delegate void WeaponCoreIconChangedEventHandler(Texture2D icon);

	[Signal]
	public delegate void IsCollectableEventHandler(bool collectable);

	[Signal]
	public delegate void CanBeLeveledChangedEventHandler(bool canBeLeveled);

	[Signal]
	public delegate void LevelTextChangedEventHandler(string level);

	[Signal]
	public delegate void InitLevelTextChangedEventHandler(string level);

	[Signal]
	public delegate void LevelChangedEventHandler(float level);

	[Signal]
	public delegate void InitLevelChangedEventHandler(float level);

	[Signal]
	public delegate void UncappedLevelTextChangedEventHandler(string level);

	[Signal]
	public delegate void LevelMaxChangedEventHandler(float levelMax);

	[Signal]
	public delegate void LevelProgressChangedEventHandler(float levelProgress);

	[Signal]
	public delegate void InitLevelProgressChangedEventHandler(float levelProgress);

	[Signal]
	public delegate void RatingChangedEventHandler(string rating);

	[Signal]
	public delegate void RatingVisibilityEventHandler(bool visibility);

	[Signal]
	public delegate void AmountChangedEventHandler(string amountText);

	[Signal]
	public delegate void AmountVisibilityEventHandler(bool visibility);

	[Signal]
	public delegate void NotificationChangedEventHandler(bool isNotificationVisible);

	[Signal]
	public delegate void BookmarkChangedEventHandler(bool isBookmarkVisible);

	[Signal]
	public delegate void FavoriteChangedEventHandler(bool isFavoriteVisible);

	[Signal]
	public delegate void InteractableChangedEventHandler(bool interactable);

	[Signal]
	public delegate void RarityChangedEventHandler(Color rarityColour);

	[Signal]
	public delegate void InitRarityChangedEventHandler(Color rarityColour);

	[Signal]
	public delegate void MaxTierChangedEventHandler(int maxTier);

	[Signal]
	public delegate void TierChangedEventHandler(int tier);

	[Signal]
	public delegate void InitTierChangedEventHandler(int tier);

	[Signal]
	public delegate void InitVisibleEventHandler(bool visible);
	[Signal]

	public delegate void UncappedVisibleEventHandler(bool visible);

	[Signal]
	public delegate void SuperchargeChangedEventHandler(int supercharge);

	[Signal]
	public delegate void SelectionVisibleChangedEventHandler(Texture2D marker);

	[Signal]
	public delegate void SelectionMarkerChangedEventHandler(Texture2D marker);

	[Signal]
	public delegate void SelectionQuantityChangedEventHandler(string quantity);

	[Signal]
	public delegate void SelectionTintChangedEventHandler(Color rarityColour);

	[Signal]
	public delegate void OverflowWarningEventHandler(bool value);

	[Signal]
	public delegate void DurabilityVisibleEventHandler(bool value);

	[Signal]
	public delegate void DurabilityValueEventHandler(float value);

	[Signal]
	public delegate void IsHeroEventHandler(bool value);

	[Signal]
	public delegate void IsSchematicEventHandler(bool value);

	[Signal]
	public delegate void IsPackEventHandler(bool value);

	[Signal]
	public delegate void PressedEventHandler();

	[Export]
	public bool addXToAmount;
	[Export]
	public bool compactifyAmount;
	[Export]
	public bool useLargePreview;
	[Export]
	public bool includeDescriptionInTooltip = false;
	[Export]
	public bool preventInteractability;
	[Export]
	public bool forceInteractability;
	[Export]
	public bool allowInspectWhenUninteractable = true;
	[Export]
	public bool interactableWhenEmpty;
	[Export]
	public bool autoLinkToViewer = true;
	[Export]
	public bool showSingleItemAmount = false;
	[Export]
	public bool showZeroItemAmount = false;
	[Export]
	public bool autoLinkToRecycleSelection = false;
	[Export]
	public bool autoSelectOnPress = true;
	[Export]
	public bool unlinkOnInvalidHandle = true;
	[Export]
	public bool useSquadForRating;
	[Export]
	public bool hideMythicLeadSquad = false;
	[Export]
	public bool updateRewardNotification = true;
	[Export]
	public bool forceShowVBucks = false;
	[Export]
	public bool packImageAsSubtype = true;
	[Export]
	public bool ignoreLevelCap = false;
	[Export]
	public bool ignoreCollectable = false;
	[Export]
	public bool defaultClearIconToNull;
	[Export]
	string placeholderItem;
	[Export(PropertyHint.MultilineText)]
	string levelTextPrefix = "Lv ";
	[Export]
	protected CheckButton selectionGraphics;

	protected static Texture2D missingIcon = ResourceLoader.Load<Texture2D>("res://Images/InterfaceIcons/T_UI_VKConnectionIndicator_Error_Icon.png");
	protected static Color missingRarityColor = new(Colors.DarkRed * 0.2f, 1);

	bool ForceInteractability => forceInteractability;

	public override void _Ready()
	{
		if (autoLinkToViewer)
			Pressed += Inspect;
		if (autoLinkToRecycleSelection)
			Pressed += PerformRecycleSelection;
		ClearItem();
		EmitSignal(SignalName.InteractableChanged, interactableWhenEmpty);
		AppConfig.OnConfigChanged += OnConfigChanged;
		GameAccount.RemindersChanged += UpdateBookmark;
		GameAccount.ActiveAccountChanged += UpdateItem;
		if (!string.IsNullOrWhiteSpace(placeholderItem))
		{
			SetItem(GameItemTemplate.Get(placeholderItem)?.CreateInstance());
		}
	}

	private void OnConfigChanged(string section, string key, JsonNode value)
	{
		if (section == "missions" && key == "showLiteVbucks" && currentItem?.templateId == "AccountResource:currency_mtxswap")
			UpdateItem();
		if (section == "ui" && key == "hero_tooltips")
			SetTooltip();
		//SetInteractable();
	}

	private void UpdateBookmark()
	{
		if (currentItem is not null)
			EmitSignalBookmarkChanged(GameAccount.ActiveAccount.HasReminder(currentItem.template));
	}

	public override void _ExitTree()
	{
		GameAccount.ActiveAccountChanged -= UpdateItem;
		GameAccount.RemindersChanged -= UpdateBookmark;
		AppConfig.OnConfigChanged -= OnConfigChanged;
		if (currentItem is not null)
		{
			currentItem.OnChanged -= UpdateItem;
			currentItem.OnRemoved -= RemoveItem;
		}
	}

	public GameItem currentItem { get; protected set; }
	public GameItem displayItem { get; protected set; }
	protected GameItem inspectorOverride;

	public void SetItem(GameItem newItem, bool forceUpdate = false)
	{
		if (newItem == currentItem)
		{
			if (forceUpdate)
				UpdateItem(currentItem);
			return;
		}

		if (currentItem is not null)
		{
			currentItem.OnChanged -= UpdateItem;
			currentItem.OnRemoved -= RemoveItem;
		}

		currentItem = newItem;

		if (currentItem is not null)
		{
			currentItem.OnChanged += UpdateItem;
			currentItem.OnRemoved += RemoveItem;
			UpdateItem(currentItem);
		}
		else
			ClearItem();
	}

	public void UpdateItem() => UpdateItem(currentItem);

	protected virtual void UpdateItem(GameItem updatedItem)
	{
		if (!IsInstanceValid(this) || !IsInsideTree())
			return;
		displayItem = updatedItem;

		if (displayItem is null || displayItem.customData?["empty"] is not null)
		{
			ClearItem();
			return;
		}

		int amount = displayItem.quantity;

		if (
			!forceShowVBucks &&
			displayItem.templateId == "AccountResource:currency_mtxswap" &&
			(
				(
					GameAccount.ActiveAccount.isOwned &&
					GameAccount.ActiveAccount
						.GetProfile(FnProfileTypes.AccountItems)
						.GetFirstTemplateItem("Token:receivemtxcurrency") is null
				)||
				(
					!GameAccount.ActiveAccount.isOwned && 
					!AppConfig.Get("missions", "showLiteVbucks", true)
				)
			)
		)
		{
			displayItem = GameItemTemplate.Get("AccountResource:currency_xrayllama").CreateInstance(amount);
		}

		if (displayItem.templateId == "AccountResource:currency_mtxswap")
			displayItem = GameItemTemplate.Get("AccountResource:currency_hybrid_mtx_xrayllama").CreateInstance(amount);
		if (displayItem.templateId== "Currency:mtxpurchased")
			displayItem = GameItemTemplate.Get("Token:receivemtxcurrency").CreateInstance(amount);
		//substitute generic event tickets for current event tickets

		inspectorOverride = displayItem.inspectorOverride;
		if (inspectorOverride is not null && inspectorOverride.template is not null)
			displayItem = inspectorOverride;


		if (displayItem.template?.Type == "Accolades")
			amount = displayItem.template?["AccoladeXP"]?.GetValue<int>() ?? 1;
		string amountText = compactifyAmount ? amount.Compactify() : amount.Notate();

		if (addXToAmount)
			amountText = "x" + amountText;
		if (amount <= (showSingleItemAmount ? (showZeroItemAmount ? -1 : 0) : 1))
			amountText = "";
		bool amountNeeded = amountText != "";

		string name = displayItem.template?.DisplayName ?? displayItem.templateId?.Split(":")[1];
		string displayType = displayItem.template?.Type;
		Texture2D mainIcon = displayItem.GetTexture(missingIcon, useLargePreview);

		if (displayType == "Worker")
			displayType = "Survivor";

		string overrideSurvivorSquad = selector is SimpleItemSelector gameItemSelector ? gameItemSelector.OverriddeSurvivorSquad : null;
		float rating = displayItem.CalculateSurvivorRating(
			useSquadForRating || overrideSurvivorSquad is not null,
			overrideSurvivorSquad
		);

		bool invalidRating = displayItem.RatingOutOfRange;
		EmitSignalRatingChanged(rating == 0 ? "" : (invalidRating ? "???" : rating.ToString()));
		EmitSignalRatingVisibility(rating != 0);

		int tier = displayItem.template?.Tier ?? 0;
		int level = Mathf.Max(displayItem.Level, 1);
		if (displayItem.DesiredLevel > 0)
		{
			(level, tier, bool capped) = displayItem.ResolveDesiredLevelAndTier(ignoreLevelCap: ignoreLevelCap);
			EmitSignalUncappedVisible(capped);
			EmitSignalUncappedLevelTextChanged($"{levelTextPrefix}{displayItem.DesiredLevel}");
		}
		else
		{
			EmitSignalUncappedVisible(false);
			EmitSignalUncappedLevelTextChanged("");
		}

		int bonusMaxLevel = displayItem.attributes?["max_level_bonus"]?.GetValue<int>() ?? 0;
		int maxLevel = Mathf.Clamp(tier * 10, 1, 50) + bonusMaxLevel;
		int minLevel = Mathf.Max(maxLevel - 10, 1);
		float levelProgress = minLevel == maxLevel ? 1 : ((float)level - minLevel) / (maxLevel - minLevel);

		int initLevel = displayItem.attributes?["starting_level"]?.GetValue<int>() ?? 0;
		int initTier = displayItem.attributes?["starting_tier"]?.GetValue<string>() switch
		{
			"v" => 5,
			"iv" => 4,
			"iii" => 3,
			"ii" => 2,
			"i" => 1,
			_ => 0
		};
		int initRarityLevel = displayItem.attributes?["starting_rarity"]?.GetValue<string>() switch
		{
			"Mythic" => 6,
			"Legendary" => 5,
			"Epic" => 4,
			"Rare" => 3,
			"Uncommon" => 2,
			"Common" => 1,
			_ => 0
		};

		bool useInit =
			(initLevel > 1 && initLevel < level) ||
			(initTier > 1 && initTier < tier) ||
			(initRarityLevel > 1 && initRarityLevel < (displayItem?.template?.RarityLevel ?? 1));
		EmitSignalInitVisible(useInit);
		if (useInit)
		{
			EmitSignalInitLevelChanged(initLevel);
			EmitSignalInitTierChanged(initTier);
			EmitSignalInitRarityChanged(initRarityLevel <= 0 || initRarityLevel > 6 ? Colors.Transparent : PaletteHelper.RarityColours[initRarityLevel - 1]);
			EmitSignalInitLevelTextChanged($"{levelTextPrefix}{level}");
			EmitSignalInitLevelProgressChanged(((initLevel + 9 % 10) + 1) / 10f);
		}

		if (displayType == "AccountResource" || displayType == "ConsumableAccountItem")
		{
			//type = Regex.Replace(type, "([A-Z])", " $1");
			displayType = name;
		}

		if (displayType != "Survivor")
		{
			EmitSignalPersonalityIconChanged(null);
			EmitSignalSurvivorBoostIconChanged(null);
		}

		EmitSignalItemDoesExist(true);
		EmitSignalItemDoesNotExist(false);

		EmitSignalNameChanged(name);
		EmitSignalDescriptionChanged(DisplayDescription);
		EmitSignalTypeChanged(displayType ?? displayItem.templateId?.Split(":")[0]);
		EmitSignalRarityChanged(displayItem.template?.RarityColor ?? missingRarityColor);

		SetTooltip();

		var subtypeIcon = displayItem.template?.GetSubtypeTexture();
		var packIcon = displayItem.GetTexture(FnItemTextureType.PackImage, null);

		if (packImageAsSubtype && displayType == "CardPack")
			subtypeIcon = packIcon;

		EmitSignalIconChanged(mainIcon ?? missingIcon);
		EmitSignalIconFit(!(displayType == "Hero" || displayType == "Survivor" || displayType == "Defender"));
		EmitSignalSubtypeIconChanged(subtypeIcon);
		EmitSignalPackIconChanged(packIcon);
		EmitSignalAmmoIconChanged(displayItem.template?.GetAmmoTexture());
		EmitSignalWeaponCoreIconChanged(displayItem.template?.GetWeaponCoreTexture());

		//bool lowQuality = OS.HasFeature("mobile") && AppConfig.Get("ui", "mobile_performance_mode", true);
		bool lowQuality = false;
		EmitSignalIsPack(displayType == "CardPack");
		EmitSignalIsSchematic(displayType == "Schematic" && !lowQuality);
		EmitSignalIsHero(displayType == "Hero" && !lowQuality);

		EmitSignalAmountVisibility(amountNeeded);
		EmitSignalAmountChanged(amountText);
		if (displayType == "Weapon" && displayItem.template?.Category == "Ranged" && displayItem.attributes?["loadedAmmo"]?.GetValue<int>() is int loadedAmmo)
		{
			int maxAmmo = displayItem.template?["RangedWeaponStats"]?["Reload"]?["ClipSize"]?.GetValue<int>() ?? 0;
			if (maxAmmo != 0)
			{
				EmitSignalAmountVisibility(true);
				EmitSignalAmountChanged($"{loadedAmmo}/{maxAmmo}");
			}
		}

		bool hasDurability = displayItem.template?.Type == "Weapon" && displayItem.attributes?["durability"] is not null;
		EmitSignalDurabilityVisible(hasDurability);
		if (hasDurability)
		{
			var stats = displayItem.template["MeleeWeaponStats"] ?? displayItem.template["RangedWeaponStats"] ?? displayItem.template["TrapStats"];
			var maxDura = stats?["Durability"].GetValue<float>() ?? 1;
			var currentDura = displayItem.attributes?["durability"]?.GetValue<float>() ?? maxDura;
			EmitSignalDurabilityValue(currentDura / maxDura);
		}
		
		EmitSignalIsCollectable(!(displayItem.IsCollected() ?? true) && !ignoreCollectable);
		EmitSignalCanBeLeveledChanged(displayItem.template?.HasLevel == true);
		EmitSignalLevelTextChanged($"{levelTextPrefix}{level}");
		EmitSignalLevelChanged(level);
		EmitSignalLevelMaxChanged(maxLevel);
		EmitSignalLevelProgressChanged(levelProgress);

		SetInteractable(DefaultInteractable(displayItem));

		//if survivor, set personality icons

		if (displayType == "Survivor")
		{
			EmitSignalPersonalityIconChanged(displayItem.GetTexture(FnItemTextureType.Personality, null));
			if (!hideMythicLeadSquad || displayItem.template?.RarityLevel != 6 || displayItem?.attributes?["portrait"] is not null)
				EmitSignalSurvivorBoostIconChanged(displayItem.GetTexture(FnItemTextureType.SetBonus, null));
		}

		//var rarity = itemInstance.GetTemplate().GetItemRarity();
		//if (!(data.rarity < 7 && data.rarity >= 0))
		//    rarity = 0;

		EmitSignalOverflowWarning(displayItem.attributes?["inventory_overflow_date"]?.GetValueKind() == System.Text.Json.JsonValueKind.String);
		EmitSignalNotificationChanged(!displayItem.IsSeen);
		EmitSignalBookmarkChanged(GameAccount.ActiveAccount.HasReminder(displayItem.template));
		EmitSignalFavoriteChanged(displayItem.IsFavourited);
		EmitSignalMaxTierChanged(displayItem.template?.MaxTier ?? 0);
		EmitSignalTierChanged(tier);
		EmitSignalSuperchargeChanged(bonusMaxLevel / 2);
	}

	protected string DisplayDescription
	{
		get
		{
			string description = displayItem.template?.Description;
			string name = displayItem.template?.DisplayName ?? displayItem.templateId?.Split(":")[1];
			string displayType = displayItem.template?.Type;
			if (displayType == "GameplayModifier")
				description = description.Replace("\r\n", " ");
			if (displayType != "Worker" || name != "Survivor")
				return description;
			var personalityText = displayItem.Personality;
			var setBonusText = displayItem.SetBonus;

			if (personalityText is null || setBonusText is null)
				return description[..104];

			var pronoun = displayItem.attributes?["gender"]?.ToString() is string gender ? (gender == "1" ? "him" : "her") : "them";
			return description
				.Replace("{Gender}|gender(him, her)", pronoun)
				.Replace("[Worker.Personality]", personalityText)
				.Replace("[Worker.SetBonus.Buff]", setBonusText);
		}
	}

	//protected virtual string CreateTooltip(GameItem displayItem, string itemName, string itemAmount, List<string> tooltipDescriptions) =>
	//	CustomTooltip.GenerateSimpleTooltip(
	//		itemName,
	//		itemAmount,
	//		AppConfig.Get("ui", "hero_tooltips", false) ? null : [..tooltipDescriptions],
	//		(displayItem.template?.RarityColor ?? missingRarityColor).ToHtml(),
	//		abilities: AppConfig.Get("ui", "hero_tooltips", false) ? displayItem.template?.GetHeroAbilities()?.Select(a => a.TemplateId)?.ToArray() : null
	//	);

	protected virtual void SetTooltip()
	{
		if (displayItem is null)
			return;

		int amount = displayItem.quantity;
		if (displayItem.template?.Type == "Accolades")
			amount = displayItem.template?["AccoladeXP"]?.GetValue<int>() ?? 1;
		string amountText = compactifyAmount ? amount.Compactify() : amount.Notate();

		if (addXToAmount)
			amountText = "x" + amountText;
		if (amount <= (showSingleItemAmount ? (showZeroItemAmount ? -1 : 0) : 1))
			amountText = "";
		bool amountNeeded = amountText != "";

		var tooltipAmount = amountNeeded ? ((addXToAmount ? "x" : "") + amount.Notate()) : null;
		if (displayItem.template?.Type == "Ingredient" && inspectorOverride is null)
			tooltipAmount = displayItem.TotalQuantity.ToString();

		List<string> tooltipDescriptions =
		[
			DisplayDescription,
			//"Item Id: " + item.templateId,
		];
		if (displayItem.GetSearchTags() is JsonArray tagArray && tagArray.Count > 0)
			tooltipDescriptions.Add("Search Tags: " + tagArray.Select(t => t?.ToString()).Where(t => !t.StartsWith("hidetag_")).ToArray().Join(", "));

		if (displayItem.template is null)
			tooltipDescriptions[0] = "Err: Missing Template";

		bool heroTooltip = AppConfig.Get("ui", "hero_tooltips", false) && displayItem.template?.Type == "Hero";

		var tooltip = CustomTooltip.GenerateSimpleTooltip(
			displayItem.template?.DisplayName ?? displayItem.templateId?.Split(":")[1],
			tooltipAmount,
			heroTooltip ? null : [.. tooltipDescriptions],
			(displayItem.template?.RarityColor ?? missingRarityColor).ToHtml(),
			abilities: heroTooltip ? displayItem.template?.GetHeroAbilities()?.Select(a => a.TemplateId)?.ToArray() : null
		);
		EmitSignalTooltipChanged(tooltip);
	}

	void RemoveItem()
	{
		if (unlinkOnInvalidHandle)
			ClearItem();
	}

	public Control node => this;

	public Vector2 GetBasisSize() => CustomMinimumSize;

	static bool DefaultInteractable(GameItem item) => item?.template?.Type switch
	{
		null => false,
		_ when item.template?.HasLevel == true => true,
		"Schematic" or "Weapon" or "Trap" or "Hero" or "Defender" => true,
		"Worker" when item.profile?.account?.isOwned == true => true,
		"CardPack" when item.CardPackChoices is not null => true,
		_ => false
	};

	bool interactableState;
	public void SetInteractable() => SetInteractable(interactableState);
	public void SetInteractable(bool interactable)
	{
		interactableState = interactable;
		EmitSignal(SignalName.InteractableChanged, IsInteractable);
	}

	bool IsInteractable =>
		ForceInteractability ||
		(
			interactableState &&
			(
				interactableWhenEmpty ||
				currentItem is not null
			) &&
			!preventInteractability
		);

	public virtual void EmitPressedSignal()
	{
		if (selectionGraphics is not null && autoSelectOnPress)
			selectionGraphics.ButtonPressed = true;
		((IListEntry<GameItem>)this).SelectEntry();
		EmitSignal(SignalName.Pressed);
	}


	public void ClearItem() => ClearItem(defaultClearIconToNull ? null : PegLegResourceManager.defaultIcon);
	public virtual void ClearItem(Texture2D clearIcon)
	{
		if (currentItem is not null)
		{
			currentItem.OnChanged -= UpdateItem;
			currentItem.OnRemoved -= RemoveItem;
			currentItem = null;
		}
		displayItem = null;
		inspectorOverride = null;
		EmitSignalItemDoesExist(false);
		EmitSignalItemDoesNotExist(true);
		EmitSignalNameChanged("");
		EmitSignalDescriptionChanged("");
		EmitSignalTooltipChanged("");
		EmitSignalIconChanged(clearIcon);
		EmitSignalSubtypeIconChanged(clearIcon);
		EmitSignalTypeChanged("");
		EmitSignalAmountVisibility(false);
		EmitSignalRatingVisibility(false);
		EmitSignalUncappedVisible(false);
		EmitSignalAmountChanged("");
		EmitSignalRarityChanged(Colors.Transparent);
		EmitSignalInteractableChanged(interactableWhenEmpty);
		EmitSignalNotificationChanged(false);
		EmitSignalFavoriteChanged(false);
		EmitSignalOverflowWarning(false);
	}

	public void Inspect()
	{
		if (!allowInspectWhenUninteractable && !IsInteractable)
			return;
		if (currentItem is null)
			return;
		if (inspectorOverride is not null)
			GameItemViewer.Instance.ShowItem(inspectorOverride);
		else
			GameItemViewer.Instance.ShowItem(currentItem);
	}

	//public static bool TypeShouldBeInteractable(string type) => autoInteractableTypes.Contains(type.ToLower());

	protected IRecyclableElementProvider<GameItem> itemProvider;
	protected ISelectableElementProvider<GameItem> selector;
	public virtual void SetRecyclableElementProvider(IRecyclableElementProvider provider)
	{
		itemProvider = provider is IRecyclableElementProvider<GameItem> newProvider ? newProvider : null;
		selector = provider is ISelectableElementProvider<GameItem> newSelector ? newSelector : null;
	}

	protected int recycleIndex = 0;
	public virtual void SetRecycleIndex(int index)
	{
		if (itemProvider is null)
			return;
		recycleIndex = index;
		SetItem(itemProvider.GetRecycleElement(index));
		if (selector is not null)
			EmitSignal(SignalName.InteractableChanged, selector.IsSelectable(currentItem));
		UpdateSelectionVisuals();
	}

	public void PerformRecycleSelection() => PerformRecycleSelection("");
	public virtual void PerformRecycleSelection(string ctx)
	{
		if (itemProvider is null)
			return;
		itemProvider.OnElementSelected(recycleIndex, ctx);
		UpdateSelectionVisuals();
	}

	public virtual void ClearRecycleIndex() => ClearItem();

	protected virtual void UpdateSelectionVisuals()
	{
		if (selector is null || selectionGraphics is null)
			return;

		bool isSelected = selector.IsSelected(currentItem);
		bool isSelectable = selector.IsSelectable(currentItem);
		int quantity = selector.GetSelectionQuantity(currentItem);
		selectionGraphics.ButtonPressed = isSelected || !isSelectable;
		EmitSignalSelectionTintChanged(selector.GetSelectableColor(currentItem));
		EmitSignalSelectionMarkerChanged(selector.GetSelectableIcon(currentItem));
		EmitSignalSelectionQuantityChanged(quantity > 0 ? quantity.ToString() : "");
	}

	int IListEntry<GameItem>.CurrentIndexTarget { get; set; }
	IListProvider<GameItem> IListEntry<GameItem>.CurrentListProvider { get; set; }
	public void SetListEntryValue(GameItem newValue) => SetItem(newValue);
}
