using Godot;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

public class GameItem
{
    #region Statics

    static Dictionary<string, string> zcpEquivelents = new()
    {
        ["cardpack:zcp_reagent_c_t04\\w*"] = "AccountResource:reagent_c_t04",
        ["cardpack:zcp_reagent_c_t03\\w*"] = "AccountResource:reagent_c_t03",
        ["cardpack:zcp_reagent_c_t02\\w*"] = "AccountResource:reagent_c_t02",
        ["cardpack:zcp_reagent_c_t01\\w*"] = "AccountResource:reagent_c_t01",

        ["cardpack:zcp_reagent_alteration_upgrade_sr\\w*"] = "AccountResource:reagent_alteration_upgrade_sr",
        ["cardpack:zcp_reagent_alteration_upgrade_vr\\w*"] = "AccountResource:reagent_alteration_upgrade_vr",
        ["cardpack:zcp_reagent_alteration_upgrade_r\\w*"] = "AccountResource:reagent_alteration_upgrade_r",
        ["cardpack:zcp_reagent_alteration_upgrade_uc\\w*"] = "AccountResource:reagent_alteration_upgrade_uc",
        ["cardpack:zcp_reagent_alteration_generic\\w*"] = "AccountResource:reagent_alteration_generic",

        ["cardpack:zcp_phoenixxp\\w*"] = "AccountResource:phoenixxp",
        ["cardpack:zcp_personnelxp\\w*"] = "AccountResource:personnelxp",
        ["cardpack:zcp_heroxp\\w*"] = "AccountResource:heroxp",
        ["cardpack:zcp_schematicxp\\w*"] = "AccountResource:schematicxp",

        ["cardpack:zcp_ore_copper\\w*"] = "Ingredient:ingredient_ore_copper",
        ["cardpack:zcp_ore_silver\\w*"] = "Ingredient:ingredient_ore_silver",
        ["cardpack:zcp_ore_malachite\\w*"] = "Ingredient:ingredient_ore_malachite",
        ["cardpack:zcp_ore_obsidian\\w*"] = "Ingredient:ingredient_ore_obsidian",
        ["cardpack:zcp_ore_brightcore\\w*"] = "Ingredient:ingredient_ore_brightcore",

        ["cardpack:zcp_crystal_quartz\\w*"] = "Ingredient:ingredient_crystal_quartz",
        ["cardpack:zcp_crystal_shadowshard\\w*"] = "Ingredient:ingredient_crystal_shadowshard",
        ["cardpack:zcp_crystal_sunbeam\\w*"] = "Ingredient:ingredient_crystal_sunbeam",

        ["cardpack:zcp_improvised_r"] = "Ingredient:ingredient_rare_mechanism",
        ["cardpack:zcp_improvised_vr"] = "Ingredient:ingredient_rare_powercell",

        ["cardpack:zcp_eventscaling\\w*"] = "AccountResource:eventcurrency_scaling",
    };

    static GameItem FindZcpEquivelent(string templateId)
    {
        if (!(templateId?.StartsWith("CardPack:zcp_", StringComparison.InvariantCultureIgnoreCase) ?? false))
            return null;
        foreach (var equivelent in zcpEquivelents)
        {
            if (Regex.Match(templateId.ToLower(), equivelent.Key).Success)
            {
                GameItem equivelentItem = GameItemTemplate.Get(equivelent.Value).CreateInstance();
                equivelentItem.SetSeenLocal();
                equivelentItem.GetSearchTags();
                return equivelentItem;
            }
        }
        return null;
    }

    public static GameItem Empty { get; private set; } = new(null, 1, customData: new() { ["empty"] = true });

    #endregion

    public event Action OnChanged;
    public event Action OnRemoving;
    public event Action OnRemoved;

    public GameItem(GameProfile profile, string uuid, JsonObject rawData)
    {
        this.uuid = uuid;
        this.profile = profile;
        SetItemOrRewardData(rawData);
    }

    public void Reassociate(string newUUID, JsonObject rawData)
    {
        uuid = newUUID;
        SetItemOrRewardData(rawData);
    }

    public record struct ItemData(string templateId, int quantity = 1, JsonObject attributes = null)
    {
        public GameItem ToItem() => new(GameItemTemplate.Get(templateId), quantity, attributes.SafeDeepClone(), templateId:templateId);

        public override string ToString() => JsonSerializer.Serialize(this, Helpers.JsonOptions.Fields);

        //assumes that item types are distinct
        public static ItemData[] Add(ItemData[] first, ItemData[] second)
        {
            var secondDict = second.ToDictionary(i => i.templateId, i => i.quantity);
            for (int i = 0; i < first.Length; i++)
            {
                if (!secondDict.TryGetValue(first[i].templateId, out int amount))
                    continue;
                first[i].quantity += amount;
                secondDict.Remove(first[i].templateId);
            }
            return [.. first.Where(i => i.quantity >= 1), ..second.Where(i => secondDict.ContainsKey(i.templateId))];
        }

        public static ItemData[] Subtract(ItemData[] first, ItemData[] second)
        {
            var secondDict = second.ToDictionary(i => i.templateId, i => i.quantity);
            for (int i = 0; i < first.Length; i++)
            {
                if (!secondDict.TryGetValue(first[i].templateId, out int amount))
                    continue;
                first[i].quantity -= amount;
            }
            return [.. first.Where(i => i.quantity >= 1)];
        }
    }

    public record struct ItemReward()
    {
        public string name;
        public string itemType;
        public JsonElement? attributes;
        public int quantity = 1;
        public GameItem ToItem() => new(GameItemTemplate.Get(itemType), quantity, attributes?.Deserialize<JsonObject>());
    }

    public GameItem SetUUID(string customUUID = null)
    {
        uuid ??= customUUID ?? Guid.NewGuid().ToString();
        return this;
    }

    public GameItem(GameItemTemplate template, int quantity, JsonObject attributes = null, GameItem inspectorOverride = null, JsonObject customData = null, string templateId = null)
    {
        _template = template;
        this.templateId = template?.TemplateId ?? templateId;
        upgradeBasis = template;
        this.quantity = quantity;
        this.attributes = attributes;
        this.customData = customData ?? [];
        this.inspectorOverride = inspectorOverride;
        zcpEquivelent = FindZcpEquivelent(this.templateId);
        isSeenLocal = true;
    }

    public GameProfile profile { get; private set; }
    public string uuid { get; private set; }

    public GameItemTemplate sortingTemplate => zcpEquivelent?.template ?? template;
    public GameItem zcpEquivelent { get; private set; }
    public GameItem inspectorOverride { get; private set; }

    public string templateId { get; private set; }
    GameItemTemplate _template;
    public GameItemTemplate template => _template ??= GameItemTemplate.Get(templateId);

    public JsonObject attributes { get; private set; }
    public JsonObject customData { get; private set; } = [];

    public int quantity { get; private set; }
    public void SetLocalQuantity(int newQuant) =>  quantity = newQuant;

    public int TotalQuantity
    {
        get
        {
            if (profile is null)
                return quantity;
            return profile.GetTemplateItems(templateId).Select(i => i.quantity).Sum();
        }
    }

    public ItemData GameItemData => new()
    {
        templateId = templateId,
        attributes = attributes?.SafeDeepClone() ?? [],
        quantity = quantity,
    };

    public void SetItemOrRewardData(JsonObject rawData)
    {
        var newTemplate = rawData["templateId"]?.ToString() ?? rawData["itemType"]?.ToString();
        if (templateId != newTemplate)
        {
            _template = null;
            templateId = newTemplate;
            zcpEquivelent = FindZcpEquivelent(templateId);
        }
        quantity = rawData["quantity"]?.GetValue<int>() ?? 1;
        attributes = rawData["attributes"]?.AsObject().SafeDeepClone();
        isSeenLocal = null;
        customData = [];
        ResetCachedData();
    }

    JsonObject _rawData;
    public JsonObject RawData => _rawData ?? GenerateRawData();
    public JsonObject GenerateRawData()
    {
        var templateData = template?.rawData.SafeDeepClone();
        templateData ??= [];
        templateData["searchTags"] = null;
        templateData.Remove("searchTags");
        _rawData = new()
        {
            ["uuid"] = uuid,
            ["templateId"] = templateId,
            ["attributes"] = attributes?.SafeDeepClone(),
            ["quantity"] = quantity,
            ["template"] = templateData,
            ["searchTags"] = GetSearchTags()?.SafeDeepClone(),
        };
        if (uuid is null)
            _rawData.Remove("uuid"); //doing this backwards to make uuid the first property of rawData
        if(customData is not null)
            _rawData["custom"] = customData.SafeDeepClone();
        if(zcpEquivelent is not null)
            _rawData["bundleItem"] = zcpEquivelent.template.rawData.SafeDeepClone();
        return _rawData;
    }

    public JsonObject CustomSearchObject(string[] searchTags, bool union = false)
    {
        var searchObj = RawData.SafeDeepClone();
        searchObj["searchTags"] = new JsonArray([.. searchTags, .. union ? RawData["searchTags"].Deserialize<string[]>() : []]);
        return searchObj;
    }

    string[] alterations;
    public string[] Alterations => alterations ??= (attributes?["alterations"] ?? attributes?["alterationDefinitions"])?.Deserialize<string[]>();

    public string Personality => attributes?["personality"]?.ToString() is string rawPersonality ? ParseSurvivorAttribute(rawPersonality) : null;
    public string SetBonus => attributes?["set_bonus"]?.ToString() is string rawSetBonus ? ParseSurvivorAttribute(rawSetBonus) : null;

    GameItem[] cardPackChoices;
    public GameItem[] CardPackChoices => cardPackChoices ??= attributes?["options"]?
                .AsArray()
                .Select(c =>
                {
                    var template = GameItemTemplate.Get(c["itemType"].ToString());
                    var choiceItem = template.CreateInstance(c["quantity"].GetValue<int>(), c["attributes"]?.AsObject().SafeDeepClone());
                    choiceItem.SetRewardNotification(null, true);
                    return choiceItem;
                })
                .ToArray();
    static string ParseSurvivorAttribute(string survivorAttr)
    {
        survivorAttr = survivorAttr.Split(".")[^1][2..];
        if (survivorAttr.EndsWith("Low"))
            survivorAttr = survivorAttr[..^3];
        if (survivorAttr.EndsWith("High"))
            survivorAttr = survivorAttr[..^4];
        return Regex.Replace(survivorAttr, "[A-Z]", " $&").Trim();
    }

    JsonArray _searchTags;
    public JsonArray GetSearchTags(bool assumeUncommon = true)
    {
        if(_searchTags is not null)
            return _searchTags;
        if(zcpEquivelent is not null)
        {
            _searchTags = zcpEquivelent.GetSearchTags(assumeUncommon);
            _searchTags.Add("Bundle");
            return _searchTags;
        }
        _searchTags = template?.GenerateSearchTags(assumeUncommon)?.SafeDeepClone();
        if (_searchTags is null)
            return [.. templateId?.Split(":") ?? []];
        if (attributes?["inventory_overflow_date"] is not null)
            _searchTags.Add("Overflow");
        if (attributes?["personality"]?.ToString() is string rawPersonality)
            _searchTags.Add(ParseSurvivorAttribute(rawPersonality));
        if (attributes?["set_bonus"]?.ToString() is string rawSetBonus)
            _searchTags.Add(ParseSurvivorAttribute(rawSetBonus));
        if (attributes?["quest_state"]?.ToString() is string questState)
            _searchTags.Add(questState);
        return _searchTags;
    }

    void ResetCachedData()
    {
        _rawData = null;
        _rating = null;
        _searchTags = null;
        alterations = null;
        cardPackChoices = null;
    }

    bool? isFavouritedLocal = null;
    public bool IsFavourited => isFavouritedLocal ?? attributes?["favorite"]?.GetValue<bool>() == true;
    public void SetFavouritedLocal(bool? newVal)
    {
        if (isFavouritedLocal == newVal || template?.CanBeFavourited != true)
            return;
        bool realVal = attributes?["favorite"]?.GetValue<bool>() ?? false;
        bool update = (newVal ?? realVal) != (isFavouritedLocal ?? realVal);
        isFavouritedLocal = newVal;
        if (update)
            NotifyChanged();
    }

    public async void SetFavourited(bool newVal)
    {
        if (template?.CanBeFavourited != true || profile?.account.isOwned != true)
            return;
        SetFavouritedLocal(newVal);
        string content = @$"{{""targetItemId"": ""{uuid}"", ""bFavorite"":{newVal.ToString().ToLower()}}}";
        await profile.PerformOperation("SetItemFavoriteStatus", content);
    }

    public bool QuestPinned => profile?.account.HasPinnedQuest(this) ?? false;
    public async void SetPinned(bool newVal) => await SetPinnedAsync(newVal);
    public async Task SetPinnedAsync(bool newVal)
    {
        if (profile?.account.isOwned != true)
            return;
        if (newVal)
            await profile.account.AddPinnedQuest(this);
        else
            await profile.account.RemovePinnedQuest(this);
    }



    bool? isSeenLocal = null;
    public bool IsSeen => isSeenLocal ?? attributes?["item_seen"]?.GetValue<bool>() ?? false || template?.CanBeUnseen != true;
    public void SetSeenLocal(bool? newVal = true)
    {
        if (isSeenLocal == newVal || template?.CanBeUnseen != true)
            return;
        bool realVal = attributes?["item_seen"]?.GetValue<bool>() ?? false;
        bool update = (newVal ?? realVal) != (isSeenLocal ?? realVal);
        isSeenLocal = newVal;
        if (update)
            NotifyChanged();
    }

    public void MarkItemSeen()
    {
        if (attributes?["item_seen"] is not null || template?.CanBeUnseen != true)
            return;
        SetSeenLocal();
        string content = @$"{{""itemIds"": [""{uuid}""]}}";
        profile.PerformOperation("MarkItemSeen", content).StartTask();
    }

    public static void MarkItemsSeen(IEnumerable<GameItem> items)
    {
        var itemArr = items.ToArray();
        if (itemArr.Length == 0)
            return;
        if (!itemArr.Any(i => i.attributes?["item_seen"] is not null))
            return;
        var profile = itemArr[0].profile;
        foreach (var item in itemArr)
        {
            if (item.template?.CanBeUnseen != true)
            {
                GD.PushWarning("an item cant be unseen");
                return;
            }
            if (item.profile != profile)
            {
                GD.PushWarning("why did you mix profiles?");
                return;
            }
        }
        foreach (var item in itemArr)
        {
            item.SetSeenLocal();
        }
        string content = @$"{{""itemIds"": [{string.Join(", ", itemArr.Select(i=> @$"""{i.uuid}"""))}]}}";
        profile.PerformOperation("MarkItemSeen", content).StartTask();
    }

    public async Task<GameItem[]> ClaimQuest(int index = 0)
    {
        GD.Print("Claiming "+uuid);
        string altcontent = @$"{{""questId"": ""{uuid}"", ""selectedRewardIndex"": {index}}}";
        var notifs = await profile.PerformOperation("ClaimQuestReward", altcontent);
        var claimNotif = notifs?.FirstOrDefault(n => n["type"]?.ToString() == "questClaim");
        if(claimNotif is not null)
        {
            GD.Print(claimNotif);
            return [.. claimNotif["loot"]["items"]
                .AsArray()
                .Select(n =>
                    profile.account
                        .GetProfile(n["itemProfile"].ToString())?
                        .GetItem(n["itemGuid"].ToString())?
                        .Clone(n["quantity"].GetValue<int>()) ??
                    GameItemTemplate.Get(n["itemType"].ToString())?.
                        CreateInstance(n["quantity"].GetValue<int>())
                )
            ];
        }
        return [];
    }

    public async void SetRewardNotification(GameAccount account = null, bool force = false)
    {
        account ??= GameAccount.ActiveAccount;
        if (profile is not null || (!force && isSeenLocal != null))
            return;

        if(!IsSeen)
            SetSeenLocal(true);

        if (!account.isOwned)
            return;

        bool exists = await SetCollected(account) ?? true;

        if (!exists)
        {
            var accountItems = await account.GetProfile(FnProfileTypes.AccountItems).Query();
            exists = accountItems
            .GetItems(template.Type, item =>
                item.template?.DisplayName == (template?.DisplayName ?? "nope") &&
                item.template?.RarityLevel >= template?.RarityLevel)
            .Any();
        }

        if (!exists)
            SetSeenLocal(false);
    }

    public async Task TransferStorage(int amount = 0)
    {
        if (profile?.profileId != "theater0" && profile?.profileId != "outpost0")
            return;
        if (amount < 1)
            amount = quantity;
        bool toStorage = profile?.profileId == "theater0";
        amount = Mathf.Max(amount, quantity);
        var backpack = await profile.account.GetProfile("theater0").Query();
        var storage = await profile.account.GetProfile("outpost0").Query();
        var dest = toStorage ? storage : backpack;

        var firstEmptyStack = dest.GetTemplateItems(templateId).OrderBy(i => i.quantity).FirstOrDefault();

        JsonObject transfer = new()
        {
            ["itemId"] = uuid,
            ["quantity"] = amount,
            ["toStorage"] = toStorage,
        };
        if (!string.IsNullOrWhiteSpace(firstEmptyStack?.uuid))
            transfer["newItemIdHint"] = firstEmptyStack.uuid;

        JsonObject content = new()
        {
            ["transferOperations"] = new JsonArray([transfer])
        };
        //GameProfile.printChanges = true;
        await backpack.PerformOperation("StorageTransfer", content);
    }

    public bool? isCollectedCache { get; private set; }
    public async Task<bool?> SetCollected(GameAccount account = null)
    {
        if (template?.IsCollectable != true)
            return null;

        account ??= profile?.account ?? GameAccount.ActiveAccount;
        await account.GetProfile(template.CollectionProfile).Query();

        var collectionBook = account.GetProfile(template.CollectionProfile);
        if (template.Type == "Worker")
        {
            if (template.Name.StartsWith("workerhalloween"))
            {
                //with costume party attendees, 3 of each rarity can be collected
                return collectionBook
                    .GetItems("Worker", item =>
                        item.template.Name.StartsWith("workerhalloween") &&
                        item.template.Rarity == template.Rarity)
                    .Length < 3;
            }
            else if (template.SubType is not null)
            {
                //with mythic lead survivors, one of each unique lead can be collected
                if (template.Rarity == "Mythic")
                    return collectionBook
                        .GetItems("Worker", item => item.templateId == templateId)
                        .Any();
                //with regular lead survivors, one of each subtype-rarity combo can be collected
                else
                    return collectionBook
                        .GetItems("Worker", item =>
                            item.template.SubType == template.SubType &&
                            item.template.Rarity == template.Rarity)
                        .Any();
            }
            //with regular survivors, one of personality-rarity combo can be collected
            return collectionBook
                .GetItems("Worker", item =>
                    item.attributes?["personality"]?.ToString() == (attributes?["personality"]?.ToString() ?? "nope") &&
                    item.template.Rarity == template.Rarity)
                .Any();
        }
        var result = collectionBook
            .GetItems(template.Type, item => item.templateId == templateId)
            .Any();
        if (isCollectedCache != result)
        {
            isCollectedCache = result;
            NotifyChanged();
        }
        return result;
    }

    public float GetHeroStat(string stat, int givenLevel = 0, int givenTier = 0)
    {
        if (PegLegResourceManager.HeroStats is not JsonObject stats)
            return 0;

        if (givenLevel <= 0)
        {
            givenLevel = attributes?["level"]?.GetValue<int>() ?? 1;
            givenTier = template.Tier;
        }

        string heroStatLine = template["HeroStatLine"].ToString();
        string heroRarityAndTier = template.GetCompactRarityAndTier(givenTier);
        var statLookup = stats["Types"]?[$"{template.SubType}_{heroStatLine}"]?[heroRarityAndTier]?[stat]?.AsObject();
        if (statLookup is null)
            return 0;
        int statKey = Mathf.Clamp(givenLevel - (int)statLookup["FirstLevel"], 0, statLookup["Values"].AsArray().Count - 1);
        return (float)statLookup["Values"][statKey];
    }

    public string QuestState => attributes?["quest_state"]?.ToString();
    public bool QuestComplete => QuestState == "Completed" || QuestClaimed;
    public bool QuestClaimed => QuestState == "Claimed";

    public void ClearRating() => _rating = null;
    int? _rating;
    public int Rating => _rating ??= CalculateRating();
    public int UpdateRating() => (_rating = CalculateRating()) ?? 0;

    public GameItem[] GetPrerollItems() => attributes?["items"]?.AsArray()
            .Select(node => new GameItem(null, null, node.AsObject()))
            .OrderBy(item => -item.template?.RarityLevel)
            .ThenBy(item => item.template?.Type)
            .ThenBy(item => item.template?.DisplayName)
            .ToArray() ?? null;

    public int CalculateRating()
    {
        if (PegLegResourceManager.ItemRatings is not JsonObject ratings)
            return 0;
        if (template is null)
            return 0;
        if (
            template.Category == "Ingredient" || 
            template.Category == "Ammo" || 
            template.Type== "Ingredient" ||
            template.Type == "Ammo"
            )
            return 0;
        var tier = template.Tier;
        if (template.Type == "Schematic" && tier == 0)
            tier = 1;
        if (tier == 0)
            return 0;

        var level = attributes?["level"]?.GetValue<int>() ?? -1;
        if (level < 0)
            return 0;

        var bonusMax = attributes?["max_level_bonus"]?.GetValue<int>() ?? 0;
        if (!template.HasLevel && tier == 5) //crafted weapons and traps dont have max_level_bonus attribute
            bonusMax = 10;
        if (template.Type == "Weapon" || template.Type == "Trap")
            bonusMax = Mathf.Max(0, level - (tier * 10));
        level = Mathf.Clamp(level, Mathf.Max(1, (tier * 10) - 10), (tier * 10) + bonusMax);
        string ratingCategory = template.Type == "Worker" ? (template.SubType is null ? "Survivor" : "LeadSurvivor") : "Default";

        int rarityLevel = template.RarityLevel;
        if (ratingCategory == "LeadSurvivor")
            rarityLevel -= 1;

        string ratingKey = template.GetCompactRarityAndTier(tier, rarityLevel);
        //if (ratingCategory == "LeadSurvivor")
        //    ratingKey = ratingKey.Replace("UR_", "SR_");

        var ratingSet = ratings[ratingCategory]?["Tiers"]?[ratingKey];
        if (ratingSet is null)
        {
            GD.PushWarning($"no rating set {ratingCategory}:{ratingKey}");
            return 0;
        }
        int ratingsLength = ratingSet["Ratings"]?.AsArray().Count ?? 0;
        int subLevel = level - ratingSet["FirstLevel"].GetValue<int>();
        if (subLevel < 0)
            return 0;
        if(subLevel>= ratingsLength)
        {
            GD.PushWarning($"{template.TemplateId} above range of ratings array ({subLevel}>={ratingsLength})");
            return 0;
        }
        var resultRating = (int)ratingSet["Ratings"][subLevel].GetValue<float>();
        return resultRating;
    }

    public int CalculateSurvivorRating(bool useSquad = true, string survivorSquad = null)
    {
        var rating = Rating;
        survivorSquad ??= attributes?["squad_id"]?.ToString();
        if (!useSquad || rating == 0 || (template.Type != "Worker" && survivorSquad is null))
            return rating;

        if (template.SubType is string leadType)
        {
            //check for lead synergy match
            var matchedSquadID = PegLegResourceManager.supplimentaryData.SynergyToSquadId.TryGetValue(leadType.Replace(" ", ""), out var match) ? match : null;
            if (matchedSquadID == survivorSquad)
                rating *= 2;
        }
        else if (profile?.profileId == FnProfileTypes.AccountItems)
        {
            var leadSurvivor = profile.GetItems("Worker", item =>
                item.attributes?["squad_id"]?.ToString() == survivorSquad &&
                item.attributes["squad_slot_idx"].GetValue<int>() == 0
            ).FirstOrDefault();

            string leaderRarity = leadSurvivor?.template.Rarity ?? "";
            int rarityBoost = leaderRarity switch
            {
                "Mythic" => 8,
                "Legendary" => 5,
                "Epic" => 4,
                "Rare" => 3,
                "Uncommon" => 2,
                "Common" => 1,
                _ => 2
            };

            int rarityPenalty = (leaderRarity == "Mythic") ? 2 : 0;

            string targetPersonality = leadSurvivor?.attributes["personality"].ToString().Split(".")[^1] ?? "";
            string currentPersonality = attributes["personality"].ToString().Split(".")[^1];

            rating += currentPersonality == targetPersonality ? rarityBoost : -rarityPenalty;
            rating = Mathf.Max(rating, 1);
        }

        return rating;
    }

    public Texture2D GetTexture(FnItemTextureType textureType = FnItemTextureType.Preview, bool largePreview = false) => 
        GetTexture(textureType, PegLegResourceManager.defaultIcon, largePreview);
    public Texture2D GetTexture(bool largePreview) =>
        GetTexture(FnItemTextureType.Preview, PegLegResourceManager.defaultIcon, largePreview);
    public Texture2D GetTexture(Texture2D fallbackIcon, bool largePreview = false) => 
        GetTexture(FnItemTextureType.Preview, fallbackIcon, largePreview);

    const string llamaDefaultPreviewImage = "PinataStandardPack";
    public static readonly Texture2D[] llamaTierIcons =
    [
        ResourceLoader.Load<Texture2D>("res://Images/Llamas/PinataStandardPack.png", "Texture2D"),
        ResourceLoader.Load<Texture2D>("res://Images/Llamas/PinataSilver.png", "Texture2D"),
        ResourceLoader.Load<Texture2D>("res://Images/Llamas/PinataGold.png", "Texture2D"),
    ];

    public Texture2D GetTexture(FnItemTextureType textureType, Texture2D fallbackIcon, bool largePreview = false)
    {
        if (textureType == FnItemTextureType.Personality)
            return GetPersonalityTexture(fallbackIcon);

        if (textureType == FnItemTextureType.SetBonus)
            return GetSetBonusTexture(fallbackIcon);

        if (textureType == FnItemTextureType.Preview && GameItemTemplate.Get(attributes?["portrait"]?.ToString()) is GameItemTemplate portraitTemplate)
            return portraitTemplate.GetTexture(fallbackIcon: fallbackIcon, largePreview: largePreview);
        if (template?.Type == "CardPack")
        {
            if (attributes?.ContainsKey("options") ?? false)
            {
                if (textureType == FnItemTextureType.Preview)
                    return llamaTierIcons[0];
                if (textureType == FnItemTextureType.PackImage)
                    textureType = FnItemTextureType.Preview;
            }
            else if (textureType == FnItemTextureType.PackImage && ((template.TryGetTexturePath(out var previewPath) && !previewPath.Contains("Pinata")) || template.DisplayName.Contains("Mini")))
                return null;
            else if (textureType == FnItemTextureType.Preview)
            {
                string llamaPinataName =
                    (template.TryGetTexturePath(out var imagePath) ? imagePath : null)
                    ?.ToString().Split("\\")[^1];
                if (llamaPinataName?.StartsWith(llamaDefaultPreviewImage) ?? false)
                {
                    int llamaTier = customData?["llamaTier"]?.GetValue<int>() ?? 0;
                    if (template.Rarity == "Legendary")
                        llamaTier = 2;//force gold tier for legendary rarity llamas
                    return llamaTierIcons[llamaTier];
                }
            }
        }

        return template?.GetTexture(textureType, fallbackIcon, largePreview);
    }
    
    Texture2D GetPersonalityTexture(Texture2D fallbackIcon = null)
    {
        if (template.Type != "Worker")
            return fallbackIcon;

        var personalityId = template.Personality ?? attributes?["personality"]?.ToString()?.Split(".")?[^1];

        if (personalityId is not null && PegLegResourceManager.supplimentaryData.PersonalityIcons.ContainsKey(personalityId))
            return PegLegResourceManager.supplimentaryData.PersonalityIcons[personalityId];

        return fallbackIcon;
    }

    Texture2D GetSetBonusTexture(Texture2D fallbackIcon = null)
    {
        if (template.Type != "Worker")
            return fallbackIcon;

        if (template.SubType is string subType)
        {
            subType = subType.Replace("Martial Artist", "MartialArtist");
            if (PegLegResourceManager.supplimentaryData.SquadIcons.ContainsKey(subType))
                return PegLegResourceManager.supplimentaryData.SquadIcons[subType];
        }
        else if(attributes?["set_bonus"]?.ToString()?.Split(".")?[^1] is string setBonus)
        {
            if (PegLegResourceManager.supplimentaryData.SetBonusIcons.ContainsKey(setBonus))
                return PegLegResourceManager.supplimentaryData.SetBonusIcons[setBonus];
        }

        return fallbackIcon;
    }
        

    public override string ToString() => $"{{\n  id:{uuid}\n  template:{templateId}\n  quantity:{quantity}\n  attributes:{attributes}\n  custom:{customData}\n}}";

    public GameItem GetUpgradedClone(int rarityUp, int tierUp)
    {
        GameItem newItemClone = Clone(useInspectorOverride: false);
        newItemClone.SetCloneUpgrade(rarityUp, tierUp);
        return newItemClone;
    }

    GameItemTemplate upgradeBasis;
    public void SetCloneUpgrade(int rarityUp, int tierUp)
    {
        if (upgradeBasis is null)
            return;
        var newTemplate = upgradeBasis;
        for (int i = 0; i < rarityUp; i++)
        {
            if (newTemplate.TryGetNextRarity() is not GameItemTemplate newRarity)
                break;
            newTemplate = newRarity;
        }
        for (int i = 0; i < tierUp; i++)
        {
            if (newTemplate.TryGetNextTier() is not GameItemTemplate newTier)
                break;
            newTemplate = newTier;
        }
        templateId = newTemplate.TemplateId;
        _template = newTemplate;
        ResetCachedData();
    }

    public GameItem Clone(int? quantity = null, JsonObject customData = null, bool useInspectorOverride = true) => 
        new(template, quantity ?? this.quantity, attributes.SafeDeepClone(), useInspectorOverride ? (profile is null ? inspectorOverride ?? this : this) : null, customData ?? this.customData.SafeDeepClone(), templateId);

    public void NotifyChanged()
    {
        ResetCachedData();
        OnChanged?.Invoke();
    }

    public void NotifyRemoving() => OnRemoving?.Invoke();
    public void DisconnectFromProfile()
    {
        profile = null;
        uuid = null;
        OnRemoved?.Invoke();
    }
}
