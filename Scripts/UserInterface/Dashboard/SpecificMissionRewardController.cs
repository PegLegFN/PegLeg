using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public partial class SpecificMissionRewardController : Control
{
	[Export]
	string targetTemplateId;
	[Export]
	string targetTemplatePrefix;
	public string TargetTemplatePrefix => IsBlockedByConfig ? null : targetTemplatePrefix;
	bool IsBlockedByConfig => configDependancy is not null && !AppConfig.Get(configSection, configKey, configDefault);
	[Export]
	string titleText;
	[Export]
	bool hideWhileEmpty;
	[Export]
	string configDependancy;
	[Export]
	bool configDefault = true;
	[ExportGroup("Nodes")]
	[Export]
	Button filterPlayable;
	[Export]
	Label titleLabel;
	[Export]
	Label totalLabel;
	[Export]
	Label fallbackLabel;
	[Export]
	GameItemEntry titleItem;
	[Export]
	PackedScene rewardEntryScene;
	[Export]
	Control rewardParent;
	[Export]
	Control loadingIndicator;
	List<MissionRewardEntry> rewards = [];

	string configSection;
	string configKey;

	public override void _Ready()
	{
		if(configDependancy is not null)
		{
			var split = configDependancy.Split(':');
			configSection = split[0];
			configKey = split[1];
		}
		if (filterPlayable is not null)
		{
			filterPlayable.Pressed += UpdateMissions;
			GameAccount.ActiveAccountChanged += UpdateMissions;
		}
		GameMission.OnMissionsInvalidated += ClearMissions;
		GameMission.OnMissionsUpdated += UpdateMissions;
		AppConfig.OnConfigChanged += OnConfigChanged;
		var template = GameItemTemplate.Get(targetTemplateId);
		titleItem.SetItem(template?.CreateInstance());
		titleLabel.Text = titleText;
		fallbackLabel.Visible = false;
		if (string.IsNullOrEmpty(titleText))
		{
			fallbackLabel.Visible = true;
			titleLabel.Visible = false;
		}
		if (GameMission.MissionList is not null)
			UpdateMissions();
	}

	private void OnConfigChanged(string section, string key, JsonNode value)
	{
		if (configDependancy is null)
			return;
		if (!(
			(section != configSection || key != configKey) ||
			(section != "missions" || key != "split_grouped_stacks")
			))
			return;
		if (GameMission.MissionList is not null)
			UpdateMissions();
	}

	public override void _ExitTree()
	{
		if (filterPlayable is not null)
			GameAccount.ActiveAccountChanged -= UpdateMissions;
		GameMission.OnMissionsInvalidated -= ClearMissions;
		GameMission.OnMissionsUpdated -= UpdateMissions;
		AppConfig.OnConfigChanged -= OnConfigChanged;
	}

	private void ClearMissions()
	{
		loadingIndicator.Visible = true;
		rewardParent.Visible = false;
		if(hideWhileEmpty)
			Visible = false;
		for (int i = 0; i < rewards.Count; i++)
		{
			rewards[i].Visible = false;
			rewards[i].ClearReward();
		}
	}

	bool MissionRewardValidator(GameItem item) => 
		item.templateId.StartsWith(targetTemplatePrefix, StringComparison.OrdinalIgnoreCase);

	private void UpdateMissions()
	{
		if (IsBlockedByConfig)
		{
			ClearMissions();
			Visible = false;
			return;
		}

		loadingIndicator.Visible = false;
		rewardParent.Visible = true;
		var matchingMissions = GameMission.MissionList.Where(m => 
			m.allItems.Any(MissionRewardValidator) &&
			(
				!(filterPlayable?.ButtonPressed ?? false) ||
				m.PlayableBy(GameAccount.ActiveAccount)
			)
		).ToArray();

		if (hideWhileEmpty)
		{
			Visible = matchingMissions.Length != 0;
		}
		else
		{
			Visible = true;
		}

		if(totalLabel is not null)
		{
			var matchingItems = matchingMissions.SelectMany(m => m.allItems.Where(MissionRewardValidator));
			totalLabel.Text = $"x{matchingItems.Sum(i => i.quantity)}";
		}

		bool splitStacks = AppConfig.Get("missions", "split_grouped_stacks", false);
		int rewardTotal = 0;

		for (int i = 0; i < matchingMissions.Length; i++)
		{
			var m = matchingMissions[i];
			GameItem[] missionRewards = [.. m.allItems.Where(MissionRewardValidator)];

			//kinda jank way to enable/disable stack splitting
			GameItem[][] rewardSets = splitStacks ? [.. missionRewards.Select<GameItem, GameItem[]>(m => [m])] : [missionRewards];

			foreach (var rewardSet in rewardSets)
			{
				if (rewardTotal >= rewards.Count)
				{
					var newEntry = rewardEntryScene.Instantiate<MissionRewardEntry>();
					rewards.Add(newEntry);
					rewardParent.AddChild(newEntry);
				}
				rewards[rewardTotal].SetRewardInfoManually(m, rewardSet);
				rewards[rewardTotal].Visible = true;
				rewardTotal++;
			}
		}
		for (int i = rewardTotal; i < rewards.Count; i++)
		{
			rewards[i].Visible = false;
			rewards[i].ClearReward();
		}
	}
}
