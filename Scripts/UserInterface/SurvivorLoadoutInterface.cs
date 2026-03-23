using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class SurvivorLoadoutInterface : Node
{
	const string LoadoutKey = "SurvivorLoadouts";
	[Export]
	Control unownedAccountLayout;
	[Export]
	Control loadoutOptionsLayout;
	[Export]
	OptionButton loadoutSelector;
	[Export]
	Button saveLoadoutButton;
	[Export]
	Button loadLoadoutButton;
	[Export]
	Button renameLoadoutButton;
	[Export]
	Button deleteLoadoutButton;
	[Export]
	Button clearSquadButton;
	[Export(PropertyHint.ArrayType)]
	SurvivorSquadEntry[] survivorSquads;

	GameAccount overrideAccount;

	public override void _Ready()
	{
		loadoutSelector.ItemSelected += OnLoadoutChanged;
		saveLoadoutButton.Pressed += OnLoadoutSave;
		loadLoadoutButton.Pressed += OnLoadoutLoad;
		renameLoadoutButton.Pressed += OnLoadoutRename;
		deleteLoadoutButton.Pressed += OnLoadoutDelete;
		clearSquadButton.Pressed += OnSquadClear;
		loadoutSelector.Selected = 0;
		saveLoadoutButton.Disabled = false;
		loadLoadoutButton.Disabled = true;
		renameLoadoutButton.Disabled = true;
		deleteLoadoutButton.Disabled = true;
		loadoutOptionsLayout.Visible = true;
		unownedAccountLayout.Visible = false;

		GameAccount.ActiveAccountChanged += OnActiveAccountChanged;
		UpdateAccount();
	}

	public override void _ExitTree()
	{
		GameAccount.ActiveAccountChanged -= OnActiveAccountChanged;
	}

	private void OnActiveAccountChanged()
	{
		if (overrideAccount is null)
			UpdateAccount();
	}

	public void SetOverrideAccount(GameAccount account = null)
	{
		overrideAccount = account;
		loadoutOptionsLayout.Visible = overrideAccount is null;
		unownedAccountLayout.Visible = overrideAccount is not null;
		UpdateAccount();
	}

	void UpdateAccount()
	{
		foreach (var squad in survivorSquads)
		{
			squad.SetOverrideAccount(overrideAccount);
		}
		if (overrideAccount is null)
		{
			GenerateOptions();
			loadoutSelector.Selected = 0;
		}
	}

	public override void _Process(double delta)
	{
		eggTimer -= (float)(delta / Engine.TimeScale);
	}

	string loadoutName = null;

	private void OnLoadoutChanged(long index)
	{
		saveLoadoutButton.Disabled = true;
		loadLoadoutButton.Disabled = true;
		renameLoadoutButton.Disabled = true;
		deleteLoadoutButton.Disabled = true;
		if (index == 0)
		{
			saveLoadoutButton.Disabled = false;
			loadoutName = null;
		}
		else if (index >= 2)
		{
			saveLoadoutButton.Disabled = false;
			loadLoadoutButton.Disabled = false;
			renameLoadoutButton.Disabled = false;
			deleteLoadoutButton.Disabled = false;
			loadoutName = loadoutSelector.GetItemText((int)index);
		}
	}

	private async void OnLoadoutSave()
	{
		if (overrideAccount is not null)
			return;
		if (loadoutName is not null)
		{
			if (await GenericConfirmationWindow.ShowConfirmation(
					"Overwrite Loadout?",
					"Overwrite",
					contextText: "The contents of the selected loadout will be overwritten"
			) != true)
				return;
		}

		var account = GameAccount.ActiveAccount;
		var loadouts = account.GetLocalData(LoadoutKey)?.AsObject() ?? [];

		loadoutName ??= await GenericLineEditWindow.ShowLineEdit("Enter Survivor Loadout Name", validator: val =>
		{
			if (loadouts.ContainsKey(val))
				return "A survivor loadout with that name already exists";
			return string.IsNullOrWhiteSpace(val) ? "" : null;
		});

		if (loadoutName is null)
			return;

		var allWorkers = (await account.GetProfile(FnProfileTypes.AccountItems).Query())
			.GetItems("Worker", item => !string.IsNullOrWhiteSpace(item.attributes["squad_id"]?.ToString()));

		var groupedWorkers = allWorkers.GroupBy(item => item.attributes["squad_id"].ToString());

		JsonObject squadsMappings = [];
		foreach (var workers in groupedWorkers)
		{
			var squadArray = new JsonArray();
			for (int i = 0; i < 8; i++)
			{
				squadArray.Add("");
			}
			foreach (var item in workers)
			{
				squadArray[item.attributes["squad_slot_idx"].GetValue<int>()] = item.uuid;
			}
			squadsMappings[workers.Key] = squadArray;
		}

		loadouts ??= [];
		loadouts[loadoutName] = squadsMappings;

		account.SetLocalData(LoadoutKey, loadouts);
		GenerateOptions();
		loadoutSelector.Selected = loadouts.Select(kvp => kvp.Key).ToList().IndexOf(loadoutName) + 2;
		OnLoadoutChanged(loadoutSelector.Selected);
	}

	float eggTimer = 0;
	private async void OnLoadoutLoad()
	{
		if (eggTimer > 0 || overrideAccount is not null)
			return;

		var account = GameAccount.ActiveAccount;

		var accountItems = account.GetProfile(FnProfileTypes.AccountItems);
		var allWorkers = accountItems.GetItems("Worker");
		//var slottedWorkers = allWorkers.Where( item => item.attributes.ContainsKey("squad_id"));
		var workerUUIDs = allWorkers.Select(item => item.uuid).ToList();
		int missingWorkers = 0;

		JsonObject fullLoadout = account.GetLocalData(LoadoutKey)[loadoutName].AsObject();
		JsonObject flattenedLoadout = new()
		{
			["characterIds"] = new JsonArray(),
			["squadIds"] = new JsonArray(),
			["slotIndices"] = new JsonArray(),
		};
		foreach (var squad in fullLoadout)
		{
			if (string.IsNullOrWhiteSpace(squad.Key))
				continue;
			var squadArray = squad.Value.AsArray();
			for (int i = 0; i < squadArray.Count; i++)
			{
				string workerKey = squadArray[i].ToString();

				if (string.IsNullOrWhiteSpace(workerKey))
					continue;

				if (!workerUUIDs.Contains(workerKey))
				{
					missingWorkers++;
					//todo: try to approximate a worker to fill in the blank?
					continue;
				}


				//if (
				//    existingWorkers.ContainsKey(workerKey) &&
				//    existingWorkers[workerKey]["attributes"]["squad_id"].ToString() == squad.Key &&
				//    existingWorkers[workerKey]["attributes"]["squad_slot_idx"].GetValue<int>() == i
				//    )
				//    continue;

				flattenedLoadout["characterIds"].AsArray().Add(workerKey);
				flattenedLoadout["squadIds"].AsArray().Add(squad.Key);
				flattenedLoadout["slotIndices"].AsArray().Add(i);
			}
		}

		if (!(await GenericConfirmationWindow.ShowConfirmation(
				"Apply Loadout?",
				"Apply",
				contextText: "Survivors in this loadout will be slotted into their squads",
				warningText: missingWorkers > 0 ? $"Warning: {missingWorkers} survivor{(missingWorkers > 1 ? "s" : "")} in the loadout could not be found" : null
			) ?? false))
			return;
		eggTimer = 3;

		using var _ = LoadingOverlay.CreateToken();
		await accountItems.PerformOperation("UnassignAllSquads", new JsonObject() { ["squadIds"] = new JsonArray([.. survivorSquadIds.Select(s => (JsonNode)s)]) });
		await accountItems.PerformOperation("AssignWorkerToSquadBatch", flattenedLoadout);
		await Helpers.WaitForTimer(0.1);
	}

	private async void OnLoadoutRename()
	{
		if (overrideAccount is not null)
			return;
		var account = GameAccount.ActiveAccount;
		var loadouts = account.GetLocalData(LoadoutKey).AsObject();
		string newLoadoutName = await GenericLineEditWindow.ShowLineEdit("Enter Survivor Loadout Name", validator: val =>
		{
			if (val == loadoutName)
				return "...That's already the name of the loadout";
			if (loadouts.ContainsKey(val))
				return "A survivor loadout with that name already exists";
			return string.IsNullOrWhiteSpace(val) ? "" : null;
		});

		if (newLoadoutName is null)
			return;

		var loadoutInQuestion = loadouts[loadoutName];
		loadouts.Remove(loadoutName);
		loadouts.Add(newLoadoutName, loadoutInQuestion);
		loadoutName = newLoadoutName;

		account.SetLocalData(LoadoutKey, loadouts);
		GenerateOptions();
		loadoutSelector.Selected = loadouts.Select(kvp => kvp.Key).ToList().IndexOf(loadoutName) + 2;
		OnLoadoutChanged(loadoutSelector.Selected);
	}

	private async void OnLoadoutDelete()
	{
		if (await GenericConfirmationWindow.ShowConfirmation(
				"Delete Loadout?",
				"Delete",
				contextText: "The selected loadout will be deleted",
				warningText: "This action cannot be undone"
		) != true)
			return;

		var account = GameAccount.ActiveAccount;
		var loadouts = account.GetLocalData(LoadoutKey).AsObject();

		loadouts.Remove(loadoutName);

		account.SetLocalData(LoadoutKey, loadouts);
		GenerateOptions();

		loadoutSelector.Selected = 0;
		OnLoadoutChanged(loadoutSelector.Selected);
	}

	static readonly string[] survivorSquadIds =
	[
		"squad_attribute_medicine_emtsquad",
		"squad_attribute_medicine_trainingteam",
		"squad_attribute_arms_fireteamalpha",
		"squad_attribute_arms_closeassaultsquad",
		"squad_attribute_scavenging_scoutingparty",
		"squad_attribute_scavenging_gadgeteers",
		"squad_attribute_synthesis_corpsofengineering",
		"squad_attribute_synthesis_thethinktank",
	];

	private async void OnSquadClear()
	{
		if (await GenericConfirmationWindow.ShowConfirmation(
				"Clear Squad?",
				"Clear",
				contextText: "All slotted survivors in all squads will be unslotted"
		) != true)
			return;
		using var _ = LoadingOverlay.CreateToken();
		var accountItems = GameAccount.ActiveAccount.GetProfile(FnProfileTypes.AccountItems);
		await accountItems.PerformOperation("UnassignAllSquads", new JsonObject() { ["squadIds"] = new JsonArray([.. survivorSquadIds.Select(s => (JsonNode)s)]) });
	}

	void GenerateOptions()
	{
		loadoutSelector.Clear();
		loadoutSelector.AddItem("[Create New Loadout]");
		loadoutSelector.AddSeparator("Loadouts");
		var loadouts = GameAccount.ActiveAccount.GetLocalData("SurvivorLoadouts")?.AsObject() ?? [];
		foreach (var kvp in loadouts)
		{
			loadoutSelector.AddItem(kvp.Key);
		}
	}

	[Export]
	Texture2D recycleIcon;
	[Export]
	Texture2D collectionIcon;

	static readonly Predicate<GameItem> recycleFilter = item =>
		item.template.Type is string type && (type == "Worker" || type == "Schematic" || type == "Hero" || type == "Defender") &&
		!(item.template["IsPermanent"]?.GetValue<bool>() ?? false) &&
		!item.templateId.Contains("ammo") && !item.templateId.Contains("floor_defender") &&
		!item.templateId.Contains("player_jump_pad") && !item.templateId.Contains("ingredient");

	async void DebugRecycle()
	{
		using var loadingToken = LoadingOverlay.CreateToken();
		var accountItems = await GameAccount.ActiveAccount.GetProfile(FnProfileTypes.AccountItems).Query();

		GameItem[] filteredItems = accountItems.GetItems(recycleFilter);

		foreach (var item in filteredItems)
		{
			await item.SetCollected();
		}

		loadingToken.Dispose();

		var recycleItems = await SimpleItemSelector.OpenMultiSelector(filteredItems, SimpleItemSelector.RecycleConfig);

		GD.Print("Items: \n" + recycleItems.Select(item => item.uuid).ToArray().Join("\n"));
	}
}
