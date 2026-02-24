using Godot;

public partial class PinQuestCtx : AbstractContextComponent
{
    public override string Id => "PinQuest";
    GameItem currentItem;
    [Export]
    ToggleIconAnim toggleIcon;

    public override void Update(ContextMenuHook hook)
    {
        currentItem = hook?.itemSource?.currentItem;
        bool disabled = currentItem?.templateId.StartsWith("Quest") != true || currentItem?.profile?.account.isOwned != true;
        SetDisabled(disabled);
        if (disabled)
            currentItem = null;
        toggleIcon.SetState(currentItem?.QuestPinned ?? false);
    }

    public void TogglePinned()
    {
        if (currentItem is null)
            return;
        bool newState = !currentItem.QuestPinned;
        currentItem.SetPinned(newState);
        toggleIcon.Animate(newState);
    }
}
