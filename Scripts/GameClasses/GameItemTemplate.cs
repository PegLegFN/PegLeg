using Godot;
using System;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

public enum FnItemTextureType
{
	Preview,
	Icon,
	LoadingScreen,
	PackImage,

	Personality,
	SetBonus
}

public static class HeroStats
{
	public const string MaxHealth = "FortHealthSet.MaxHealth";
	public const string MaxShields = "FortHealthSet.Shield";
	public const string HealthRegenRate = "FortRegenHealthSet.HealthRegenRate";
	public const string ShieldRegenRate = "FortRegenHealthSet.ShieldRegenRate";
	public const string AbilityDamage = "FortDamageSet.OutgoingBaseAbilityDamageMultiplier";
	public const string HealingModifier = "FortHealthSet.HealingSourceBaseMultiplier";
}

public static class SurvivorBonus
{
	public const string MaxHealth = "IsFortitudeLow";
	public const string MaxShields = "IsResistanceLow";
	public const string ShieldRegenRate = "IsShieldRegenLow";

	public const string RangedDamage = "IsRangedDamageLow";
	public const string MeleeDamage = "IsMeleeDamageLow";
	public const string AbilityDamage = "IsAbilityDamageLow";
	public const string TrapDamage = "IsTrapDamageLow";

	public const string TrapDurability = "IsTrapDurabilityHigh";
}

public partial class GameItemTemplate
{
	#region Static Values

	static Texture2D goldLlama = ResourceLoader.Load<Texture2D>("res://Images/Llamas/PinataGold.png", "Texture2D");

	public static string[] rarityIds =
	[
		null,
		"C",
		"UC",
		"R",
		"VR",
		"SR",
		"UR"
	];

	public static string[] tierIds =
	[
		"T00",
		"T01",
		"T02",
		"T03",
		"T04",
		"T05",
	];

	public static readonly Color[] rarityColours =
	[
		Colors.Transparent,
		Color.FromString("#bfbfbf", Colors.White),
		Color.FromString("#83db00", Colors.White),
		Color.FromString("#008bf1", Colors.White),
		Color.FromString("#a952ff", Colors.White),
		Color.FromString("#ff7b3d", Colors.White),
		Color.FromString("#ffff40", Colors.White),
	];

	static readonly string[] cardPackFromRarity =
	[
		"CardPack:cardpack_choice_all_r",
		"CardPack:cardpack_choice_all_r",
		"CardPack:cardpack_choice_all_r",
		"CardPack:cardpack_choice_all_r",
		"CardPack:cardpack_choice_all_vr",
		"CardPack:cardpack_choice_all_sr",
	];

	#endregion

	#region Static Methods

	static FrozenDictionary<string, GameItemTemplate> importedTemplates = null;
	public static void SetImportedTemplates(FrozenDictionary<string, GameItemTemplate> newImportedTemplates) =>
		importedTemplates = newImportedTemplates;

	static ConcurrentDictionary<string, GameItemTemplate> customTemplates = [];

	public static GameItemTemplate Get(string templateId)
	{
		if (templateId is null || templateId.Count(c => c == ':') != 1)
			return null;

		if (templateId.StartsWith("STWAccoladeReward"))
			templateId = templateId.Replace("STWAccoladeReward:stwaccolade_", "Accolades:accoladeid_stw_");

		//todo: move to gameitementry
		if (templateId == "AccountResource:currency_mtxswap")
			templateId = "AccountResource:currency_hybrid_mtx_xrayllama";

		if (customTemplates.TryGetValue(templateId, out var custom))
			return custom;

		if (importedTemplates?.TryGetValue(templateId, out var imported) ?? false)
			return imported;

		return null;
	}

	public static GameItemTemplate GetOrCreate(string templateId, Func<GameItemTemplate> constructor)
	{
		if (templateId is null || templateId.Count(c => c == ':') != 1)
			return null;

		if (Get(templateId) is GameItemTemplate foundTemplate)
			return foundTemplate;

		GameItemTemplate newTemplate = constructor();

		if (newTemplate is not null)
			lock (customTemplates)
			{
				bool exists = customTemplates.TryAdd(newTemplate.TemplateId, newTemplate);
				return exists ? customTemplates[newTemplate.TemplateId] : newTemplate;
			}

		return null;
	}

	public static IEnumerable<GameItemTemplate> GetTemplates()
	{
		return importedTemplates?.Union(customTemplates)?.Select(kvp => kvp.Value);
	}

	//probably pretty performance heavy, use sparingly
	public static IEnumerable<GameItemTemplate> GetTemplatesOfType(string templateType, Func<GameItemTemplate, bool> filter = null) =>
		importedTemplates?
		.Where(kvp =>
			kvp.Key.StartsWith(templateType + ":") &&
			filter.Try(kvp.Value
		))?
		.Union(customTemplates
			.Where(kvp =>
				kvp.Key.StartsWith(templateType + ":") &&
				filter.Try(kvp.Value)
			)
		)?
		.Select(kvp => kvp.Value) ?? [];

	public static Texture2D GetSubtypeTexture(string key, Texture2D fallbackIcon = null)
	{
		key ??= "";
		var dict = PegLegResourceManager.supplimentaryData.ItemTypeAndSubtypeIcons;
		if (dict.TryGetValue(key, out Texture2D value))
			return value;
		return fallbackIcon;
	}

	#endregion

	public GameItemTemplate(JsonObject rawData)
	{
		isReal = true;
		this.rawData = rawData;
	}

	public GameItemTemplate(string templateId = "Custom:item", string displayName = "Custom Item", string description = null, string iconPath = null, JsonObject extraData = null)
	{
		extraData ??= [];
		var splitTemplateId = templateId.Split(":");
		extraData["Type"] = splitTemplateId[0];
		extraData["Name"] = splitTemplateId[1];
		if (displayName is not null)
			extraData["DisplayName"] = displayName;
		if (description is not null)
			extraData["Description"] = description;
		if (iconPath is not null)
			extraData["ImagePaths"] = new JsonObject() { ["LargePreview"] = iconPath };
		rawData = extraData;
	}

	public bool isReal { get; private set; }
	public JsonObject rawData { get; private set; }
	public JsonNode this[string propertyName] => rawData[propertyName];
	public bool ContainsKey(string propertyName) => rawData.ContainsKey(propertyName);
	public string TemplateId => $"{Type}:{Name.ToLower()}";
	public bool VBucksOrXRayTickets => Type == "AccountResource" && Name.ToLower() is string lowername && (
			lowername == "currency_hybrid_mtx_xrayllama" ||
			lowername == "currency_mtxswap" ||
			lowername == "currency_xrayllama"
		);

	public string Type
	{
		get
		{
			//var type = rawData.TryGetPropertyValue("Type", out var typeNode) ? typeNode.ToString() : null;
			var type = rawData["Type"]?.ToString();
			if (type is null)
			{
				GD.Print("WOAH NELLY");
				return "";
			}
			return type;
		}
	}

	public bool IsCollectable => Type switch
	{
		"Hero" or "Worker" or "Defender" or "Schematic" => true,
		_ => false
	};

	public bool HasLevel => Tier > 0 && Type switch
	{
		"Hero" or "Worker" or "Weapon" or "Trap" => true,
		"Schematic" => !Unrecyclable || Category != "Trap",
		"Defender" => !Unrecyclable || RarityLevel > 1,
		_ => false
	};

	public bool CanBeLeveled => HasLevel && Type != "Weapon" && Type != "Trap";

	public bool CanBeSupercharged => CanBeLeveled && Type != "Defender" && RarityLevel >= 4;

	public bool CanBeUnseen => Type switch
	{
		"Hero" or "Worker" or "Defender" or "Schematic" or "Quest" or "AccountResource" or "ConsumableAccountItem" or "CardPack" => true,
		_ => false
	};
	public bool CanBeFavourited => Type switch
	{
		"Hero" or "Worker" or "Defender" or "Schematic" or "AccountResource" => true,
		_ => false
	};

	public string CollectionProfile => Type == "Schematic" ? FnProfileTypes.SchematicCollection : FnProfileTypes.PeopleCollection;
	public string Name => rawData["Name"].ToString();
	public string DisplayName => rawData["DisplayName"]?.ToString();
	public string SortingDisplayName => DisplayName.StartsWith("The ") ? DisplayName[4..] : DisplayName;
	public string Description => rawData["Description"]?.ToString();
	public string Category => rawData["Category"]?.ToString();
	public string SubType => rawData["SubType"]?.ToString();
	public string Rarity => rawData["Rarity"]?.ToString();
	public int RarityLevel => (Rarity ?? "").ConvertRarityString();
	public Color RarityColor => Name.StartsWith("ZCP_") ? Colors.Transparent : rarityColours[RarityLevel];

	public int Tier => rawData["Tier"]?.GetValue<int>() ?? 0;
	public int MaxTier => Mathf.Min(RarityLevel + 1, 5);

	public string Personality => rawData["Personality"]?.ToString();

	public bool Unrecyclable => rawData["RecycleRecipe"] is null;
	public bool Undismantlable => rawData["DismantleResults"] is null;

	AlterationSlot[] alterationSlots;
	public AlterationSlot[] AlterationSlots => alterationSlots ??= AlterationSlot.SlotsFromRow(
		rawData["AlterationLoadoutRow"]?.ToString(),
		rawData["AlterationNamedExclusions"]?.Deserialize<string[]>() ?? []
	);

	FrozenSet<string> heroTags = null;
	public FrozenSet<string> HeroTags => heroTags ??= [.. rawData["HeroTags"]?.Deserialize<string[]>() ?? []];

	Dictionary<FnItemTextureType, Texture2D> persistantTextureCache = [];

	private Texture2D CustomGetTexture(ref FnItemTextureType textureType, Texture2D fallbackIcon, bool largePreview = false)
	{
		if (persistantTextureCache.TryGetValue(textureType, out var cachedTex) && (!largePreview || textureType != FnItemTextureType.Preview))
			return cachedTex;

		if ((Type == "TeamPerk" || Type == "Ability") && textureType == FnItemTextureType.Preview)
			textureType = FnItemTextureType.Icon;

		if (Type == "Worker" &&
			(
				rawData["ImagePaths"]?
				["SmallPreview"]?
				.ToString()
				.Contains("GenericWorker") ?? false
			))
			return GetSubtypeTexture(SubType ?? "Survivor", fallbackIcon);

		if
		(
			Type == "CardPack" &&
			textureType == FnItemTextureType.Preview &&
			DisplayName.Contains("Legendary") &&
			DisplayName.Contains("Llama") &&
			!Name.StartsWith("ZCP_")
		)
			return goldLlama;
		return null;
	}
	public Texture2D GetTexture(FnItemTextureType textureType = FnItemTextureType.Preview, bool largePreview = false) => GetTexture(textureType, PegLegResourceManager.defaultIcon, largePreview);
	public Texture2D GetTexture(Texture2D fallbackIcon, bool largePreview = false) => GetTexture(FnItemTextureType.Preview, fallbackIcon, largePreview);
	public Texture2D GetTexture(FnItemTextureType textureType, Texture2D fallbackIcon, bool largePreview = false)
	{
		if (CustomGetTexture(ref textureType, fallbackIcon, largePreview) is Texture2D loadedTexture)
			return loadedTexture;
		if (!TryGetTexturePath(out var texturePath, out var wasLargePreview, textureType, largePreview))
			return fallbackIcon;
		var loadedTex = PegLegResourceManager.LoadResourceAsset<Texture2D>("GameAssets/" + texturePath);
		if (loadedTex is not null && !wasLargePreview)
			persistantTextureCache[textureType] = loadedTex;
		return loadedTex ?? fallbackIcon;
	}

	public Task<Texture2D> GetTextureAsync(FnItemTextureType textureType = FnItemTextureType.Preview, bool largePreview = false, Action<float> onProgress = null) => GetTextureAsync(textureType, PegLegResourceManager.defaultIcon, largePreview, onProgress);
	public Task<Texture2D> GetTextureAsync(Texture2D fallbackIcon, bool largePreview = false, Action<float> onProgress = null) => GetTextureAsync(FnItemTextureType.Preview, fallbackIcon, largePreview, onProgress);
	public async Task<Texture2D> GetTextureAsync(FnItemTextureType textureType, Texture2D fallbackIcon, bool largePreview = false, Action<float> onProgress = null)
	{
		if (CustomGetTexture(ref textureType, fallbackIcon, largePreview) is Texture2D loadedTexture)
			return loadedTexture;
		if (!TryGetTexturePath(out var texturePath, out var wasLargePreview, textureType, largePreview))
			return fallbackIcon;
		var loadedTex = await PegLegResourceManager.LoadResourceAssetAsync<Texture2D>("GameAssets/" + texturePath, onProgress);
		if (loadedTex is not null && !wasLargePreview)
			persistantTextureCache[textureType] = loadedTex;
		return loadedTex ?? fallbackIcon;
	}


	public bool TryGetTexturePath(out string foundPath, FnItemTextureType textureType = FnItemTextureType.Preview) =>
		TryGetTexturePath(out foundPath, out _, textureType, false);


	public bool TryGetTexturePath(out string foundPath, out bool wasLargePreview, FnItemTextureType textureType, bool preferLargePreview)
	{
		foundPath = null;
		wasLargePreview = false;
		JsonObject imagePaths = rawData["ImagePaths"]?.AsObject();
		if (imagePaths is null)
			return false;

		if (textureType == FnItemTextureType.Preview)
		{
			if (preferLargePreview)
			{
				wasLargePreview = imagePaths["LargePreview"] is not null;
				foundPath = (imagePaths["LargePreview"] ?? imagePaths["SmallPreview"])?.ToString();
			}
			else
				foundPath = (imagePaths["SmallPreview"] ?? imagePaths["LargePreview"])?.ToString();
		}
		else
			foundPath = imagePaths[textureType.ToString()]?.ToString();

		if (string.IsNullOrWhiteSpace(foundPath) || !foundPath.StartsWith("ExportedImages"))
			return false;
		return true;
	}

	public Texture2D GetSubtypeTexture(Texture2D fallbackIcon = null)
	{
		switch (Type)
		{
			case "Schematic":
				if (Category == "Trap")
					return GetSubtypeTexture("Trap", fallbackIcon);
				else
					return GetSubtypeTexture(SubType, fallbackIcon);
			case "Worker":
				if (rawData["ImagePaths"]?["SmallPreview"]?.ToString().Contains("GenericWorker") ?? false)
					return null;
				else
					return GetSubtypeTexture(SubType ?? "Survivor", fallbackIcon);
			case "Trap":
				return GetSubtypeTexture("Trap", fallbackIcon);
			default:
				return GetSubtypeTexture(SubType, fallbackIcon);
		}
	}

	public GameItemTemplate TryGetNextRarity()
	{
		if (rawData["RarityUpRecipe"]?["Result"]?.ToString() is string rarityUpResult)
			return Get(rarityUpResult);
		return null;
	}

	public GameItemTemplate TryGetMaxRarity()
	{
		var current = this;
		while (current.TryGetNextRarity() is GameItemTemplate next)
			current = next;
		return current;
	}

	public GameItemTemplate TryGetNextTier()
	{
		if (rawData["TierUpRecipe"]?["Result"]?.ToString() is string tierUpResult)
			return Get(tierUpResult);
		return null;
	}

	//note when migrating to blakebeard: this returns the combined RECYCLE value (excluding manuals), since a Lv1 item has a
	//base XP value. Prob worth subtracting the base XP value from the returned costs for clarity, and adding it back along
	//with the manuals when calculating the recycle value of the template
	public GameItem.ItemData[] GetCombinedUpgradeValue(int ofLevel)
	{
		if (Tier <= 1)
		{
			if (TryGetCombinedLevelUpCost(ofLevel, out var levelUpCostOnly))
				return [levelUpCostOnly];
			return [];
		}

		Dictionary<string, int> totalCosts = [];
		var currentTemplate = Get(TierSuffix().Replace(TemplateId, "_t01"));
		while (currentTemplate != null && currentTemplate != this)
		{
			var toAdd = currentTemplate["TierUpRecipe"]?["Cost"].Deserialize<Dictionary<string, int>>() ?? [];
			foreach (var item in toAdd)
			{
				if (totalCosts.ContainsKey(item.Key))
					totalCosts[item.Key] += item.Value;
				else
					totalCosts[item.Key] = item.Value;
			}
			currentTemplate = currentTemplate.TryGetNextTier();
		}

		if (TryGetCombinedLevelUpCost(ofLevel, out var levelUpCost))
		{
			if (totalCosts.ContainsKey(levelUpCost.templateId))
				totalCosts[levelUpCost.templateId] += levelUpCost.quantity;
			else
				totalCosts[levelUpCost.templateId] = levelUpCost.quantity;
		}

		return [.. totalCosts.Select(kvp => new GameItem.ItemData() { templateId = kvp.Key, quantity = kvp.Value })];
	}

	public bool TryGetRarityUpCost(out GameItem.ItemData[] cost)
	{
		cost = [];
		var costDict = rawData["RarityUpRecipe"]?["Cost"].Deserialize<Dictionary<string, int>>();
		if (costDict is null)
			return false;
		Dictionary<string, int> totalCosts = [];
		foreach (var item in costDict)
		{
			if (totalCosts.ContainsKey(item.Key))
				totalCosts[item.Key] += item.Value;
			else
				totalCosts[item.Key] = item.Value;
		}
		cost = [.. totalCosts.Select(kvp => new GameItem.ItemData() { templateId = kvp.Key, quantity = kvp.Value })];
		return true;
	}

	//ignores upgrades
	public bool TryGetCombinedLevelUpCost(int ofLevel, out GameItem.ItemData cost)
	{
		cost = default;
		if (ofLevel < 1)
			return false;
		int rarityLv = RarityLevel;
		if (Type == "Worker" && SubType is not null)
			rarityLv -= 1;//for some reason Lead Survivors are treated as one rarity lower

		string category = RarityLevel switch
		{
			1 => "Common",
			2 => "Uncommon",
			3 => "Rare",
			4 => "VeryRare",
			5 => "SuperRare",
			6 => "UltraRare",
			_ => null
		};
		if (Type == "Worker" && SubType is not null)
			category = $"Manager_{category}";
		else if (Type == "Defender")
			category = $"Defender_{category}";

		var levels = PegLegResourceManager.ItemLevelsToXP[category]?.Deserialize<int[]>() ?? [];
		if (levels.Length == 0)
			return false;

		int resolvedLevel = 0;
		if (ofLevel >= levels.Length - 1)
			resolvedLevel = levels[^1];
		else if (ofLevel > 0)
			resolvedLevel = levels[ofLevel - 1];

		var xpType = "AccountResource:peoplexp";
		if (Type == "Schematic")
			xpType = "AccountResource:schematicxp";

		cost = new()
		{
			templateId = xpType,
			quantity = resolvedLevel,
		};

		return true;
	}

	public Texture2D GetAmmoTexture(Texture2D fallbackIcon = null)
	{
		if (Type != "Schematic" && Type != "Weapon" && Type != "Trap")
			return fallbackIcon;

		if (Category == "Trap" || Type == "Trap")
			return GetSubtypeTexture(SubType, fallbackIcon);

		if (
			rawData["RangedWeaponStats"]?["AmmoType"]?.ToString() is string ammoType &&
			PegLegResourceManager.supplimentaryData.AmmoIcons.TryGetValue(ammoType.Split(" ")[0], out Texture2D value)
			)
			return value;

		return fallbackIcon;
	}

	public string GetCompactRarityAndTier(int givenTier = 0, int givenRarity = 0)
	{
		var rarityId = rarityIds[givenRarity <= 0 ? RarityLevel : givenRarity];
		var tierId = tierIds[givenTier <= 0 ? Tier : givenTier];
		return rarityId + "_" + tierId;
	}

	public string GetTemplatePrefix(bool includeRarity = false)
	{
		string tid = TemplateId;
		foreach (var tier in tierIds)
		{
			if (tid.EndsWith($"_{tier.ToLower()}"))
			{
				tid = tid[..^3];
				break;
			}
		}
		if (tid.Contains("_ore_"))
			tid = tid.Replace("_ore_", "_");
		if (tid.Contains("_crystal_"))
			tid = tid.Replace("_crystal_", "_");
		if (includeRarity)
			return tid;
		foreach (var rarity in rarityIds)
		{
			if (rarity is null)
				continue;
			if (tid.EndsWith($"_{rarity.ToLower()}_"))
			{
				tid = tid[..^(rarity.Length)];
				break;
			}
		}
		return tid;
	}

	GameItemTemplate[] heroAbilities;
	public GameItemTemplate[] GetHeroAbilities()
	{
		if (Type != "Hero")
			return null;
		return heroAbilities ??=
		[
			Get(rawData["HeroPerkTemplate"]?.ToString()),
			Get(rawData["CommanderPerkTemplate"]?.ToString()),
			Get(rawData["HeroAbilities"]?[0].ToString()),
			Get(rawData["HeroAbilities"]?[1].ToString()),
			Get(rawData["HeroAbilities"]?[2].ToString()),
		];
	}

	GameItemTemplate teamPerk;
	public GameItemTemplate GetTeamPerk()
	{
		if (Type != "Hero")
			return null;
		return teamPerk ??= Get(rawData["UnlocksTeamPerk"]?.ToString());
	}

	GameItem[] questRewards;
	GameItem[] visibleQuestRewards;
	GameItem[] hiddenQuestRewards;
	public GameItem[] GetQuestRewards()
	{
		if (Type != "Quest")
			return null;
		return questRewards ??= [.. GetVisibleQuestRewards().Union(GetHiddenQuestRewards())];
	}

	public GameItem[] GetVisibleQuestRewards()
	{
		if (Type != "Quest")
			return null;
		return visibleQuestRewards ??= GenerateQuestRewards(false);
	}

	public GameItem[] GetHiddenQuestRewards()
	{
		if (Type != "Quest")
			return null;
		return hiddenQuestRewards ??= GenerateQuestRewards(true);
	}

	GameItem[] GenerateQuestRewards(bool hidden)
	{
		var allRewards = rawData["Rewards"]
			.AsArray()
			.Where(r => r["Hidden"].GetValue<bool>() == hidden);

		var rewards = allRewards
			.Where(r => !r["Selectable"].GetValue<bool>())
			.Select(r => Get(r["Item"].ToString())?.CreateInstance(r["Quantity"].GetValue<int>()))
			.Where(r => r is not null)
			.ToList();

		var dynamicRewards = allRewards
			.Where(r => r["Selectable"].GetValue<bool>());

		if (dynamicRewards.Any())
		{
			//fake a cardpack to show a choice reward
			var cardpackID = cardPackFromRarity[dynamicRewards.Select(q => Get(q["Item"]?.ToString())?.RarityLevel ?? 0).Max()];
			JsonObject attributes = new()
			{
				["options"] = new JsonArray([.. dynamicRewards.Select(r => new JsonObject()
				{
					["itemType"] = r["Item"].ToString(),
					["attributes"] = new JsonObject(),
					["quantity"] = r["Quantity"].GetValue<int>()
				})]),
				["quest_selectable"] = true
			};
			var choiceReward = Get(cardpackID).CreateInstance(1, attributes);
			rewards.Insert(0, choiceReward);
		}
		return [.. rewards];
	}

	static Dictionary<string, GameItem> gadgetSingletonLookup = [];
	static Dictionary<string, string> gadgetNodeMap = new(StringComparer.OrdinalIgnoreCase)
	{
		["g_airstrike"] = "skilltree_airstrike",
		["g_generic_adrenalinerush"] = "skilltree_adrenalinerush",
		["g_generic_banner"] = "skilltree_banner",
		["g_generic_botturret"] = "skilltree_hoverturret",
		["g_generic_proximitymines"] = "skilltree_proximitymine",
		["g_generic_slowfield"] = "skilltree_slowfield",
		["g_supplydrop"] = "skilltree_supplydrop",
		["g_teleporter"] = "skilltree_teleporter",
	};

	public GameItem GadgetSingleton =>
		gadgetSingletonLookup.TryGetValue(TemplateId, out var value) ?
		value :
		(gadgetSingletonLookup[TemplateId] = CreateInstance().SetUUID());

	public bool HomebaseNodeForGadget(out string nodeTemplateId)
	{
		nodeTemplateId = null;
		if (Type != "Gadget")
			return false;
		if (gadgetNodeMap.TryGetValue(Name, out var nodeName))
		{
			nodeTemplateId = $"HomebaseNode:{nodeName}";
			return true;
		}
		return false;
	}

	CommanderRequirement? commanderReq;
	public bool PerkCompatibleWithCommander(GameItemTemplate commanderTemplate, out string warning)
	{
		warning = null;
		if (commanderTemplate?.Type != "Hero" || (Type != "Hero" && Type != "TeamPerk"))
			return false;
		commanderReq ??= rawData[Type == "TeamPerk" ? "CommanderRequirement" : "HeroPerkRequirement"]?
			.Deserialize<CommanderRequirement>(Helpers.JsonOptions.Fields);
		if (commanderReq?.IsMatch(commanderTemplate) != false)
			return true;
		warning = commanderReq?.Description;
		return false;
	}

	TeamPerkSupportRequirements? teamperkReq;
	public bool TeamPerkBoostedByHero(GameItemTemplate heroTemplate)
	{
		if (heroTemplate?.Type != "Hero" || Type != "TeamPerk")
			return false;
		teamperkReq ??= rawData["SupportRequirements"]?
			.Deserialize<TeamPerkSupportRequirements>(Helpers.JsonOptions.Fields);
		if (teamperkReq?.IsMatch(heroTemplate) == true)
			return true;
		return false;
	}

	public int TeamPerkMinRequirements => (teamperkReq ??= rawData["SupportRequirements"]?.Deserialize<TeamPerkSupportRequirements>(Helpers.JsonOptions.Fields)).Value.MinimumQuantity;

	struct CommanderRequirement
	{
#pragma warning disable CS0649 //Field is never assigned to, and will always have its default value
		public string Description;
		public string[] CommanderTag;
		public string CommanderSubType;
#pragma warning restore CS0649 //Field is never assigned to, and will always have its default value

		public bool IsMatch(GameItemTemplate template)
		{
			if (template is null)
				return false;
			if (CommanderSubType is not null && template.SubType != CommanderSubType)
				return false;
			else if (CommanderTag is not null)
			{
				var targetTags = CommanderTag.ToHashSet();
				var commanderTags = template["HeroTags"]?.Deserialize<string[]>().ToHashSet();
				if (targetTags.All(t => !commanderTags.Contains(t)))
					return false;
			}

			return true;
		}
	}

	struct TeamPerkSupportRequirements()
	{
#pragma warning disable CS0649 //Field is never assigned to, and will always have its default value
		public string Description;
		public int MinimumQuantity = 1;
		public string[] HeroTags;
		public string HeroSubType;
		public int? MinimumTier;
		public string MinimumRarity;
#pragma warning restore CS0649 //Field is never assigned to, and will always have its default value

		public bool IsMatch(GameItemTemplate template)
		{
			if (template is null)
				return false;

			if (HeroSubType is not null && template.SubType != HeroSubType)
				return false;

			if (HeroTags is not null && HeroTags.Length > 0)
			{
				var targetTags = HeroTags.ToHashSet();
				var heroTags = template.HeroTags;
				if (targetTags.All(t => !heroTags.Contains(t)))
					return false;
			}

			if (MinimumTier is int tier && template.Tier < tier)
				return false;

			if (MinimumRarity is not null && template.RarityLevel < MinimumRarity.ConvertRarityString())
				return false;

			return true;
		}
	}

	public struct AlterationSlot
	{
		public GameItem.ItemData[] respecCost { get; private set; }
		public string[] options { get; private set; }
		public string[] OptionsForLevel(int level) => [.. options.Select(o => o.EndsWith("_t01") ? $"{o[..^4]}_t0{level}" : o)];
		public int requiredLevel { get; private set; }
		public string requiredRarity { get; private set; }
		public int RequiredRarityLevel => requiredRarity.ConvertRarityString();

		public static AlterationSlot[] SlotsFromRow(string alterationSlotRow, string[] exclusions = null)
		{
			if (alterationSlotRow is null)
				return [];
			var row = PegLegResourceManager.AlterationLoadouts[alterationSlotRow].AsArray();
			var exclusionSet = (exclusions ?? []).ToHashSet();
			return [..row?
				.Select(slot => new AlterationSlot()
				{
					options = [..slot["RawAlterations"]
						.AsArray()
						.Where(a => !exclusionSet.Overlaps(a["ExclusionNames"].Deserialize<string[]>()))
						.Select(a => a["AID"].ToString())
					],
					requiredLevel = slot["RequiredLevel"].GetValue<int>(),
					requiredRarity = slot["RequiredRarity"].ToString(),
					respecCost = [..(slot["BaseRespecCost"]?.Deserialize<Dictionary<string, int>>()??[])
						.Select(kvp=>new GameItem.ItemData(kvp.Key, kvp.Value))
					],
				})
			];
		}
	}

	public JsonArray GenerateSearchTags(bool assumeUncommon = true)
	{
		if (rawData["searchTags"] is JsonArray existingSearchTags)
			return existingSearchTags;

		List<string> tags =
		[
			$"hidetag_{DisplayName}",
			//$"hidetag_{Description}",
            Rarity ?? (assumeUncommon ? "Uncommon" : null),
			Type,
			SubType,
			Category,
			Personality?[2..]
		];

		if (GetHeroAbilities() is GameItemTemplate[] abilities)
		{
			foreach (var ability in abilities)
			{
				if (!ability?.DisplayName?.EndsWith('+') ?? false)
					tags.Add(ability.DisplayName);
				//if (ability["PreferredQuickbarSlot"] is null)
				//	tags.Add($"hidetag_{ability.Description}");
			}
		}
		if (GetTeamPerk() is GameItemTemplate teamPerk)
			tags.Add(teamPerk.DisplayName);

		if (tags.Contains("Worker"))
			tags.Add("Survivor");
		if (Unrecyclable && Undismantlable && Type != "AccountResource" && Type != "CardPack") //"Permanent" is misleading for Account Resources and Card Packs, since they can be spent and opened respectively
			tags.Add("Permanent");
		var searchTags = new JsonArray(tags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => (JsonNode)t).ToArray());
		lock (rawData)
		{
			rawData["RarityLv"] = RarityLevel;
			rawData["searchTags"] = searchTags;
		}
		return searchTags;
	}

	public GameItem CreateInstance(int quantity = 1, JsonObject attributes = null, GameItem inspectorOverride = null, JsonObject customData = null)
	{
		customData ??= [];
		customData["generated_by_pegleg"] = true;
		return new(this, quantity, attributes, inspectorOverride, customData);
	}

	public GameItem PriceForItem()
	{
		int quantity = Type switch
		{
			"Hero" => Rarity switch
			{
				"Mythic" => 3200,
				"Legendary" => 2800,
				"Epic" => 1000,
				_ => 100
			},
			"Schematic" => Rarity switch
			{
				"Legendary" => 1680,
				"Epic" => 600,
				_ => 100
			},
			_ => 100
		};
		return Get("AccountResource:eventcurrency_scaling").CreateInstance(quantity);
	}

	public GameOffer CreateOffer(GameItem price = null, int quantity = 1, int limit = 1, JsonObject rawData = null) =>
		GameOffer.CreateFake([CreateInstance(quantity)], price ?? PriceForItem(), limit, rawData);

	[GeneratedRegex("_(?:c|uc|r|vr|sr|ur)_")]
	public static partial Regex RaritySuffix();

	[GeneratedRegex("_t0\\d")]
	public static partial Regex TierSuffix();
}

