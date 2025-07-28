using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public partial class GameItemEntry : Control, IRecyclableEntry
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
    public delegate void AmmoIconChangedEventHandler(Texture2D icon); //also used for trap subtype

    [Signal]
    public delegate void PersonalityIconChangedEventHandler(Texture2D icon);

    [Signal]
    public delegate void SurvivorBoostIconChangedEventHandler(Texture2D icon); //squad synergy for leads, set bonus for non-leads

    [Signal]
    public delegate void IsCollectableEventHandler(bool collectable);

    [Signal]
    public delegate void CanBeLeveledChangedEventHandler(bool canBeLeveled);

    [Signal]
    public delegate void LevelChangedEventHandler(float level);

    [Signal]
    public delegate void LevelMaxChangedEventHandler(float levelMax);

    [Signal]
    public delegate void LevelProgressChangedEventHandler(float levelProgress);

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
    public delegate void MaxTierChangedEventHandler(int maxTier);

    [Signal]
    public delegate void TierChangedEventHandler(int tier);

    [Signal]
    public delegate void SuperchargeChangedEventHandler(int supercharge);

    [Signal]
    public delegate void SelectionMarkerChangedEventHandler(Texture2D marker);

    [Signal]
    public delegate void SelectionTintChangedEventHandler(Color rarityColour);
    [Signal]
    public delegate void OverflowWarningEventHandler(bool value);

    [Signal]
    public delegate void PressedEventHandler();

    [Export]
    public bool addXToAmount;
    [Export]
    public bool compactifyAmount;
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
        EmitSignal(SignalName.InteractableChanged, interactableWhenEmpty);
        AppConfig.OnConfigChanged += OnConfigChanged;
        GameAccount.BookmarksChanged += UpdateBookmark;
        GameAccount.ActiveAccountChanged += UpdateItem;
    }


    private void OnConfigChanged(string section, string key, JsonValue value)
    {
        SetInteractable();
    }

    private void UpdateBookmark()
    {
        if (currentItem is not null)
            EmitSignalBookmarkChanged(GameAccount.activeAccount.IsBookmarked(currentItem.template));
    }

    public override void _ExitTree()
    {
        GameAccount.ActiveAccountChanged -= UpdateItem;
        GameAccount.BookmarksChanged -= UpdateBookmark;
        AppConfig.OnConfigChanged -= OnConfigChanged;
        if (currentItem is not null)
        {
            currentItem.OnChanged -= UpdateItem;
            currentItem.OnRemoved -= RemoveItem;
        }
    }

    public GameItem currentItem { get; protected set; }
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

    protected virtual void UpdateItem(GameItem item)
    {
        if (!IsInstanceValid(this) || !IsInsideTree())
            return;

        if(item is null || item.customData?["empty"] is not null)
        {
            ClearItem();
            return;
        }

        if (item.template is null)
        {
            //GD.Print("uh oh");
            //GD.Print(item);
        }

        int amount = item.quantity;

        if (item.templateId== "AccountResource:currency_hybrid_mtx_xrayllama" && GameAccount.activeAccount.GetProfile(FnProfileTypes.AccountItems).GetFirstTemplateItem("Token:receivemtxcurrency") is null)
        {
            item = GameItemTemplate.Get("AccountResource:currency_xrayllama").CreateInstance(amount);
        }
        //substitute generic event tickets for current event tickets

        inspectorOverride = item.inspectorOverride;
        if (inspectorOverride is not null && inspectorOverride.template is not null)
            item = inspectorOverride;


        if (item.template?.Type == "Accolades")
            amount = item.template?["AccoladeXP"]?.GetValue<int>() ?? 1;
        string amountText = compactifyAmount ? amount.Compactify() : amount.Notate();

        if (addXToAmount)
            amountText = "x" + amountText;
        if (amount <= (showSingleItemAmount ? (showZeroItemAmount ? -1 : 0) : 1))
            amountText = "";
        bool amountNeeded = amountText != "";

        string name = item.template?.DisplayName ?? item.templateId?.Split(":")[1];
        string description = item.template?.Description;
        string type = item.template?.Type;
        Texture2D mainIcon = item.GetTexture();

        description ??= "";
        var personalityText = item.Personality;
        var setBonusText = item.SetBonus;
        if (type == "Worker" && name == "Survivor")
        {
            if(personalityText is not null && setBonusText is not null)
            {
                var pronoun = item.attributes?["gender"]?.ToString() is string gender ? (gender == "1" ? "him" : "her") : "them";
                description = description
                    .Replace("{Gender}|gender(him, her)", pronoun)
                    .Replace("[Worker.Personality]", personalityText)
                    .Replace("[Worker.SetBonus.Buff]", setBonusText);
            }
            else
            {
                //cut off text that requires personality and set bonus if we dont know what they are
                description = description[..104];
            }
        }
        //description = description.Replace(". ", ".\n");

        if (type == "Worker")
            type = "Survivor";

        float rating = item.CalculateSurvivorRating(useSquadForRating || selector?.overrideSurvivorSquad is not null, selector?.overrideSurvivorSquad);
        string ratingText = rating == 0 ? "" : rating.ToString();

        int tier = item.template?.Tier ?? 0;
        float levelProgress = 0;
        int level = item.attributes?["level"]?.GetValue<int>() ?? 1;
        int bonusMaxLevel = item.attributes?["max_level_bonus"]?.GetValue<int>() ?? 0;
        int maxLevel = Mathf.Max(tier * 10, 1) + bonusMaxLevel;
        int minLevel = Mathf.Max(maxLevel - 10, 1);
        levelProgress = minLevel == maxLevel ? 1 : (level - minLevel) / (maxLevel - minLevel);

        if (type == "AccountResource" || type == "ConsumableAccountItem")
        {
            //type = Regex.Replace(type, "([A-Z])", " $1");
            type = name;
        }

        if (type != "Survivor")
        {
            EmitSignal(SignalName.PersonalityIconChanged, (Texture2D)null);
            EmitSignal(SignalName.SurvivorBoostIconChanged, (Texture2D)null);
        }

        EmitSignal(SignalName.ItemDoesExist, true);
        EmitSignal(SignalName.ItemDoesNotExist, false);

        EmitSignal(SignalName.NameChanged, name);
        EmitSignal(SignalName.DescriptionChanged, description);
        EmitSignal(SignalName.TypeChanged, type ?? item.templateId?.Split(":")[0]);
        EmitSignal(SignalName.RarityChanged, item.template?.RarityColor ?? missingRarityColor);

        var tooltipAmount = amountNeeded ? ((addXToAmount ? "x" : "") + amount.Notate()) : null;
        if (type == "Ingredient" && inspectorOverride is null)
            tooltipAmount = item.TotalQuantity.ToString();

        List<string> tooltipDescriptions =
        [
            description ?? "",
            //"Item Id: " + item.templateId,
        ];
        if (item.GetSearchTags() is JsonArray tagArray && tagArray.Count > 0)
            tooltipDescriptions.Add("Search Tags: " + tagArray.Select(t => t?.ToString()).Except([name]).ToArray().Join(", "));

        if (item.template is null)
            tooltipDescriptions[0] = "Err: Missing Template";

        EmitSignal(
            SignalName.TooltipChanged,
            CustomTooltip.GenerateSimpleTooltip(
                name,
                tooltipAmount,
                [.. tooltipDescriptions],
                (item.template?.RarityColor ?? missingRarityColor).ToHtml()
            )
        );

        var subtypeIcon = item.template?.GetSubtypeTexture();

        if (type == "CardPack" && item.GetTexture(FnItemTextureType.PackImage, null) is Texture2D packIcon)
            subtypeIcon = packIcon;

        EmitSignal(SignalName.IconChanged, mainIcon ?? missingIcon);
        EmitSignal(SignalName.IconFit, !(type == "Hero" || type == "Survivor" || type == "Defender"));
        EmitSignal(SignalName.SubtypeIconChanged, subtypeIcon);
        EmitSignal(SignalName.AmmoIconChanged, item.template?.GetAmmoTexture());

        EmitSignal(SignalName.AmountVisibility, amountNeeded);
        EmitSignal(SignalName.AmountChanged, amountText);
        if (type == "Weapon" && item.template?.Category == "Ranged" && item.attributes?["loadedAmmo"]?.GetValue<int>() is int loadedAmmo)
        {
            int maxAmmo = item.template?["RangedWeaponStats"]?["Reload"]?["ClipSize"]?.GetValue<int>() ?? 0;
            if (maxAmmo != 0)
            {
                EmitSignal(SignalName.AmountVisibility, true);
                EmitSignal(SignalName.AmountChanged, $"{loadedAmmo}/{maxAmmo}");
            }
        }

        EmitSignal(SignalName.RatingChanged, ratingText);
        EmitSignal(SignalName.RatingVisibility, rating != 0);

        EmitSignal(SignalName.IsCollectable, !(item.isCollectedCache ?? true));
        EmitSignal(SignalName.CanBeLeveledChanged, item.template?.CanBeLeveled == true && item.template?.Type != "Weapon" && item.template?.Type != "Trap");
        EmitSignal(SignalName.LevelChanged, level);
        EmitSignal(SignalName.LevelMaxChanged, maxLevel);
        EmitSignal(SignalName.LevelProgressChanged, levelProgress);

        SetInteractable(autoInteractableTypes.Contains(item.template?.Type.ToLower()) || item.CardPackChoices is not null);

        //if survivor, set personality icons

        if (type == "Survivor")
        {
            EmitSignal(SignalName.PersonalityIconChanged, item.GetTexture(FnItemTextureType.Personality, null));
            if (!hideMythicLeadSquad || item.template?.RarityLevel != 6 || item?.attributes?["portrait"] is not null)
                EmitSignal(SignalName.SurvivorBoostIconChanged, item.GetTexture(FnItemTextureType.SetBonus, null));
        }

        //var rarity = itemInstance.GetTemplate().GetItemRarity();
        //if (!(data.rarity < 7 && data.rarity >= 0))
        //    rarity = 0;

        EmitSignal(SignalName.OverflowWarning, item.attributes?["inventory_overflow_date"]?.GetValueKind()==System.Text.Json.JsonValueKind.String);
        EmitSignal(SignalName.NotificationChanged, !item.IsSeen);
        EmitSignal(SignalName.BookmarkChanged, GameAccount.activeAccount.IsBookmarked(item.template));
        EmitSignal(SignalName.FavoriteChanged, item.IsFavourited);
        EmitSignal(SignalName.MaxTierChanged, Mathf.Min((item.template?.RarityLevel ?? 0) + 1, 5));
        EmitSignal(SignalName.TierChanged, tier);
        EmitSignal(SignalName.SuperchargeChanged, bonusMaxLevel / 2);
    }

    void RemoveItem()
    {
        if (unlinkOnInvalidHandle)
            ClearItem();
    }

    public Control node => this;

    public Vector2 GetBasisSize() => CustomMinimumSize;

    static readonly string[] autoInteractableTypes =
    [
        "schematic",
        "weapon",
        "trap",
        "hero",
        "defender"
    ];

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
        EmitSignal(SignalName.Pressed);
    }

    public void ClearItem() => ClearItem(PegLegResourceManager.defaultIcon);
    public virtual void ClearItem(Texture2D clearIcon)
    {
        if (currentItem is not null)
        {
            currentItem.OnChanged -= UpdateItem;
            currentItem.OnRemoved -= RemoveItem;
            currentItem = null;
        }
        inspectorOverride = null;
        EmitSignal(SignalName.ItemDoesExist, false);
        EmitSignal(SignalName.ItemDoesNotExist, true);
        EmitSignal(SignalName.NameChanged, "");
        EmitSignal(SignalName.DescriptionChanged, "");
        EmitSignal(SignalName.IconChanged, clearIcon);
        EmitSignal(SignalName.SubtypeIconChanged, clearIcon);
        EmitSignal(SignalName.TypeChanged, "");
        EmitSignal(SignalName.AmountVisibility, false);
        EmitSignal(SignalName.AmountChanged, "");
        EmitSignal(SignalName.RarityChanged, Colors.Transparent);
        EmitSignal(SignalName.InteractableChanged, interactableWhenEmpty);
        EmitSignal(SignalName.NotificationChanged, false);
        EmitSignal(SignalName.FavoriteChanged, false);
        EmitSignal(SignalName.OverflowWarning, false);
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
    protected GameItemSelector selector;
    public virtual void SetRecyclableElementProvider(IRecyclableElementProvider provider)
    {
        if (provider is GameItemSelector newSelector)
        {
            itemProvider = selector = newSelector;
        }
        else if (provider is IRecyclableElementProvider<GameItem> newHandleProvider)
        {
            itemProvider = newHandleProvider;
            selector = null;
        }
    }

    protected int recycleIndex = 0;
    public virtual void SetRecycleIndex(int index)
    {
        if (itemProvider is not null)
        {
            recycleIndex = index;
            SetItem(itemProvider.GetRecycleElement(index));
            if (selector is not null)
                EmitSignal(SignalName.InteractableChanged, selector.selectablePredicate.Try(currentItem));
            UpdateSelectionVisuals();
        }
    }

    public virtual void PerformRecycleSelection()
    {
        if (itemProvider is not null)
        {
            itemProvider.OnElementSelected(recycleIndex);
            UpdateSelectionVisuals();
        }
    }

    public virtual void ClearRecycleIndex() => ClearItem();

    void UpdateSelectionVisuals()
    {
        if (selector is null || selectionGraphics is null)
            return;

        bool isSelected = selector.ItemIsSelected(currentItem);
        bool isSelectable = selector.selectablePredicate.Try(currentItem);
        selectionGraphics.ButtonPressed = isSelected || !isSelectable;
        if (!isSelectable)
        {
            EmitSignal(SignalName.SelectionTintChanged, selector.unselectableTintColor);
            EmitSignal(SignalName.SelectionMarkerChanged, selector.unselectableMarkerTex);
            return;
        }
        if (isSelected)
        {
            bool isCollectable = currentItem.isCollectedCache ?? false;
            EmitSignal(SignalName.SelectionTintChanged, isCollectable ? selector.collectionTintColor : selector.selectedTintColor);
            EmitSignal(SignalName.SelectionMarkerChanged, isCollectable ? selector.collectionMarkerTex : selector.selectedMarkerTex);
        }
    }
}
