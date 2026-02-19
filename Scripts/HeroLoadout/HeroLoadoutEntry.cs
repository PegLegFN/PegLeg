using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class HeroLoadoutEntry : GameItemEntry
{
    [Signal]
    public delegate void LoadoutNameEventHandler(string name);
    [Signal]
    public delegate void IsCurrentEventHandler(bool value);
    [Export]
	GameItemEntry commander;
    [Export]
    TeamPerkEntry teamPerk;
    [Export]
    HeroEntry[] support;
    [Export]
    GameItemEntry[] gadgets;
    [Export]
    Control selectionFX;
    [Export]
    bool useActiveAccount = true;
    [Export]
    bool interactable = true;
    [Export]
    bool editable = false;
    [Export]
    bool addCurrentToName = true;

    public override void _Ready()
    {
        ClearItem();
        if (useActiveAccount)
        {
            GameAccount.ActiveAccountChanged += UpdateActive;
            SetAccount(GameAccount.ActiveAccount);
        }
        commander.Pressed += InteractCommander;
        teamPerk.Pressed += InteractTeamPerk;
        for (int i = 0; i < support.Length; i++)
        {
            int idx = i;
            support[i].Pressed += () => InteractSupport(idx);
        }
        for (int i = 0; i < gadgets.Length; i++)
        {
            int idx = i;
            gadgets[i].Pressed += () => InteractGadget(idx);
        }
    }

    public override void _ExitTree()
    {
        GameAccount.ActiveAccountChanged -= UpdateActive;
    }

    void UpdateActive()
    {
        SetAccount(GameAccount.ActiveAccount);
    }

    public void SetAccount(GameAccount account)
    {
        ClearItem();
        var profile = account?.GetProfile(FnProfileTypes.AccountItems);
        var loadoutItem = profile?.GetItem(profile?.statAttributes?["selected_hero_loadout"]?.ToString());
        if(profile is not null && loadoutItem is null)
        {
            //very new account, hasnt touched Hero Loadout menu once
            loadoutItem = profile.GetFirstTemplateItem("CampaignHeroLoadout:defaultloadout");
        }
        SetItem(loadoutItem);
    }

    public void ClearLoadout(bool animated = false)
    {
        bufferWhenCleared = animated;
        ClearItem(null);
    }

    public override void ClearItem(Texture2D clearIcon)
    {
        base.ClearItem(clearIcon);

        EmitSignalLoadoutName("");
        EmitSignalIsCurrent(false);

        float offset = 0;

        commander.SetItem(null);
        commander.SetInteractable(false);
        SetBGAnim(commander, offset);
        SetBGAnim(commander, offset, "PerkBG");

        teamPerk.SetItem(null);
        teamPerk.SetInteractable(false);
        SetBGAnim(teamPerk, offset += 0.05f);

        for (int i = 0; i < support.Length; i++)
        {
            support[i].SetItem(null);
            support[i].SetInteractable(false);
            support[i].SetTeamPerkContributor(false);
            support[i].SetWarningForCommander(null);
            SetBGAnim(support[i], offset += 0.05f);
        }

        offset += 0.05f;

        gadgets[0].SetItem(null);
        SetBGAnim(gadgets[0], offset);

        gadgets[1].SetItem(null);
        SetBGAnim(gadgets[1], offset);

        bufferWhenCleared = false;
    }

    bool bufferWhenCleared = false;

    void SetBGAnim(Control node, float offset, string nodeName = "LoadoutBG")
    {
        if (node.GetNodeOrNull("%" + nodeName) is not ShaderHook bg)
            return;
        bg.SetShaderBool(bufferWhenCleared, "enabled");
        bg.SetShaderFloat(offset, "offset");
    }

    protected override void UpdateItem(GameItem loadoutItem)
    {
        if (loadoutItem?.profile is null)
        {
            ClearLoadout();
            return;
        }
        if (loadoutItem.customData?["displayName"]?.ToString() is string customName)
        {
            EmitSignalLoadoutName(customName);
            EmitSignalIsCurrent(false);
        }
        else
        {
            bool isCurrent = loadoutItem.profile?.statAttributes?["selected_hero_loadout"]?.ToString() == loadoutItem.uuid && !string.IsNullOrWhiteSpace(loadoutItem.uuid);
            var idx = loadoutItem.attributes["loadout_index"]?.GetValue<int>();
            EmitSignalLoadoutName($"Loadout Slot {idx + 1}{((isCurrent && addCurrentToName) ? " (Current)" : null)}");
            EmitSignalIsCurrent(isCurrent);
        }

        var commanderUUID = loadoutItem.attributes?["crew_members"]?["commanderslot"]?.ToString();
        if (commanderUUID is null)
        {
            GD.Print("Missing Commander in " + loadoutItem.uuid);
            GD.PushWarning("Missing Commander in " + loadoutItem.uuid);
            ClearLoadout();
            return;
        }

        var commanderItem = loadoutItem.profile.GetItem(loadoutItem.attributes["crew_members"]["commanderslot"].ToString());
        commander.SetItem(commanderItem);
        commander.SetInteractable(interactable);

        var tpGuid = loadoutItem.attributes["team_perk"]?.ToString();
        var tpItem = tpGuid is not null ? loadoutItem.profile.GetItem(tpGuid) : null;
        var tpTemplate = tpItem?.template;
        teamPerk.SetItem(tpItem);
        teamPerk.SetInteractable(interactable);

        int matchCount = 0;
        int limit = tpTemplate?["ProgressiveBonus"]?.GetValue<bool>() == true ? 6 : tpTemplate?.TeamPerkMinRequirements ?? 0;

        for (int i = 0; i < support.Length; i++)
        {
            var supportGuid = loadoutItem.attributes["crew_members"][$"followerslot{i + 1}"]?.ToString();
            var supportHero = supportGuid is not null ? loadoutItem.profile.GetItem(supportGuid) : null;
            support[i].SetItem(supportHero);
            support[i].SetInteractable(interactable);
            support[i].SetTeamPerkContributor(false);
            support[i].SetWarningForCommander(null);

            var supportTemplate = supportHero?.template;
            if (supportTemplate is null)
                continue;

            support[i].SetWarningForCommander(commanderItem.template);

            if (tpTemplate is null || matchCount >= limit || !tpTemplate.TeamPerkBoostedByHero(supportTemplate))
                continue;
            support[i].SetTeamPerkContributor(true);
            matchCount++;
        }

        //GD.Print($"matched: {matchCount}/{limit}");
        teamPerk.SetTeamProgress(matchCount);
        teamPerk.SetWarningForCommander(commanderItem.template);

        if ((loadoutItem.attributes["gadgets"]?.AsArray().Count ?? 0) > 0)
            TrySetGadget(gadgets[0], loadoutItem.attributes["gadgets"]?[0]?["gadget"]?.ToString());
        else
            TrySetGadget(gadgets[0], null);

        if ((loadoutItem.attributes["gadgets"]?.AsArray().Count ?? 0) > 1)
            TrySetGadget(gadgets[1], loadoutItem.attributes["gadgets"]?[1]?["gadget"]?.ToString());
        else
            TrySetGadget(gadgets[1], null);
    }

    //public Vector2 BasisSize => node.CustomMinimumSize;

    void TrySetGadget(GameItemEntry itemEntry, string templateId)
    {
        var gadgetTemplate = GameItemTemplate.Get(templateId);
        itemEntry.SetItem(gadgetTemplate?.GadgetSingleton);
        itemEntry.SetInteractable(interactable);

        var stagesParent = itemEntry.GetNodeOrNull("%GadgetStages");
        if (stagesParent is null)
            return;

        int progress = 0;
        if (gadgetTemplate?.HomebaseNodeForGadget(out var nodeTemplateId) == true)
        {
            progress = currentItem
                ?.profile
                ?.GetFirstTemplateItem(nodeTemplateId)
                ?.quantity ?? 0;
        }

        ColorRect[] gadgetStages = [..stagesParent.GetChildren().OfType<ColorRect>()];
        for (int i = 0; i < gadgetStages.Length; i++)
        {
            gadgetStages[i].Color = progress > i ? Colors.White : Colors.Black;
        }
    }

    async void InteractCommander()
    {
        GD.Print("commander");
        if (editable && currentItem?.profile?.account.isOwned == true)
        {
            var current = commander.currentItem?.uuid;
            HashSet<string> exclusions = [.. currentItem.attributes["crew_members"].AsObject().Select(kvp => kvp.Value.ToString()).Except([current??""])];
            var newHero = await HeroItemSelector.OpenSelector(currentItem.profile.GetItems("Hero").Where(item =>
            {
                if (exclusions.Contains(item.uuid) || item.attributes?["squad_id"] is not null)
                    return false;
                return true;
            }), HeroItemSelector.CommanderConfig with
            {
                lastSelectedId = current
            });
            if(newHero is null)
            {
                GD.Print("cancelled");
                return;
            }
            //using var _ = LoadingOverlay.CreateToken();
            await currentItem.profile.PerformOperation("AssignHeroToLoadout", $$"""
            {
                "heroId": "{{newHero?.uuid}}",
                "loadoutId": "{{currentItem.uuid}}",
                "slotName": "CommanderSlot"
            }
            """);
        }
        else
        {
            commander.Inspect();
        }
    }

    async void InteractTeamPerk()
    {
        if (editable && currentItem?.profile?.account.isOwned == true)
        {
            var newTeamPerk = await HeroItemSelector.OpenSelector(currentItem.profile.GetItems("TeamPerk"), HeroItemSelector.TeamPerkConfig with
            {
                commanderType = commander.currentItem?.templateId,
                lastSelectedId = teamPerk.currentItem?.uuid
            });
            if (newTeamPerk is null)
            {
                GD.Print("cancelled");
                return;
            }
            //using var _ = LoadingOverlay.CreateToken();
            await currentItem.profile.PerformOperation("AssignTeamPerkToLoadout", $$"""
            {
                "teamPerkId": "{{newTeamPerk?.uuid}}",
                "loadoutId": "{{currentItem.uuid}}"
            }
            """);
        }
        else
        {
            teamPerk.Inspect();
        }
    }

    async void InteractSupport(int idx)
    {
        if (editable && currentItem?.profile?.account.isOwned == true)
        {
            var current = support[idx].currentItem?.uuid;
            HashSet<string> exclusions = [.. currentItem.attributes["crew_members"].AsObject().Select(kvp => kvp.Value.ToString()).Except([current ?? ""])];
            var newHero = await HeroItemSelector.OpenSelector(currentItem.profile.GetItems("Hero").Where(item =>
            {
                if (exclusions.Contains(item.uuid) || item.attributes?["squad_id"] is not null)
                    return false;
                return true;
            }), HeroItemSelector.SupportConfig with
            {
                commanderType = commander.currentItem?.templateId,
                teamPerkType = teamPerk.currentItem?.templateId,
                lastSelectedId = current
            });
            if (newHero is null)
            {
                GD.Print("cancelled");
                return;
            }
            if (newHero == GameItem.Empty)
                newHero = null;
            //using var _ = LoadingOverlay.CreateToken();
            await currentItem.profile.PerformOperation("AssignHeroToLoadout", $$"""
            {
                "heroId": "{{newHero?.uuid}}",
                "loadoutId": "{{currentItem.uuid}}",
                "slotName": "FollowerSlot{{idx+1}}"
            }
            """);
        }
        else
        {
            support[idx].Inspect();
        }
    }

    async void InteractGadget(int idx)
    {
        if (editable && currentItem?.profile?.account.isOwned == true)
        {
            var options = GameItemTemplate.GetTemplatesOfType("Gadget")
                .Where(g => currentItem?.profile.GetFirstTemplateItem(g.HomebaseNodeForGadget(out var node) ? node : null) is not null)
                .Select(g => g.GadgetSingleton).ToArray();
            var current = options.FirstOrDefault(g => g.templateId == gadgets[idx].currentItem?.templateId)?.uuid;

            var newGadget = await HeroItemSelector.OpenSelector(options, HeroItemSelector.DefaultConfig with
            {
                title = "Select Gadget",
                lastSelectedId = current,
                allowEmptySelection = true
            });
            if (newGadget is null)
            {
                GD.Print("cancelled");
                return;
            }
            //using var _ = LoadingOverlay.CreateToken();
            await currentItem.profile.PerformOperation("AssignGadgetToLoadout", $$"""
            {
                "gadgetId": "{{newGadget?.templateId}}",
                "loadoutId": "{{currentItem.uuid}}",
                "slotIndex": {{idx}}
            }
            """);
        }
        else
        {
            gadgets[idx].Inspect();
        }
    }

    protected override void UpdateSelectionVisuals()
    {
        if (selector is null || selectionFX is null)
            return;
        selectionFX.Visible = selector.IsSelected(currentItem);
        selectionFX.SelfModulate = selector.GetSelectableColor(currentItem);
    }
}
