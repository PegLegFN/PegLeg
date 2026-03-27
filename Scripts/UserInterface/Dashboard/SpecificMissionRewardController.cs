using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class SpecificMissionRewardController : Control
{
	[Export]
	string targetTemplateId;
	[Export]
	string targetTemplatePrefix;
	public string TargetTemplatePrefix => targetTemplatePrefix;
	[Export]
	string titleText;
	[Export]
	bool hideWhileEmpty;
	[ExportGroup("Nodes")]
	[Export]
	Label titleLabel;
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

	public override void _Ready()
	{
		GameMission.OnMissionsInvalidated += ClearMissions;
		GameMission.OnMissionsUpdated += UpdateMissions;
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
	public override void _ExitTree()
	{
		GameMission.OnMissionsInvalidated -= ClearMissions;
		GameMission.OnMissionsUpdated -= UpdateMissions;
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
		loadingIndicator.Visible = false;
		rewardParent.Visible = true;
		var matchingMissions = GameMission.MissionList.Where(m => 
			m.allItems.Any(MissionRewardValidator)
		).ToArray();

		if (hideWhileEmpty)
		{
			Visible = matchingMissions.Length != 0;
		}
		else
		{
			Visible = true;
		}

		for (int i = 0; i < matchingMissions.Length; i++)
		{
			if (i >= rewards.Count)
			{
				var newEntry = rewardEntryScene.Instantiate<MissionRewardEntry>();
				rewards.Add(newEntry);
				rewardParent.AddChild(newEntry);
			}
			var m = matchingMissions[i];
			rewards[i].SetRewardInfoManually(m, [.. m.allItems.Where(MissionRewardValidator)]);
			rewards[i].Visible = true;
		}
		for (int i = matchingMissions.Length; i < rewards.Count; i++)
		{
			rewards[i].Visible = false;
			rewards[i].ClearReward();
		}
	}
}
