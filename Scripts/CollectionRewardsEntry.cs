using Godot;
using System;
using System.Linq;

public partial class CollectionRewardsEntry : Control
{
    [Export]
    GameItemEntry itemEntry;
    [Export]
    public PackedScene numberScene;
    [Export]
    public Control numberParent;

    public void SetItem(GameItem item, int[] levels)
    {
        itemEntry.SetItem(item);
        for (int i = 0; i < levels.Length; i++)
        {
            var num = numberScene.Instantiate();
            numberParent.AddChild(num);
            num.GetNode<Label>("%Label").Text = levels[i].ToString();
        }
    }
}
