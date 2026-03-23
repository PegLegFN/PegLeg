using Godot;
using System;

public partial class TimelineShopStrip : Control
{
    [Export]
    GameItemEntry[] shopItems;
    [Export]
    Control resetIndicator;

    public TimelineInterface.ShopMarker current { get; private set; }
    public void SetMarker(TimelineInterface.ShopMarker marker)
    {
        if (current == marker)
            return;
        current = marker;

        resetIndicator.Visible = marker.isReset;
        var items = marker.ShopItems;
        for (int i = 0; i < shopItems.Length; i++)
        {
            if (items.Length > i)
            {
                shopItems[i].SetItem(items[i]);
                shopItems[i].Visible = true;
            }
            else
            {
                shopItems[i].Visible = false;
            }
        }
    }
}
