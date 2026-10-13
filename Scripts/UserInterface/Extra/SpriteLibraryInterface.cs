using Godot;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

public partial class SpriteLibraryInterface : Control
{
	static SpriteLibraryInterface instance;
	static Dictionary<string, string> modules;
	static string currentModule;
	static string currentGen;
	static Dictionary<string, GameItemTemplate> spriteTemplates;
	static bool hasLinkedToFileHandler = false;

	const string spriteArchiveType = "spriteArchive";

	[Export]
	Control listHandlerNode;
	[Export]
	LineEdit searchBar;
	[Export]
	VirtualTabBar generationFilter;
	[Export]
	VirtualTabBar ownershipFilter;
	[Export]
	VirtualTabBar masteryFilter;
	[Export]
	Control compareInfo;
	[Export]
	Label compareOwner;
	[Export]
	Button neededToggle;

	[Export]
	FileDialog manualImporter;

	IListHandler listHandler;
	Dictionary<string, int> catalog = [];
	List<GameItem> accountSprites = [];
	EntryList<GameItem> sprites = [];

	PLSearch.Instruction[] searchInstructions;

	public override void _Ready()
	{
		if (!hasLinkedToFileHandler)
		{
			hasLinkedToFileHandler = true;
			FileDistributer.RegisterFileHandler<SpriteArchive>(spriteArchiveType, a =>
			{
				if (instance is null)
					return false;
				//switch to tab
				instance.SetComparison(a);
				instance.Visible = true;
				instance.GetParentOrNull<Control>()?.Visible = true;
				return true;
			});
		}

		instance = this;
		if (listHandlerNode is IListHandler handler)
		{
			listHandler = handler;
			listHandler.LinkListProvider(sprites);
		}

		modules ??= PegLegResourceManager.MagicNumbers["sprites"]?["modules"].Deserialize<Dictionary<string, string>>();
		currentModule ??= modules.LastOrDefault().Value;
		currentGen ??= modules.LastOrDefault().Key;
		spriteTemplates ??= GameItemTemplate.GetTemplatesOfType("ExtractableRelic").ToDictionary(t => t["MappingID"]?.ToString() ?? t.Name);

		searchBar.TextChanged += _ => UpdateSearch();

		generationFilter.SetTabContents([..modules.Keys.Select(gen => new VirtualTabBar.TabData()
		{
			text = gen
		})]);
		generationFilter.ClearTabs();
		generationFilter.Visible = modules.Count > 0;
		generationFilter.TabsChanged += FilterSprites;
		ownershipFilter.TabsChanged += FilterSprites;
		masteryFilter.TabsChanged += FilterSprites;
		neededToggle.Disabled = true;
		neededToggle.Toggled += _ => FilterSprites();

		manualImporter.FileSelected += ManualImport;
		compareInfo.Visible = false;

		GameAccount.ActiveAccountChanged += UpdateAccount;
		UpdateAccount();
	}

	public override void _ExitTree()
	{
		instance = null;
		GameAccount.ActiveAccountChanged -= UpdateAccount;
	}

	private void ManualImport(string path)
	{
		using var peglegDataFile = FileAccess.Open(path, FileAccess.ModeFlags.Read);
		var dataFileContent = JsonSerializer.Deserialize<FileDistributer.PegLegDataFile>(peglegDataFile.GetAsText());
		SetComparison(dataFileContent.content.Deserialize<SpriteArchive>());
	}

	void ClearComparison()
	{
		compareArchive = null;
		neededToggle.Disabled = true;
		compareInfo.Visible = false;
		UpdateComparison();
	}

	void SetComparison(SpriteArchive archive)
	{
		if (archive.Generation != currentGen)
			return; // show error for outdated archive
		compareArchive = archive;
		neededToggle.Disabled = false;
		compareInfo.Visible = true;
		compareOwner.Text = archive.DisplayName;
		UpdateComparison();
	}

	SpriteArchive? compareArchive;
	void UpdateComparison()
	{
		var archive = compareArchive ?? default;
		var hasArchive = compareArchive is not null;
		if (hasArchive)
		{
			archive = archive with
			{
				HasCollected = archive.HasCollected ?? [],
				HasMastered = archive.HasMastered ?? [],
				HasMaxxed = archive.HasMaxxed ?? [],
				HasSprites = archive.HasSprites ?? [],
			};
		}
		foreach (var item in accountSprites)
		{
			item.customData["needed"] = 0;
			if (!hasArchive || item.template?["Generation"].ToString() != currentGen)
			{
				item.NotifyChanged();
				continue;
			}
			if (item.quantity == 2)
			{
				bool isMaxxed = (item.attributes?["xp"]?.GetValue<int>() ?? 0) >= 4000;
				bool mastered = item.attributes?["ml"]?.GetValue<bool>() == true;

				//you have a Lv5 sprite that the archive hasnt mastered
				if (isMaxxed && !archive.HasMastered.Contains(item.templateId))
					item.customData["needed"] = 2;
				//you have a sprite that the archive hasnt collected
				else if (!archive.HasCollected.Contains(item.templateId))
					item.customData["needed"] = 2;
				//the archive has a Lv5 sprite that you have not mastered
				else if (!mastered && archive.HasMaxxed.Contains(item.templateId))
					item.customData["needed"] = 1;
			}
			else if (item.quantity == 1)
			{
				bool mastered = item.attributes?["ml"]?.GetValue<bool>() == true;
				//you have collected a sprite that the archive hasnt
				if (!archive.HasCollected.Contains(item.templateId))
					item.customData["needed"] = 2;
				//the archive has a Lv5 sprite that you have not mastered
				else if (!mastered && archive.HasMaxxed.Contains(item.templateId))
					item.customData["needed"] = 1;
			}
			else
			{
				// the archive has a sprite that you have not collected
				if (archive.HasCollected.Contains(item.templateId))
					item.customData["needed"] = 1;
			}
			item.NotifyChanged();
		}
		FilterSprites();
	}

	public void ExportArchive()
	{
		GameItem[] genSprites = [.. accountSprites.Where(s => s.template["Generation"].ToString() == currentGen)];
		SpriteArchive archive = new()
		{
			DisplayName = GameAccount.ActiveAccount.DisplayName,
			Generation = currentGen,
			HasCollected = [.. genSprites.Where(s => s.quantity == 1 || s.quantity == 2).Select(s => s.templateId)],
			HasMastered = [.. genSprites.Where(s => s.attributes?["ml"]?.GetValue<bool>() == true).Select(s => s.templateId)],
			HasMaxxed = [.. genSprites.Where(s => s.quantity == 2 && (s.attributes?["xp"]?.GetValue<int>() ?? 0) >= 4000).Select(s => s.templateId)],
			HasSprites = [.. genSprites.Where(s => s.quantity == 2).Select(s => s.templateId)],
		};
		FileDistributer.SaveFile(spriteArchiveType, archive, $"{GameAccount.ActiveAccount.DisplayName}_SpriteArchive");
	}

	GameAccount prevAccount;
	async void UpdateAccount()
	{
		sprites.Clear();
		accountSprites.Clear();
		listHandler.UpdateList();
		var account = GameAccount.ActiveAccount;
		bool clearCompare = false;
		if (prevAccount != account)
		{
			clearCompare = true;
			prevAccount = account;
		}
		await account.AuthenticateEOS();

		catalog = await GetSpriteCatalog();
		List<GameItem> proxySprites = [];
		foreach (var moduleId in modules.Values)
		{
			var response = await FnWebAddresses.Magpie.MakeRequest(
				$"/api/magpie/v2/deployment/{GameClient.FortEOSDeployment}/domain/FN1/account/{account.accountId}/workspace/default/linkMode/live/inventory" +
				$"?moduleFilters={moduleId}:*&includeMetadata=true"
			).SetAccount(account, true).Send();
			if (await response.CheckForError())
				continue;
			if ((await response.ReadJson())?["inventory"]?.AsArray().FirstOrDefault()?.Deserialize<MagpieInventory>() is not { } inventory)
				continue;
			proxySprites.AddRange(inventory.CreateItems(catalog));
		}
		var ownedTemplates = proxySprites
			.Select(s => s.templateId)
			.ToHashSet();
		var catalogTemplates = catalog.Keys .ToDictionary(key => key, spriteTemplates.GetValueOrDefault);
		var unownedSprites = catalogTemplates.Values
			.Where(t => t is not null && !ownedTemplates.Contains(t?.TemplateId))
			.Select(t => t.CreateInstance(3));
		proxySprites.AddRange(unownedSprites);

		accountSprites.AddRange(proxySprites);
		UpdateComparison();
		SortSprites();
		if(clearCompare)
			ClearComparison();
	}

	async Task<Dictionary<string, int>> GetSpriteCatalog()
	{
		int initVersion = AppConfig.Get("advanced", "spriteCatalogVer", 1);
		var account = GameAccount.ActiveAccount;
		Dictionary<string, MagpieCatalogEntry> catalog = null;
		int version = initVersion;
		while (true)
		{
			var response = await FnWebAddresses.MagpieCatalog.MakeRequest($"/fortnite/api/game/v2/extractablerelics/{currentModule}/{version}/getBackendCatalog/")
				.SetAccount(account)
				.Send();
			if (await response.CheckForError(false))
			{
				if(catalog is not null)
					break;
				if (version == 1)
				{
					GD.PushWarning("No Sprite Catalog Version Found");
					return [];
				}
				version = 1;
				continue;
			}
			if ((await response.ReadJson<Dictionary<string, MagpieCatalogEntry>>()) is not { } responseCatalog)
			{
				GD.PushWarning("Invalid Sprite Catalog Format");
				return [];
			}
			catalog = responseCatalog;
			version++;
			continue;
		}
		if (initVersion != version - 1)
			AppConfig.Set("advanced", "spriteCatalogVer", version - 1);
		return catalog.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.attributes?["summonCost"]?.GetValue<int>() ?? -1);
	}

	public void SortSprites()
	{
		accountSprites = [.. accountSprites
			.OrderByDescending(s => s.template?["Generation"]?.ToString())
			.ThenBy(s => s.template?["SpriteDex"]?.GetValue<int>() ?? 999)
			.ThenBy(s => s.template?.RarityLevel ?? 999)
			.ThenBy(s => s.template?["Parent"]?.ToString())
			.ThenBy(s =>
			{
				var augs = s.template?["Augments"]?.AsArray();
				return augs.Count > 1 ? augs[1].ToString() : "";
			})
		];
		FilterSprites();
	}

	public void UpdateSearch()
	{
		searchInstructions = PLSearch.GenerateSearchInstructions(searchBar.Text);
		FilterSprites();
	}

	public void FilterSprites()
	{
		if (accountSprites.Count == 0)
			return;
		var genFilters = generationFilter.PressedTabs.Select(t => t.Text.ToLower()).ToHashSet();
		var quantityFilters = ownershipFilter.PressedTabIndexes.Select(i => i switch
		{
			0 => 3,
			_ => i
		}).ToHashSet();
		var masteryIndexes = masteryFilter.PressedTabIndexes;
		bool useMastery = masteryIndexes.Length == 1;
		bool requireMastered = masteryIndexes.FirstOrDefault() == 1;

		sprites.Clear();
		sprites.AddRange(accountSprites.Where(s =>
		{
			if (genFilters.Count > 0 && !genFilters.Contains(s.template?["Generation"]?.ToString().ToLower()))
				return false;
			if (quantityFilters.Count > 0 && !quantityFilters.Contains(s.quantity))
				return false;
			if (useMastery && requireMastered != (s.attributes?["ml"]?.GetValue<bool>() ?? false))
				return false;
			if (!neededToggle.Disabled && neededToggle.ButtonPressed && (s.customData["needed"]?.GetValue<int>() ?? 0) == 0)
				return false;
			if (searchInstructions is not null)
				return PLSearch.EvaluateInstructions(searchInstructions, s.RawData);
			return true;
		}));
		listHandler.UpdateList();
	}

	struct MagpieCatalogEntry
	{
		public JsonObject attributes { get; init; }
		public string templateId { get; init; }
	}

	struct MagpieInventory
	{
		public Dictionary<string, int> counts { get; init; }
		public Dictionary<string, string> entitlementMetadata { get; init; }
		public GameItem[] CreateItems(Dictionary<string, int> catalog = null)
		{
			List<GameItem> items = [];
			foreach ((var key, var count) in counts)
			{
				var template = spriteTemplates.GetValueOrDefault(key);
				if (template is null)
					continue;
				var attributes = entitlementMetadata.TryGetValue(key, out var stringAttributes) ? JsonNode.Parse(stringAttributes).AsObject() : null;
				if (catalog?.TryGetValue(key, out var summonCost)==true)
				{
					attributes ??= [];
					attributes["summonCost"] = summonCost;
				}
				items.Add(template.CreateInstance(count, attributes));
			}
			return [..items];
		}
	}

	struct SpriteArchive
	{
		public string DisplayName { get; init; }
		public string Generation { get; init; }
		public HashSet<string> HasSprites { get; init; }
		public HashSet<string> HasMaxxed { get; init; }
		public HashSet<string> HasCollected { get; init; }
		public HashSet<string> HasMastered { get; init; }
	}
}
