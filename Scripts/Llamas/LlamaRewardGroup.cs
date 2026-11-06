using Godot;
using System;
using System.Linq;

public partial class LlamaRewardGroup : Control
{
	[Export]
	Label displayName;
	[Export]
	GameItemEntry[] entries;

	public void SetItems(GameItem[] items)
	{
		if(items is null)
		{
			foreach (var e in entries)
			{
				e.ClearItem();
				e.Visible = false;
			}
			Visible = false;
			return;
		}
		Visible = true;
		displayName.Text = items.FirstOrDefault().template.ItemName;
		for (int i = 0; i < MathF.Min(items.Length, entries.Length); i++)
		{
			items[i].SetRewardNotification();
			entries[i].SetItem(items[i]);
			entries[i].Visible = true;
		}
		for (int i = items.Length; i < entries.Length; i++)
		{
			entries[i].ClearItem();
			entries[i].Visible = false;
		}
	}
}
