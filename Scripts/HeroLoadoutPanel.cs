using Godot;
using System;
using System.Collections.Generic;
using static System.Runtime.InteropServices.JavaScript.JSType;

public partial class HeroLoadoutPanel : Control
{
	[Export]
	GameItemEntry commander;
    [Export]
    GameItemEntry teamPerk;
    [Export]
    GameItemEntry[] support;
    [Export]
    GameItemEntry[] gadgets;
    [Export]
    bool useActiveAccount = true;
    [Export]
    bool interactable = true;

    public override void _Ready()
	{
        if (useActiveAccount)
        {
            GameAccount.ActiveAccountChanged += UpdateActive;
            SetAccount(GameAccount.activeAccount);
        }

	}

    public override void _ExitTree()
    {
        GameAccount.ActiveAccountChanged -= UpdateActive;
    }

    void UpdateActive()
    {
        SetAccount(GameAccount.activeAccount);
    }

    GameItem loadoutItem;

    public void SetAccount(GameAccount account)
    {
        var profile = account?.GetProfile(FnProfileTypes.AccountItems);
        var loadoutUUID = profile?.statAttributes?["selected_hero_loadout"]?.ToString();
        SetLoadout(profile?.GetItem(loadoutUUID));
    }

    public void SetLoadout(GameItem newLoadoutItem)
    {
        if (newLoadoutItem == loadoutItem)
            return;
        if (loadoutItem is not null)
        {
            loadoutItem.OnChanged -= UpdateLoadout;
        }
        loadoutItem = newLoadoutItem;
        if (loadoutItem is not null)
        {
            loadoutItem.OnChanged += UpdateLoadout;
            UpdateLoadout();
        }
    }


    void UpdateLoadout()
    {
        commander.SetItem(loadoutItem.profile.GetItem(loadoutItem.attributes["crew_members"]["commanderslot"].ToString()));
        TrySetItem(teamPerk, loadoutItem.attributes["team_perk"]?.ToString());
        for (int i = 0; i < support.Length; i++)
        {
            GD.Print($"followerslot{i + 1}");
            TrySetItem(support[i], loadoutItem.attributes["crew_members"][$"followerslot{i+1}"]?.ToString());
        }
        GD.Print(loadoutItem.attributes["gadgets"]?.ToString() ?? "No Gadget");
        TrySetTemplate(gadgets[0], loadoutItem.attributes["gadgets"]?[0]?["gadget"]?.ToString());
        TrySetTemplate(gadgets[1], loadoutItem.attributes["gadgets"]?[1]?["gadget"]?.ToString());
    }

    void TrySetItem(GameItemEntry itemEntry, string guid)
    {
        itemEntry.Visible = !string.IsNullOrWhiteSpace(guid);
        if (itemEntry.Visible)
        {
            itemEntry.SetItem(loadoutItem.profile.GetItem(guid));
            itemEntry.SetInteractable(interactable);
        }
    }

    static Dictionary<string, GameItem> gadgetLookup = [];

    void TrySetTemplate(GameItemEntry itemEntry, string templateId)
    {
        itemEntry.Visible = !string.IsNullOrWhiteSpace(templateId);
        if (itemEntry.Visible)
        {
            var gadgetItem = gadgetLookup.TryGetValue(templateId, out var existing) ? 
                existing : 
                (gadgetLookup[templateId] = GameItemTemplate.Get(templateId).CreateInstance());
            itemEntry.SetItem(gadgetItem);
            itemEntry.SetInteractable(interactable);
        }
    }
}
