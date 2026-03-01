using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

public partial class HeroLoadoutEntry : GameItemEntry
{
    [Signal]
    public delegate void LoadoutNameEventHandler(string name);
    [Signal]
    public delegate void LoadoutNumberEventHandler(string name);
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
    Control altSelectionFX;
    [Export]
    bool useActiveAccount = true;
    [Export]
    bool interactable = true;
    [Export]
    bool editable = false;
    [Export]
    bool addCurrentToName = true;
    [Export]
    bool addNumberToName = false;

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
        ClearItem();
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
        currentItem = loadoutItem;
        if (currentItem is null)
        {
            ClearLoadout();
            return;
        }
        if (loadoutItem.templateId.StartsWith("CampaignHeroLoadout:"))
            SetAsLoadoutSlot();
        if (loadoutItem.templateId == GameAccount.HeroLoadoutBlueprintTID)
            SetAsLoadoutBlueprint();
        UpdateSelectionVisuals();
    }

    void SetAsLoadoutBlueprint()
    {
        EmitSignalLoadoutNumber("");
        if (currentItem.attributes["displayName"]?.ToString() is string displayName && !string.IsNullOrWhiteSpace(displayName))
        {
            EmitSignalLoadoutName(displayName);
        }
        else
        {
            EmitSignalLoadoutName("Loadout Blueprint");
        }

        var commanderNode = currentItem.attributes?["crew_members"]?["commanderslot"];
        var commanderTemplate = commanderNode?.Deserialize<GameAccount.LoadoutBlueprintHero?>(Helpers.JsonOptions.Fields)?.displayTemplate;
        GameItem commanderItem = GameItemTemplate.Get(commanderTemplate)?.CreateInstance();
        commander.SetItem(commanderItem);
        commander.SetInteractable(interactable);

        var tpTemplate = GameItemTemplate.Get(currentItem.attributes?["team_perk"]?.ToString());
        teamPerk.SetItem(tpTemplate?.CreateInstance());
        teamPerk.SetInteractable(interactable);

        int matchCount = 0;
        int limit = tpTemplate?["ProgressiveBonus"]?.GetValue<bool>() == true ? 6 : tpTemplate?.TeamPerkMinRequirements ?? 0;

        for (int i = 0; i < support.Length; i++)
        {
            var supportNode = currentItem.attributes["crew_members"][$"followerslot{i + 1}"];
            var supportTemplate = supportNode?.Deserialize<GameAccount.LoadoutBlueprintHero?>(Helpers.JsonOptions.Fields)?.displayTemplate;
            var supportHero = GameItemTemplate.Get(supportTemplate)?.CreateInstance();
            support[i].SetItem(supportHero);
            support[i].SetInteractable(interactable);
            support[i].SetTeamPerkContributor(false);
            support[i].SetWarningForCommander(null);

            if (supportHero is null)
                continue;

            support[i].SetWarningForCommander(commanderItem.template);

            if (tpTemplate is null || matchCount >= limit || !tpTemplate.TeamPerkBoostedByHero(supportHero.template))
                continue;
            support[i].SetTeamPerkContributor(true);
            matchCount++;
        }

        teamPerk.SetTeamProgress(matchCount);
        teamPerk.SetWarningForCommander(commanderItem.template);

        var gadgetTemplates = currentItem.attributes["gadgets"]?.Deserialize<string[]>();
        TrySetGadget(gadgets[0], gadgetTemplates.Length > 0 ? gadgetTemplates[0] : null);
        TrySetGadget(gadgets[1], gadgetTemplates.Length > 1 ? gadgetTemplates[1] : null);
    }

    void SetAsLoadoutSlot()
    {
        if (currentItem.profile is null)
        {
            ClearLoadout();
            return;
        }
        bool isCurrent = currentItem.profile?.statAttributes?["selected_hero_loadout"]?.ToString() == currentItem.uuid && !string.IsNullOrWhiteSpace(currentItem.uuid);
        EmitSignalIsCurrent(isCurrent);
        var idx = currentItem.attributes["loadout_index"]?.GetValue<int>() ?? 0;
        if (currentItem.customData?["displayName"]?.ToString() is string customName)
        {
            EmitSignalLoadoutName(customName);
            EmitSignalLoadoutNumber(currentItem.profile is null ? "" : $"#{idx + 1:00}");
        }
        else if (currentItem.profile?.account.GetCustomNameForLoadoutSlot(currentItem) is string customSlotName)
        {
            EmitSignalLoadoutName(customSlotName);
            EmitSignalLoadoutNumber($"#{idx + 1:00}");
        }
        else
        {
            EmitSignalLoadoutName($"Loadout Slot{(addNumberToName ? $" #{idx + 1:00}" : "")}");
            EmitSignalLoadoutNumber($"#{idx + 1:00}");
        }

        var commanderUUID = currentItem.attributes?["crew_members"]?["commanderslot"]?.ToString();
        if (commanderUUID is null)
        {
            GD.Print("Missing Commander in " + currentItem.uuid);
            GD.PushWarning("Missing Commander in " + currentItem.uuid);
            ClearLoadout();
            return;
        }

        var commanderItem = currentItem.profile.GetItem(currentItem.attributes["crew_members"]["commanderslot"].ToString());
        commander.SetItem(commanderItem);
        commander.SetInteractable(interactable);

        var tpGuid = currentItem.attributes["team_perk"]?.ToString();
        var tpItem = tpGuid is not null ? currentItem.profile.GetItem(tpGuid) : null;
        var tpTemplate = tpItem?.template;
        teamPerk.SetItem(tpItem);
        teamPerk.SetInteractable(interactable);

        int matchCount = 0;
        int limit = tpTemplate?["ProgressiveBonus"]?.GetValue<bool>() == true ? 6 : tpTemplate?.TeamPerkMinRequirements ?? 0;

        for (int i = 0; i < support.Length; i++)
        {
            var supportGuid = currentItem.attributes["crew_members"][$"followerslot{i + 1}"]?.ToString();
            var supportHero = supportGuid is not null ? currentItem.profile.GetItem(supportGuid) : null;
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

        var gadgetTemplates = currentItem.attributes["gadgets"]?.AsArray().OrderBy(g => g?["slot_index"]?.GetValue<int>()).Select(g => g?["gadget"]?.ToString()).ToArray() ?? [];
        TrySetGadget(gadgets[0], gadgetTemplates.Length > 0 ? gadgetTemplates[0] : null);
        TrySetGadget(gadgets[1], gadgetTemplates.Length > 1 ? gadgetTemplates[1] : null);
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
        if(Input.IsKeyPressed(Key.Shift) || !editable || currentItem?.profile?.account.isOwned != true)
        {
            commander.Inspect();
            return;
        }
        var current = commander.currentItem?.uuid;
        HashSet<string> exclusions = [.. currentItem.attributes["crew_members"].AsObject().Select(kvp => kvp.Value.ToString()).Except([current ?? ""])];
        var newHero = await HeroItemSelector.OpenSelector(currentItem.profile.GetItems("Hero").Where(item =>
        {
            if (exclusions.Contains(item.uuid) || item.attributes?["squad_id"] is not null)
                return false;
            return true;
        }), HeroItemSelector.CommanderConfig with
        {
            lastSelectedId = current
        });
        if (newHero is null)
        {
            //GD.Print("cancelled");
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

    async void InteractTeamPerk()
    {
        if (Input.IsKeyPressed(Key.Shift) || !editable || currentItem?.profile?.account.isOwned != true)
        {
            teamPerk.Inspect();
            return;
        }
        var newTeamPerk = await HeroItemSelector.OpenSelector(currentItem.profile.GetItems("TeamPerk"), HeroItemSelector.TeamPerkConfig with
        {
            commanderType = commander.currentItem?.templateId,
            lastSelectedId = teamPerk.currentItem?.uuid
        });
        if (newTeamPerk is null)
        {
            //GD.Print("cancelled");
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

    async void InteractSupport(int idx)
    {
        if (Input.IsKeyPressed(Key.Shift) || !editable || currentItem?.profile?.account.isOwned != true)
        {
            support[idx].Inspect();
            return;
        }
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
            //GD.Print("cancelled");
            return;
        }
        if (newHero == GameItem.Empty)
            newHero = null;
        //using var _ = LoadingOverlay.CreateToken();
        await currentItem.profile.PerformOperation("AssignHeroToLoadout", $$"""
        {
            "heroId": "{{newHero?.uuid}}",
            "loadoutId": "{{currentItem.uuid}}",
            "slotName": "FollowerSlot{{idx + 1}}"
        }
        """);
    }

    async void InteractGadget(int idx)
    {
        if (Input.IsKeyPressed(Key.Shift) || !editable || currentItem?.profile?.account.isOwned != true)
        {
            gadgets[idx].Inspect();
            return;
        }
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

    protected override void UpdateSelectionVisuals()
    {
        if (selector is null || selectionFX is null || altSelectionFX is null)
            return;
        var color = selector.GetSelectableColor(currentItem);
        bool useAlt = color == HeroLoadoutSlotSelector.transparantWhite;
        bool selected = selector.IsSelected(currentItem);

        altSelectionFX.Visible = useAlt && selected;
        selectionFX.Visible = !useAlt && selected;
        selectionFX.SelfModulate = selector.GetSelectableColor(currentItem);
    }

    public void ForceSelectionVisuals(bool selected, Color? selectableColor = null)
    {
        if (selectionFX is null)
            return;
        selectionFX.Visible = selected;
        selectionFX.SelfModulate = selectableColor ?? Colors.White;
    }
}
