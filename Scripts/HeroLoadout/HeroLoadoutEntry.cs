using Godot;
using Polly;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

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
    bool addCurrentToName = true;

    public override void _Ready()
    {
        ClearItem();
        if (useActiveAccount)
        {
            GameAccount.ActiveAccountChanged += UpdateActive;
            SetAccount(GameAccount.ActiveAccount);
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

    struct CommanderRequirement
    {
        public string Description;
        public string[] CommanderTag;
        public string CommanderSubType;

        public bool IsMatch(GameItemTemplate template)
        {
            if(template is null)
                return false;
            if (CommanderSubType is not null && template.SubType != CommanderSubType)
                return false;
            else if (CommanderTag is not null)
            {
                var targetTags = CommanderTag.ToHashSet();
                var commanderTags = template["HeroTags"]?.Deserialize<string[]>().ToHashSet();
                if (targetTags.All(t => !commanderTags.Contains(t)))
                    return false;
            }

            return true;
        }
    }

    struct TeamPerkSupportRequirements()
    {
        public string Description;
        public int MinimumQuantity = 1;
        public string[] HeroTags;
        public string HeroSubType;
        public int? MinimumTier;
        public string MinimumRarity;

        public bool IsMatch(GameItemTemplate template)
        {
            if (template is null)
                return false;

            if (HeroSubType is not null && template.SubType != HeroSubType)
                return false;

            if (HeroTags is not null && HeroTags.Length > 0)
            {
                var targetTags = HeroTags.ToHashSet();
                var heroTags = template["HeroTags"]?.Deserialize<string[]>().ToHashSet();
                if (targetTags.All(t => !heroTags.Contains(t)))
                    return false;
            }

            if (MinimumTier is int tier && template.Tier < tier)
                return false;

            if (MinimumRarity is not null && template.RarityLevel < MinimumRarity.ConvertRarityString())
                return false;

            return true;
        }
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
            support[i].SetWarningVisibility(false);
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

        bool tpIncompatible = false;
        if (tpTemplate?["CommanderRequirement"]?.Deserialize<CommanderRequirement>(Helpers.JsonOptions.Fields) is CommanderRequirement tpCommanderReqs)
            tpIncompatible = !tpCommanderReqs.IsMatch(commanderItem.template);
        var tpSupportReqs = tpTemplate?["SupportRequirements"].Deserialize<TeamPerkSupportRequirements>(Helpers.JsonOptions.Fields) ?? default;
        int matchCount = 0;
        int limit = tpTemplate?["ProgressiveBonus"]?.GetValue<bool>() == true ? 6 : tpSupportReqs.MinimumQuantity;

        for (int i = 0; i < support.Length; i++)
        {
            var supportGuid = loadoutItem.attributes["crew_members"][$"followerslot{i + 1}"]?.ToString();
            var supportHero = supportGuid is not null ? loadoutItem.profile.GetItem(supportGuid) : null;
            support[i].SetItem(supportHero);
            support[i].SetInteractable(interactable);
            support[i].SetTeamPerkContributor(false);
            support[i].SetWarningVisibility(false);

            var supportTemplate = supportHero?.template;
            if (supportTemplate is null)
                continue;

            if (supportTemplate?["HeroPerkRequirement"]?.Deserialize<CommanderRequirement>(Helpers.JsonOptions.Fields) is CommanderRequirement supportCommanderReqs)
                support[i].SetWarningVisibility(!supportCommanderReqs.IsMatch(commanderItem.template));

            if (tpTemplate is null || matchCount >= limit || !tpSupportReqs.IsMatch(supportTemplate))
                continue;
            support[i].SetTeamPerkContributor(true);
            matchCount++;
        }

        //GD.Print($"matched: {matchCount}/{limit}");
        teamPerk.SetTeamProgress(matchCount, tpIncompatible);

        if ((loadoutItem.attributes["gadgets"]?.AsArray().Count ?? 0) > 0)
            TrySetGadget(gadgets[0], loadoutItem.attributes["gadgets"]?[0]?["gadget"]?.ToString());
        else
            TrySetGadget(gadgets[0], null);

        if ((loadoutItem.attributes["gadgets"]?.AsArray().Count ?? 0) > 1)
            TrySetGadget(gadgets[1], loadoutItem.attributes["gadgets"]?[1]?["gadget"]?.ToString());
        else
            TrySetGadget(gadgets[1], null);
    }

    static Dictionary<string, GameItem> gadgetLookup = [];
    static Dictionary<string, string> gadgetNodeMap = new()
    {
        ["g_airstrike"] = "skilltree_airstrike",
        ["g_generic_adrenalinerush"] = "skilltree_adrenalinerush",
        ["g_generic_banner"] = "skilltree_banner",
        ["g_generic_botturret"] = "skilltree_hoverturret",
        ["g_generic_proximitymines"] = "skilltree_proximitymine",
        ["g_generic_slowfield"] = "skilltree_slowfield",
        ["g_supplydrop"] = "skilltree_supplydrop",
        ["g_teleporter"] = "skilltree_teleporter",
    };

    public Vector2 BasisSize => node.CustomMinimumSize;

    void TrySetGadget(GameItemEntry itemEntry, string templateId)
    {
        GameItem gadgetItem;
        if (templateId is null)
            gadgetItem = null;
        else if (gadgetLookup.TryGetValue(templateId, out var existing))
            gadgetItem = existing;
        else
            gadgetItem = gadgetLookup[templateId] = GameItemTemplate.Get(templateId)?.CreateInstance();
        itemEntry.SetItem(gadgetItem);
        itemEntry.SetInteractable(interactable);

        var stagesParent = itemEntry.GetNodeOrNull("%GadgetStages");
        if (stagesParent is null)
            return;

        int progress = 0;
        if (!string.IsNullOrWhiteSpace(templateId))
        {
            var nodeTemplateId = $"HomebaseNode:{(gadgetNodeMap.TryGetValue(templateId.Split(":")[1], out var nodeName) ? nodeName : "")}";
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

    protected override void UpdateSelectionVisuals()
    {
        if (selector is null || selectionFX is null)
            return;
        selectionFX.Visible = selector.IsSelected(currentItem);
        selectionFX.SelfModulate = selector.GetSelectableColor(currentItem);
    }
}
