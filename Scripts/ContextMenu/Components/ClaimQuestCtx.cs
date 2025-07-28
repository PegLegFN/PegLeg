using System.Linq;

public partial class ClaimQuestCtx : BaseContextComponent
{
    public override string Id => "ClaimQuest";
    GameItem currentItem;

    public override void Update(ContextMenuHook hook)
    {
        currentItem = hook?.itemSource?.currentItem;
        bool disabled = currentItem?.templateId.StartsWith("Quest") != true || currentItem?.profile?.account.isOwned != true;
        SetDisabled(disabled);
        if (disabled)
            currentItem = null;
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
