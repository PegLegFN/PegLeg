using Godot;
using System.Linq;

public partial class GameItemCost : Control
{
    [Export]
    Label currentLabel;
    [Export]
    GameItemEntry itemEntry;

    public void SetItem(GameItem item, GameAccount forAccount = null, string forProfile = null)
    {
        itemEntry.SetItem(item);
        forAccount ??= GameAccount.ActiveAccount;
        forProfile ??= FnProfileTypes.AccountItems;
        currentLabel.Visible = forAccount is not null;
        if (!currentLabel.Visible)
            return;
        var profile = forAccount.GetProfile(forProfile);
        var totalItems = profile.SumTemplateItems(item.templateId);
        currentLabel.Text = $"{(itemEntry.compactifyAmount ? totalItems.Compactify() : totalItems.Notate())}/";
    }

    public void ClearItem()
    {
        itemEntry.ClearItem();
        currentLabel.Visible = false;
    }
}
