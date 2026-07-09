using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public partial class MissionEntry : Control, IRecyclableEntry, IListEntry<GameMission>
{
	[Signal]
	public delegate void NameChangedEventHandler(string name);
	[Signal]
	public delegate void DescriptionChangedEventHandler(string description);
	[Signal]
	public delegate void LocationChangedEventHandler(string location);
	[Signal]
	public delegate void PowerLevelChangedEventHandler(string powerLevel);
	[Signal]
	public delegate void TooltipChangedEventHandler(string tooltip);
	[Signal]
	public delegate void IconChangedEventHandler(Texture2D icon);
	[Signal]
	public delegate void BackgroundChangedEventHandler(Texture2D background);
	[Signal]
	public delegate void BackgroundVisibleEventHandler(bool visible);
	[Signal]
	public delegate void VenturesIndicatorVisibleEventHandler(bool visible);
	[Signal]
	public delegate void TheaterCategoryChangedEventHandler(string theatreCat);
	[Signal]
	public delegate void TheaterColorChangedEventHandler(Color theatreCol);
	[Signal]
	public delegate void TheaterNameChangedEventHandler(string theatreName);
	[Signal]
	public delegate void MissionLockedEventHandler(bool locked);
	[Signal]
	public delegate void MissionCompleteEventHandler(bool complete);
	[Signal]
	public delegate void IsToDoEventHandler(bool todo);

	[Export]
	bool controlModifierParentLayoutProps = true;
	[Export]
	bool fullItems = false;
	[Export]
	bool ignoreAccountStatus = false;

	[Export]
	Control alertModifierLayout;
	[Export]
	Control alertModifierParent;

	[Export]
	Control missionRewardLayout;
	[Export]
	Control missionRewardParent;

	[Export]
	Control alertRewardLayout;
	[Export]
	Control alertRewardParent;

	[Export]
	Control highlightedRewardParent;

	[Export]
	Control toDoListContent;

	[Export]
	Texture2D defaultBackground;

	public GameMission currentMission { get; private set; } = null;
	IMissionHighlightProvider highlightedItemProvider;

	public Control node => this;

	public override void _Ready()
	{
		//if (toDoListContent is not null)
		//    toDoListContent.Visible = GameAccount.ActiveAccount.isOwned;
		GameAccount.ActiveAccountChanged += AccountChanged;
		MissionToDoListController.OnToDoListChanged += ToDoListChanged;
		AppConfig.OnConfigChanged += OnConfigChanged;
		EmitSignalBackgroundVisible(AppConfig.Get("missions", "show_background", true));
	}

	private void OnConfigChanged(string section, string key, JsonNode val)
	{
		if (section != "missions")
			return;
		if (key == "show_background")
			EmitSignalBackgroundVisible(val.GetValue<bool?>() ?? true);
		if (key == "hide_lock")
			EmitSignalMissionLocked(
				!AppConfig.Get("missions", "hide_lock", false) &&
				currentMission?.PlayableBy(GameAccount.ActiveAccount) != true &&
				!ignoreAccountStatus
			);
	}

	public override void _ExitTree()
	{
		GameAccount.ActiveAccountChanged -= AccountChanged;
		MissionToDoListController.OnToDoListChanged -= ToDoListChanged;
		AppConfig.OnConfigChanged -= OnConfigChanged;
	}

	private void ToDoListChanged()
	{
		EmitSignalIsToDo(IsFullyOnToDo());
	}

	private void AccountChanged()
	{
		EmitSignalMissionLocked(
			!AppConfig.Get("missions", "hide_lock", false) &&
			currentMission?.PlayableBy(GameAccount.ActiveAccount) != true &&
			!ignoreAccountStatus
		);

		EmitSignalMissionComplete(
			currentMission?.AlertIsCompleteFor(GameAccount.ActiveAccount) == true &&
			!ignoreAccountStatus
		);

		currentMission?.UpdateRewardNotifications();
		if (toDoListContent is not null)
			toDoListContent.Visible = !ignoreAccountStatus && GameAccount.ActiveAccount.isOwned;
	}

	IRecyclableElementProvider<GameMission> missionProvider;
	public void SetRecyclableElementProvider(IRecyclableElementProvider provider)
	{
		if (provider is IRecyclableElementProvider<GameMission> newProvider)
			missionProvider = newProvider;
	}

	public void SetRecycleIndex(int index)
	{
		if (missionProvider is null)
			return;
		SetMission(missionProvider.GetRecycleElement(index));
		currentMission?.UpdateRewardNotifications();
	}

	public void ClearMission()
	{
		currentMission = null;
		manualHighlights = null;
		EmitSignalNameChanged(null);
		EmitSignalDescriptionChanged(null);
		EmitSignalLocationChanged(null);
		EmitSignalIconChanged(null);
		EmitSignalPowerLevelChanged(null);
		EmitSignalBackgroundChanged(null);
		EmitSignalIsToDo(false);

		EmitSignalMissionLocked(false);
		EmitSignalMissionComplete(false);

		EmitSignalTheaterNameChanged(null);
		EmitSignalVenturesIndicatorVisible(false);
		EmitSignalTheaterCategoryChanged(null);
		EmitSignalTheaterColorChanged(Colors.White);

		EmitSignalTooltipChanged(null);

		UpdateHighlightedItems();
	}

	public void SetMission(GameMission mission)
	{
		if(mission is null)
		{
			ClearMission();
			return;
		}
		currentMission = mission;

		EmitSignalNameChanged(currentMission.DisplayName);
		EmitSignalDescriptionChanged(currentMission.Description);
		EmitSignalLocationChanged(currentMission.Location);
		EmitSignalIconChanged(currentMission.missionGenerator.GetTexture(FnItemTextureType.Icon));
		EmitSignalPowerLevelChanged(currentMission.PowerLevel.ToString());
		EmitSignalBackgroundChanged(currentMission.backgroundTexture ?? defaultBackground);
		EmitSignalIsToDo(IsFullyOnToDo());

		EmitSignalMissionLocked(
			!AppConfig.Get("missions", "hide_lock", false) &&
			currentMission?.PlayableBy(GameAccount.ActiveAccount) != true &&
			!ignoreAccountStatus
		);

		EmitSignalMissionComplete(
			currentMission?.AlertIsCompleteFor(GameAccount.ActiveAccount) == true &&
			!ignoreAccountStatus
		);

		EmitSignalTheaterNameChanged(currentMission.TheaterName);
		EmitSignalVenturesIndicatorVisible(currentMission.TheaterCat == "v");
		EmitSignalTheaterCategoryChanged(currentMission.TheaterCat.ToUpper());
		EmitSignalTheaterColorChanged(currentMission.TheaterColor);

		string tileEventFlag = currentMission.tile.requirements.eventFlag;
		bool hasEventFlag = !string.IsNullOrWhiteSpace(tileEventFlag);

		//TODO: if a mission has a quest requirement, mission entries should have the option of listing it
		List<string> tooltipDescriptions =
		[
			currentMission.Description ?? "",
			//"Item Id: " + item.templateId,
		];
		if (mission.searchTags.Count > 0)
			tooltipDescriptions.Add("Search Tags: " + mission.searchTags.Select(n => n.ToString()).Where(t => !t.StartsWith("hidetag_")).ToArray().Join(", "));

		EmitSignalTooltipChanged(
			CustomTooltip.GenerateSimpleTooltip(
				currentMission.DisplayName,
				null,
				[.. tooltipDescriptions]
			)
		);

		if (alertModifierLayout is not null && alertModifierParent is not null)
		{
			if (currentMission.alertModifiers.Length > 0)
			{
				for (int i = 0; i < alertModifierParent.GetChildCount(); i++)
				{
					var alertChild = alertModifierParent.GetChild<Control>(i);
					if (currentMission.alertModifiers.Length <= i)
					{
						alertChild.Visible = false;
						alertChild.ProcessMode = ProcessModeEnum.Disabled;
						continue;
					}

					alertChild.Visible = true;
					alertChild.ProcessMode = ProcessModeEnum.Inherit;

					if (alertChild is TextureRect textureChild)
					{
						var modifierTemplate = currentMission.alertModifiers[i].template;
						textureChild.Texture = modifierTemplate.GetTexture();
						textureChild.TooltipText = modifierTemplate.DisplayName;
					}
					else if (alertChild is GameItemEntry gameItemChild)
					{
						gameItemChild.SetItem(currentMission.alertModifiers[i]);
					}
				}

				if (controlModifierParentLayoutProps)
				{
					alertModifierParent.SizeFlagsHorizontal = currentMission.alertModifiers.Length == 5 ? SizeFlags.ShrinkCenter : SizeFlags.ExpandFill;
					if (currentMission.alertModifiers.Length == 6)
					{
						var seventhChild = alertModifierParent.GetChild(6) as Control;
						seventhChild.Visible = true;
						seventhChild.SelfModulate = Colors.Transparent;
					}
				}

				alertModifierLayout.Visible = true;
				alertModifierLayout.ProcessMode = ProcessModeEnum.Inherit;
			}
			else
			{
				alertModifierLayout.Visible = false;
				alertModifierLayout.ProcessMode = ProcessModeEnum.Disabled;
			}
		}

		if (missionRewardLayout is not null && missionRewardParent is not null)
		{
			var rewards = fullItems ?
				currentMission.rewardItems :
				[.. currentMission.rewardItems.Where(r =>
					r.template.DisplayName != "Gold" &&
					r.template.DisplayName != "Venture XP"
				)];
			if (rewards.Length > 0)
			{
				ApplyItems(rewards, missionRewardParent);

				missionRewardLayout.Visible = true;
				missionRewardLayout.ProcessMode = ProcessModeEnum.Inherit;
			}
			else
			{
				missionRewardLayout.Visible = false;
				missionRewardLayout.ProcessMode = ProcessModeEnum.Disabled;
			}
		}

		if (alertRewardLayout is not null && alertRewardParent is not null)
		{
			currentMission.UpdateRewardNotifications();
			var rewards = fullItems ?
				currentMission.alertRewardItems :
				[.. currentMission.alertRewardItems.Where(r =>
					r.template.DisplayName != "Venture XP"
				)];
			if (rewards.Length > 0)
			{
				ApplyItems(rewards, alertRewardParent);

				alertRewardLayout.Visible = true;
				alertRewardLayout.ProcessMode = ProcessModeEnum.Inherit;
			}
			else
			{
				alertRewardLayout.Visible = false;
				alertRewardLayout.ProcessMode = ProcessModeEnum.Disabled;
			}
		}

		UpdateHighlightedItems();
	}

	public void SetHighlightProvider(IMissionHighlightProvider provider)
	{
		if (highlightedItemProvider is not null)
		{
			highlightedItemProvider.OnHighlightedItemFilterChanged -= UpdateHighlightedItems;
		}
		highlightedItemProvider = provider;
		if (highlightedItemProvider is not null)
		{
			highlightedItemProvider.OnHighlightedItemFilterChanged += UpdateHighlightedItems;
		}
		UpdateHighlightedItems();
	}

	GameItem[] manualHighlights;
	IEnumerable<GameItem> HighlightItems
	{
		get
		{
			if (manualHighlights is not null)
				return manualHighlights;
			if (highlightedItemProvider?.HighlightedItemFilter is not Func<GameItem, bool> predicate)
				return [];
			var rewards = fullItems ?
				currentMission?.allItems :
				[.. (currentMission?.allItems ?? [])
					.Where(r =>
						r.template.DisplayName != "Gold" &&
						r.template.DisplayName != "Venture XP"
					)
					.OrderBy(r => -r.sortingTemplate.RarityLevel)
					.ThenBy(r => -r.quantity)
				];
			return rewards.Where(predicate);
		}
	}

	void UpdateHighlightedItems()
	{
		if (highlightedRewardParent is null || currentMission is null)
			return;
		ApplyItems([.. HighlightItems], highlightedRewardParent);
	}

	static void ApplyItems(GameItem[] itemArray, Control parent)
	{
		for (int i = 0; i < parent.GetChildCount(); i++)
		{
			var controlChild = parent.GetChild<GameItemEntry>(i);
			if (itemArray.Length <= i)
			{
				controlChild.ClearItem();
				controlChild.Visible = false;
				controlChild.ProcessMode = ProcessModeEnum.Disabled;
				continue;
			}
			controlChild.Visible = true;
			controlChild.ProcessMode = ProcessModeEnum.Inherit;

			bool isRewardBundle = itemArray[i].template.Name.StartsWith("zcp_", StringComparison.OrdinalIgnoreCase);
			controlChild.addXToAmount = isRewardBundle;
			controlChild.compactifyAmount = !isRewardBundle;
			controlChild.preventInteractability = isRewardBundle;
			controlChild.SetItem(itemArray[i]);
		}
	}

	public void InspectMission()
	{
		MissionViewer.ShowMission(currentMission);
	}

	public void AddToList()
	{
		foreach (var item in HighlightItems)
		{
			MissionToDoListController.AddToList(currentMission, item);
		}
	}

	bool IsFullyOnToDo()
	{
		foreach (var item in HighlightItems)
		{
			if (!MissionToDoListController.IsOnToDoList(item))
				return false;
		}
		return true;
	}

	public void RemoveFromList()
	{
		foreach (var item in HighlightItems)
		{
			MissionToDoListController.RemoveFromList(item);
		}
	}

	int IListEntry<GameMission>.CurrentIndexTarget { get; set; }
	IListProvider<GameMission> IListEntry<GameMission>.CurrentListProvider { get; set; }
	void IListEntry<GameMission>.SetListEntryValue(GameMission newValue) => SetMission(newValue);

}

public interface IMissionHighlightProvider
{
	public event Action OnHighlightedItemFilterChanged;
	public Func<GameItem, bool> HighlightedItemFilter { get; }
}
