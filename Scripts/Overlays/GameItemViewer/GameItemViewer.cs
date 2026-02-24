using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

public partial class GameItemViewer : ModalWindow
{
    public static GameItemViewer Instance { get; private set; }

    [Export]
    GameItemEntry displayItemEntry;
    [Export]
    TabContainer activeTabParent;
    [Export]
    Control inactiveTabParent;

    [ExportGroup("Item Upgrader")]
    [Export]
    GameItemUpgrader upgrader;


    [ExportGroup("Perk Details")]
    [Export]
    PerkViewer perkDetailsPanel;

    [ExportGroup("Hero Details")]
    [Export]
    Control heroDetailsPanel;

    [Export(PropertyHint.ArrayType)]
    HeroAbilityEntry[] heroAbilityEntries;

    [Export]
    HeroAbilityEntry heroPerkEntry;

    [Export]
    HeroAbilityEntry heroCommanderPerkEntry;

    [Export]
    GameItemEntry teamPerkEntry;

    [Export]
    Slider tierSlider;
    [Export]
    Slider levelSlider;

    [ExportGroup("Stats")]
    [Export]
    Control statsTreeContainer;
    [Export]
    Tree statsTree;

    [ExportGroup("Data")]
    [Export]
    Control devTextContainer;
    [Export]
    CodeEdit devText;
    [Export]
    TextEdit searchTextEdit;
    [Export]
    Label searchResult;

    [ExportGroup("Buttons")]
    [Export]
    Control recycleButtonPanel;
    [Export]
    Control levelupButtonPanel;
    [Export]
    Control evolveButtonPanel;
    [Export]
    Control rarityupButtonPanel;

    [ExportGroup("Choices")]
    [Export]
    Control itemChoiceParent;
    [Export]
    GameItemEntry[] itemChoiceEntries;
    [Export]
    Control[] itemChoiceLayoutSections;

    [ExportGroup("Purchases")]
    [Export]
    GameOfferEntry currentOfferEntry;
    [Export]
    SpinBox purchaseSpinner;

    public override void _Ready()
    {
        base._Ready();
        Instance = this;

        levelSlider.ValueChanged += _ => RefreshHeroStats();
        tierSlider.ValueChanged += _ => RefreshHeroStats();

        for (int i = 0; i < itemChoiceEntries.Length; i++)
        {
            int val = i;
            itemChoiceEntries[i].Pressed += () => SetChoiceIndex(val);
        }
        purchaseSpinner.ValueChanged += SpinnerChanged;
    }

    GameItem currentItem;
    GameOffer currentOffer;

    GameItem[] choices;
    int itemTier = 1;
    int itemLevel = 1;

    public void ShowItem(GameItem newItem, int prioritiseChoice = 0, bool preserveUnseen = false, bool rawInspect=false)
    {
        itemChoiceParent.Visible = false;
        currentItem = newItem;
        SetWindowOpen(true);
        currentOfferEntry.Visible = false;
        currentOfferEntry.ClearOffer();
        currentOffer = null;

        if (!preserveUnseen && newItem.profile?.account.isOwned == true)
            newItem.MarkItemSeen();

        upgrader.Visible = currentItem.attributes?["level"] is not null;
        upgrader.SetItem(currentItem);

        //if choice cardpack, display choices instead
        if (!rawInspect && currentItem.template?.Type == "CardPack" && currentItem.CardPackChoices is GameItem[] itemChoices)
        {
            itemChoiceParent.Visible = true;
            choices = itemChoices;

            for (int i = 0; i < choices.Length; i++)
                itemChoiceEntries[i].SetItem(choices[i]);
            for (int i = 0; i < choices.Length - 2; i++)
                itemChoiceLayoutSections[i].Visible = true;
            for (int i = choices.Length - 2; i < itemChoiceLayoutSections.Length; i++)
                itemChoiceLayoutSections[i].Visible = false;
            if (prioritiseChoice < 0 || prioritiseChoice >= choices.Length)
                prioritiseChoice = 0;

            SetDisplayItem(choices[prioritiseChoice]);
            itemChoiceEntries[prioritiseChoice].EmitPressedSignal();
        }
        else
        {
            SetDisplayItem(currentItem);
        }
    }

    public async void ShowOffer(GameOffer offer)
    {
        upgrader.Visible = false;
        itemChoiceParent.Visible = false;
        currentOffer = offer;
        currentItem = currentOffer.itemGrants[0];
        SetDisplayItem(currentItem);
        SetWindowOpen(true);
        await currentOfferEntry.SetOffer(currentOffer);
        var totalLimit = await GameAccount.ActiveAccount.GetPurchaseLimit(currentOffer);
        purchaseSpinner.MaxValue = totalLimit;
        purchaseSpinner.Visible = totalLimit > 1;
        purchaseSpinner.Value = 1;
        SpinnerChanged(1);
        currentOfferEntry.Visible = currentItem?.template is not null && !currentOffer.FakeOffer;
    }

    GameItem displayedItem = null;
    void SetDisplayItem(GameItem item)
    {
        Visible = true;
        displayItemEntry.SetItem(item);
        item.SetRewardNotification();
        displayedItem = item;
        
        //TODO: add extra icons for survivors, and fix descriptions

        statsTreeContainer.Reparent(inactiveTabParent);
        statsTree.HideFolding = true;
        perkDetailsPanel.Reparent(inactiveTabParent);
        heroDetailsPanel.Reparent(inactiveTabParent);
        devTextContainer.Reparent(inactiveTabParent);

        var type = item.template?.Type;
        if (type == "Hero")
        {
            //parse hero stuff
            heroDetailsPanel.Reparent(activeTabParent);
            int tier = item.template.Tier;
            var heroItems = item.template.GetHeroAbilities();
            heroPerkEntry.SetAbility(heroItems[0], false);
            heroCommanderPerkEntry.SetAbility(heroItems[1], tier < 2);
            for (int i = 0; i < 3; i++)
            {
                heroAbilityEntries[i].SetAbility(heroItems[i + 2], tier <= i);
            }
            if (item.template["UnlocksTeamPerk"]?.ToString() is string teamPerk)
            {
                teamPerkEntry.SetItem(GameItemTemplate.Get(teamPerk).CreateInstance());
                teamPerkEntry.Visible = true;
            }
            else
                teamPerkEntry.Visible = false;

            RefreshHeroStats();

            statsTreeContainer.Reparent(activeTabParent);
            //statsTree.CustomMinimumSize = statsTree.GetMinimumSize() + new Vector2(10, 0);
        }
        else if (type == "Schematic" || type == "Weapon" || type == "Trap")
        {
            //parse schematic stuff
            if (item.Alterations is not null || (item.template?.AlterationSlots?.Length ?? 0) > 0)
            {
                perkDetailsPanel.Reparent(activeTabParent);
                perkDetailsPanel.SetItem(item);
            }

            var statsSource = type == "Schematic" ? GameItemTemplate.Get(item.template["CraftingResult"].ToString()) : item.template;

            JsonObject statsJson = null;

            if (statsSource["RangedWeaponStats"] is JsonObject rangedWeaponStats)
            {
                statsTree.HideFolding = false;
                statsJson = rangedWeaponStats;
            }
            else if (statsSource["MeleeWeaponStats"] is JsonObject meleeWeaponStats)
                statsJson = meleeWeaponStats;
            else if (statsSource["TrapStats"] is JsonObject trapStats)
                statsJson = trapStats;

            if (statsJson is not null)
            {
                statsTreeContainer.Reparent(activeTabParent);
                statsTree.Clear();
                var root = statsTree.CreateItem();
                statsTree.Columns = 2;
                statsTree.SetColumnTitle(0, "Stat");
                statsTree.SetColumnTitle(1, "Value");
                statsTree.SetColumnClipContent(0, false);
                statsTree.SetColumnClipContent(1, false);
                statsTree.SetColumnExpand(1, false);
                statsTree.SetColumnCustomMinimumWidth(1, 100);
                GenerateTreeFromJson(statsJson, root);
            }
            //statsTree.CustomMinimumSize = statsTree.GetMinimumSize() + new Vector2(35, 0);
        }
        else if (type == "Defender")
        {
            //parse defender stuff
            if (item.attributes?.ContainsKey("alterations") ?? false)
            {
                perkDetailsPanel.Reparent(activeTabParent);
                perkDetailsPanel.SetItem(item);
            }
        }

        if (AppConfig.Get("advanced", "developer", false))
        {
            var rawData = item.RawData.ToJsonString(new() { WriteIndented = true });
            devText.Text = rawData;
            devText.FoldLine(1);
            var lines = rawData.Split("\n").ToList();
            var templateIndex = lines.FindIndex(l => l.Trim().StartsWith("\"template\": {"));
            if (templateIndex >= 0)
                devText.FoldLine(templateIndex);
            devTextContainer.Reparent(activeTabParent);
        }

        activeTabParent.Visible = activeTabParent.GetChildCount() > 0;
        if (activeTabParent.Visible)
            activeTabParent.CurrentTab = 0;
    }

    public void SetSearchText()
    {
        string searchText = searchTextEdit?.Text;
        if (searchResult is null || currentItem is null)
            return;
        PLSearch.Instruction[] itemSearchInstructions = PLSearch.GenerateSearchInstructions(searchText) ?? Array.Empty<PLSearch.Instruction>();
        PLSearch.EvaluateInstructions(itemSearchInstructions, currentItem.RawData, out var resultText);
        searchResult.Text = resultText;
    }

    SemaphoreSlim heroStatsActiveSemaphore = new(1);
    SemaphoreSlim heroStatsQueuedSemaphore = new(2);
    async void RefreshHeroStats()
    {
        if (heroStatsQueuedSemaphore.CurrentCount == 0)
            return;
        try
        {
            await heroStatsQueuedSemaphore.WaitAsync();
            await heroStatsActiveSemaphore.WaitAsync();
            var account = displayedItem.profile?.account ?? GameAccount.ActiveAccount;
            var fortStats = account.FortStats;

            statsTree.Clear();
            statsTree.Columns = 3;
            statsTree.SetColumnTitle(0, "Stat");
            statsTree.SetColumnTitle(1, "Value");
            statsTree.SetColumnTitle(2, "Scaled Value");

            statsTree.SetColumnClipContent(0, false);
            statsTree.SetColumnClipContent(1, false);
            statsTree.SetColumnClipContent(2, false);

            statsTree.SetColumnExpand(0, true);
            statsTree.SetColumnExpand(1, false);
            statsTree.SetColumnExpand(2, false);

            statsTree.SetColumnCustomMinimumWidth(1, 40);
            statsTree.SetColumnCustomMinimumWidth(2, 75);

            var root = statsTree.CreateItem();
            AddStatTreeEntry(root, "Health", displayedItem.GetHeroStat(HeroStats.MaxHealth), displayedItem.GetHeroStat(HeroStats.MaxHealth, (int)levelSlider.Value, (int)tierSlider.Value), fortStats.fortitude + await account.GetSurvivorBonus(SurvivorBonus.MaxHealth));
            AddStatTreeEntry(root, "Shield", displayedItem.GetHeroStat(HeroStats.MaxShields), displayedItem.GetHeroStat(HeroStats.MaxShields, (int)levelSlider.Value, (int)tierSlider.Value), fortStats.resistance + await account.GetSurvivorBonus(SurvivorBonus.MaxShields));
            AddStatTreeEntry(root, "Health Regen Rate", displayedItem.GetHeroStat(HeroStats.HealthRegenRate), displayedItem.GetHeroStat(HeroStats.HealthRegenRate, (int)levelSlider.Value, (int)tierSlider.Value), fortStats.fortitude);
            AddStatTreeEntry(root, "Shield Regen Rate", displayedItem.GetHeroStat(HeroStats.ShieldRegenRate), displayedItem.GetHeroStat(HeroStats.ShieldRegenRate, (int)levelSlider.Value, (int)tierSlider.Value), fortStats.resistance + await account.GetSurvivorBonus(SurvivorBonus.ShieldRegenRate));
            AddStatTreeEntry(root, "Ability Damage", displayedItem.GetHeroStat(HeroStats.AbilityDamage), displayedItem.GetHeroStat(HeroStats.AbilityDamage, (int)levelSlider.Value, (int)tierSlider.Value), 0, 10); //ingame UI doesnt scale these by tech, should it?
            AddStatTreeEntry(root, "Healing Modifier", displayedItem.GetHeroStat(HeroStats.HealingModifier), displayedItem.GetHeroStat(HeroStats.HealingModifier, (int)levelSlider.Value, (int)tierSlider.Value), 0, 100);

        }
        finally
        {
            heroStatsActiveSemaphore.Release();
            heroStatsQueuedSemaphore.Release();
        }
    }

    static void AddStatTreeEntry(TreeItem parent, string name, float baseValue = 5, float upgradeValue = 5, float scalar = 0, int roundingDivisor = 1)
    {
        var row = parent.CreateChild();
        row.SetText(0, name);
        row.SetText(1, (Mathf.Round(upgradeValue * roundingDivisor)/roundingDivisor).ToString());
        if (upgradeValue > baseValue)
            row.SetCustomColor(1, Colors.Green);
        else if (upgradeValue < baseValue)
            row.SetCustomColor(1, Colors.Red);
        else
            row.SetCustomColor(1, Colors.White);
        if (scalar != 0)
            row.SetText(2, (Mathf.Round(upgradeValue * ((scalar/100)+1)*roundingDivisor)/roundingDivisor).ToString());
    }

    //todo: display final values as scaled by FORT stats
    void GenerateTreeFromJson(JsonObject jsonParent, TreeItem treeParent)
    {
        foreach (var item in jsonParent)
        {
            string fixedName = Regex.Replace(item.Key, "[A-Z]", " $0");
            if (item.Value is JsonObject childObject)
            {
                TreeItem entry = treeParent.CreateChild();
                entry.SetText(0, fixedName);
                GenerateTreeFromJson(childObject, entry);
                entry.Collapsed = true;
            }
            else
            {
                string fixedVal = "";

                if (item.Value.AsValue().TryGetValue(out float floatValue))
                    fixedVal = (Mathf.Round(floatValue * 100) / 100).ToString();
                else
                {
                    fixedVal = Regex.Replace(item.Value.ToString(), "[A-Z]", " $0").Trim();
                    fixedVal = fixedVal.Replace("  ", " ");
                    if(fixedName == " Reload Type")
                    {
                        fixedVal = fixedVal switch
                        {
                            "Reload Individual Bullets" => "1 At A Time",
                            "Reload Based On Cartridge Per Fire" => jsonParent["CartridgePerFire"].GetValue<int>()+ " At A Time",
                            "Reload Whole Clip" => "All",
                            _ => fixedVal,
                        };
                    }
                }
                if (fixedVal == "0")
                    continue;
                
                TreeItem entry = treeParent.CreateChild();
                entry.SetText(0, fixedName);
                entry.SetText(1, fixedVal);
            }
        }
    }

    int latestPurchasablePrice = 0;

    public void SpinnerChanged(double newValue)
    {
        currentOfferEntry.SetTargetPurchaseQuantity((int)newValue);
    }

    public void PurchaseItem()
    {
        if (currentOffer is null)
            return;
        var template = currentOffer.itemGrants[0].templateId;
        bool workaround = template.Equals("Token:accountinventorybonus", StringComparison.OrdinalIgnoreCase);
        workaround |= template.StartsWith("CardPack:cardpack_schematic", StringComparison.OrdinalIgnoreCase);
        ShopPurchaseAnimation.PlayAnimation(
            currentOffer.itemGrants[0].GetTexture(),
            currentOfferEntry.currentPurchaseQuantity,
            () => GameAccount.ActiveAccount.PurchaseOffer(currentOffer, workaround ? 1 : currentOfferEntry.currentPurchaseQuantity),
            workaround
        );
        SetWindowOpen(false);
    }


    public void SetChoiceIndex(int index)
    {
        SetDisplayItem(choices[index]);
    }
}
