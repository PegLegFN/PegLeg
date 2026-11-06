using Godot;
using System;
using System.Collections.Generic;

public partial class LlamaRewardViewer : Control
{
	[Export]
	Control llamaRewardGroupParent;
	[Export]
	PackedScene llamaRewardGroupScene;
	List<LlamaRewardGroup> llamaRewardGroups = [];

	public void SetRewardsForLlama(GameItem[][] contentGroups)
	{
		for (int i = 0; i < contentGroups.Length; i++)
		{
			if (i >= llamaRewardGroups.Count)
			{
				var newGroup = llamaRewardGroupScene.Instantiate<LlamaRewardGroup>();
				llamaRewardGroupParent.AddChild(newGroup);
				llamaRewardGroups.Add(newGroup);
			}
			llamaRewardGroups[i].SetItems(contentGroups[i]);
		}
		for (int i = contentGroups.Length; i < llamaRewardGroups.Count; i++)
		{
			llamaRewardGroups[i].SetItems(null);
		}
	}
}
