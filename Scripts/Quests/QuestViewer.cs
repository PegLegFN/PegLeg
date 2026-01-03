using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public partial class QuestViewer : Control
{
    [Signal]
    public delegate void NameChangedEventHandler(string name);
    [Signal]
    public delegate void IconChangedEventHandler(Texture2D icon);
    [Signal]
    public delegate void DescriptionChangedEventHandler(string description);
    [Signal]
    public delegate void CompleteVisibleEventHandler(bool visible);
    [Signal]
    public delegate void QuestUpdatedEventHandler();

    [Export]
    Control objectiveParent;
    [Export]
    PackedScene objectiveScene;
    List<QuestObjective> objectiveEntries = [];

    [Export]
    CheckButton pinButton;
    [Export]
    Button rerollButton;

    [Export]
    Control rewardParent;
    [Export]
    PackedScene rewardScene;
    List<GameItemEntry> rewardEntries = [];

    public override void _Ready()
    {
        pinButton.Pressed += UpdatePinnedState;
        rerollButton.Pressed += RerollQuest;
    }

    static readonly string[] cardPackFromRarity =
    [
        "CardPack:cardpack_choice_all_r",
        "CardPack:cardpack_choice_all_r",
        "CardPack:cardpack_choice_all_r",
        "CardPack:cardpack_choice_all_r",
        "CardPack:cardpack_choice_all_vr",
        "CardPack:cardpack_choice_all_sr",
    ];

    QuestSlot currentQuest;
    async void UpdatePinnedState()
    {
        var account = GameAccount.ActiveAccount;
        if (
            currentQuest is null || 
            !currentQuest.isUnlocked || 
            currentQuest.isClaimed || 
            currentQuest.questItem?.profile?.account != account
            )
            return;

        await currentQuest.questItem.SetPinnedAsync(pinButton.ButtonPressed);
        pinButton.ButtonPressed = currentQuest.isPinned;
    }

    async void RerollQuest()
    {
        var account = GameAccount.ActiveAccount;
        if (currentQuest is null || 
            !currentQuest.isUnlocked ||
            currentQuest.questItem?.profile?.account != account
            )
            return;

        using var _ = LoadingOverlay.CreateToken();

        var newQuest = await account.RerollQuest(currentQuest.questItem);
        if (newQuest == null)
            return;

        currentQuest.LinkQuestItem(newQuest);
        SetupQuest(currentQuest);
        rerollButton.Visible = account.CanRerollQuest();
    }

    public void SetupQuest(QuestSlot quest)
    {
        currentQuest = quest;
        EmitSignal(SignalName.NameChanged, quest.questTemplate.DisplayName);
        EmitSignal(SignalName.DescriptionChanged, quest.questTemplate.Description);
        EmitSignal(SignalName.IconChanged, quest.questTemplate.GetTexture());
        EmitSignal(SignalName.CompleteVisible, quest.isClaimed);

        rerollButton.Visible = quest.isRerollable;
        pinButton.Visible = quest.isUnlocked && !quest.isClaimed;
        pinButton.ButtonPressed = quest.isPinned;

        var rewards = quest.questTemplate.GetVisibleQuestRewards();

        rewardParent.Visible = false;
        for (int i = 0; i < rewards.Length; i++)
        {
            if (i >= rewardEntries.Count)
            {
                var newEntry = rewardScene.Instantiate<GameItemEntry>();
                rewardParent.AddChild(newEntry);
                rewardEntries.Add(newEntry);
            }
            rewardEntries[i].SetItem(rewards[i]);
            rewards[i].SetRewardNotification();
            rewardEntries[i].Visible = true;
        }

        for (int i = rewards.Length; i < rewardEntries.Count; i++)
        {
            rewardEntries[i].Visible = false;
        }
        rewardParent.Visible = true;
        
        var objectives = quest.questTemplate["Objectives"].AsArray();
        for (int i = 0; i < objectives.Count; i++)
        {
            if (i >= objectiveEntries.Count)
            {
                var newEntry = objectiveScene.Instantiate<QuestObjective>();
                objectiveParent.AddChild(newEntry);
                objectiveEntries.Add(newEntry);
            }
            var objective = objectives[i].AsObject();
            if (
                objective["Hidden"]?.GetValue<bool>() ?? false || 
                (
                    string.IsNullOrWhiteSpace(objective["Description"]?.ToString()) &&
                    string.IsNullOrWhiteSpace(objective["HudShortDescription"]?.ToString())
                )
            )
            {
                objectiveEntries[i].Visible = false;
                continue;
            }
            int currentProgress = quest.isUnlocked ? (quest.questItem.attributes["completion_" + objective["BackendName"].ToString().ToLower()]?.GetValue<int>() ?? 0) : 0;
            objectiveEntries[i].SetupObjective(objective, currentProgress);
            objectiveEntries[i].Visible = true;
        }
        for (int i = objectives.Count; i < objectiveEntries.Count; i++)
        {
            objectiveEntries[i].Visible = false;
        }
    }
}
