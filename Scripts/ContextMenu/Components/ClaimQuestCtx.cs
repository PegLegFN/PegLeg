using Godot;
using System.Linq;

public partial class ClaimQuestCtx : BaseContextComponent
{
    public override string Id => "ClaimQuest";
    GameItem currentItem;
    [Export]
    Control[] mainBtnControls;
    [Export]
    Control[] choiceControls;

    public override void _Ready()
    {
        base._Ready();
    }

    public override void Update(ContextMenuHook hook)
    {
        currentItem = hook?.itemSource?.currentItem;
        bool disabled =
            currentItem?.templateId.StartsWith("Quest") != true ||
            currentItem?.profile?.account.isOwned != true ||
            currentItem.QuestState != "Completed";
        SetDisabled(disabled);

        var choiceReward = currentItem?.template?.GetVisibleQuestRewards()?.FirstOrDefault(i => i.attributes?["quest_selectable"] is not null);
        if (choiceReward is null)
            SetChoiceCount(0);
        else
            SetChoiceCount(choiceReward.attributes["options"].AsArray().Count);

        if (disabled)
            currentItem = null;
    }

    void SetChoiceCount(int count)
    {
        for (int i = 0; i < mainBtnControls.Length; i++)
        {
            mainBtnControls[i].Visible = count==0;
        }
        for (int i = 0; i < choiceControls.Length; i++)
        {
            choiceControls[i].Visible = i + 1 <= count;
        }
    }

    public async void Claim(int index)
    {
        if (currentItem is null)
            return;
        bool newState = !currentItem.QuestPinned;
        var items = await currentItem.ClaimQuest(index);
        NotificationManager.Push(items.Select(i => new NotificationData()
        {
            icon = i.GetTexture(),
            itemColor = i.template.RarityColor,
            header = $"Claimed: {i.template.DisplayName} x{i.quantity}",
            body = i.template.Description
        }));
    }
}
