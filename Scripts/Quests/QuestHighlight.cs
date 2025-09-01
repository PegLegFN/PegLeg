using Godot;
using System;
using System.Linq;
using System.Text.Json.Nodes;

public partial class QuestHighlight : Control
{
    [Signal]
    public delegate void TooltipChangedEventHandler(string tooltip);
    [Signal]
    public delegate void IconChangedEventHandler(Texture2D icon);
    [Signal]
    public delegate void ColorChangedEventHandler(Color color);
    [Signal]
    public delegate void ProgressChangedEventHandler(float progress);


    [Export]
    bool useFirstRewardAsIcon = false;
    [Export]
    Color incompleteColor;
    [Export]
    Color completeColor;

    public void SetHighlightedQuest(QuestSlot questData)
    {
        Visible = questData.isUnlocked;
        ProcessMode = questData.isUnlocked ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;
        if (!Visible)
            return;
        EmitSignal(SignalName.TooltipChanged, questData.questTemplate["DisplayName"].ToString());
        if (questData.isClaimed)
        {
            EmitSignal(SignalName.ColorChanged, completeColor);
            EmitSignal(SignalName.ProgressChanged, 1);
        }
        else
        {
            EmitSignal(SignalName.ColorChanged, incompleteColor);

            float progressTotal = 0;
            var objectives = questData.questTemplate["Objectives"].AsArray();
            foreach (var objective in objectives)
            {
                int currentProgress = questData.questItem.attributes["completion_" + objective["BackendName"].ToString()]?.GetValue<int>() ?? 0;
                int maxProgress = objective["Count"].GetValue<int>();
                progressTotal += ((float)currentProgress / maxProgress) / objectives.Count;
            }
            EmitSignal(SignalName.ProgressChanged, progressTotal);
        }

        if (useFirstRewardAsIcon)
        {
            var rewards = questData.questTemplate.GetVisibleQuestRewards();
            var firstReward = rewards.First();
            if (firstReward.attributes["quest_selectable"].GetValue<bool>())
            {
                firstReward = firstReward.attributes["options"]
                    .AsArray()
                    .Select(n=>new GameItem(null, null, n.AsObject()))
                    .OrderBy(item=>-item.template.RarityLevel)
                    .FirstOrDefault();
            }
            EmitSignal(SignalName.IconChanged, firstReward.GetTexture());
        }
        else
        {
            EmitSignal(SignalName.IconChanged, questData.questTemplate.GetTexture());
        }
    }
}
