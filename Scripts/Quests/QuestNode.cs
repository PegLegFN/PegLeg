using Godot;
using System;
using System.Linq;

public partial class QuestNode : Control
{
    [Signal]
    public delegate void NotificationVisibleEventHandler(bool visible);
    [Signal]
    public delegate void PinnedVisibleEventHandler(bool visible);
    [Signal]
    public delegate void KeyItemVisibleEventHandler(bool visible);
    [Signal]
    public delegate void ColorChangedEventHandler(Color color);
    [Signal]
    public delegate void PressedEventHandler();

    [Export(PropertyHint.ArrayType)]
    Color[] colorStages = [];
    [Export]
    Control flagParent;
    [Export]
    Control[] flags = [];
    [Export]
    CheckButton selectedToggle;
    [Export]
    GameItemEntry questItemEntry;
    QuestSlot questData;
    bool displayAsLocked;

    //static readonly FrozenSet<string> keyItemTemplates = new string[]
    //{
    //    "AccountResource:reagent_alteration_gameplay_generic",
    //    "AccountResource:reagent_promotion_survivors",
    //    "AccountResource:reagent_promotion_heroes",
    //    "AccountResource:reagent_promotion_weapons",
    //    "AccountResource:reagent_promotion_traps",
    //}.ToFrozenSet(StringComparer.InvariantCultureIgnoreCase);

    public void SetupQuestNode(QuestSlot newQuestData, ButtonGroup buttonGroup, bool displayAsLocked)
    {
        this.displayAsLocked = displayAsLocked;
        selectedToggle.ButtonGroup = buttonGroup;
        if (questData is not null)
            questData.OnPropertiesUpdated -= RefreshQuestNode;
        questData = newQuestData;
        questData.OnPropertiesUpdated += RefreshQuestNode;

        foreach (var flag in flags)
        {
            flag.Visible = false;
        }

        RefreshQuestNode();
    }

    void RefreshQuestNode()
    {
        questItemEntry.SetItem(questData.isUnlocked ? questData.questItem : questData.questTemplate.CreateInstance());
        EmitSignal(SignalName.NotificationVisible, questData.isNew);

        flagParent.Visible = false;
        flags[0].Visible = false;
        bool showFlags = false;
        var rewards = questData.questTemplate.GetQuestRewards().Select(r=>r.templateId);
        if (questData.isPinned)
        {
            showFlags = true;
            flags[0].Visible = true;
        }
        if (rewards.Contains("AccountResource:reagent_alteration_gameplay_generic"))
        {
            showFlags = true;
            flags[1].Visible = true;
        }
        if (rewards.Any(r => r.StartsWith("AccountResource:reagent_promotion", StringComparison.InvariantCultureIgnoreCase)))
        {
            showFlags = true;
            flags[2].Visible = true;
        }
        if (rewards.Contains("AccountResource:voucher_herobuyback"))
        {
            showFlags = true;
            flags[3].Visible = true;
        }
        if (rewards.Contains("AccountResource:voucher_item_buyback"))
        {
            showFlags = true;
            flags[3].Visible = true;
        }
        if (rewards.Any(r => r.StartsWith("schematic:", StringComparison.InvariantCultureIgnoreCase)))
        {
            showFlags = true;
            flags[4].Visible = true;
        }
        if (rewards.Any(r=>r.StartsWith("hero:", StringComparison.InvariantCultureIgnoreCase)))
        {
            showFlags = true;
            flags[5].Visible = true;
        }

        if (showFlags)
            ShowFlagParent();

        //EmitSignal(SignalName.PinnedVisible, questData.isPinned);
        //EmitSignal(SignalName.KeyItemVisible, questData.questTemplate
        //    .GetQuestRewards()
        //    .Any(r =>
        //        r.templateId.StartsWith("hero:", StringComparison.InvariantCultureIgnoreCase) ||
        //        r.templateId.StartsWith("schematic:", StringComparison.InvariantCultureIgnoreCase) ||
        //        keyItemTemplates.Contains(r.templateId)
        //    )
        //);

        int colorIndex = 0;
        if (questData.isComplete)
            colorIndex = 2;
        else if(displayAsLocked)
            colorIndex = 0;
        else if (questData.isUnlocked)
            colorIndex = 1;
        EmitSignal(SignalName.ColorChanged, colorStages[colorIndex]);
    }

    async void ShowFlagParent()
    {
        await Helpers.WaitForFrame();
        flagParent.Visible = true;

    }

    public void Press()
    {
        selectedToggle.ButtonPressed = true;
        EmitSignal(SignalName.Pressed);
        EmitSignal(SignalName.NotificationVisible, false);
    }

    public override void _ExitTree()
    {
        if (questData is not null)
            questData.OnPropertiesUpdated -= RefreshQuestNode;
    }
}
