using Godot;
using System.Collections.Generic;
using System.Text.Json;
using static HeroLoadoutEntry;

public partial class HeroEntry : GameItemEntry
{
    [Signal]
    public delegate void TeamPerkContributionVisibleEventHandler(bool visible);
    [Signal]
    public delegate void WarningVisibleEventHandler(bool visible);
    [Signal]
    public delegate void WarningTextEventHandler(string warningText);

    [Export(PropertyHint.ArrayType)]
    HeroAbilityEntry[] heroAbilityEntries;

    [Export]
    HeroAbilityEntry heroPerkEntry;

    [Export]
    HeroAbilityEntry heroCommanderPerkEntry;

    [Export]
    bool useHeroPerkDescription;
    [Export]
    bool useCommanderPerkDescription;

    public void SetTeamPerkContributor(bool val) =>
        EmitSignalTeamPerkContributionVisible(val);

    public void SetWarningForCommander(GameItemTemplate commanderTemplate)
    {
        string warning = null;
        EmitSignalWarningVisible(currentItem?.template.PerkCompatibleWithCommander(commanderTemplate, out warning) == false);
        EmitSignalWarningText(warning ?? commanderTemplate?.TemplateId);
    }

    protected override void UpdateItem(GameItem item)
    {
        base.UpdateItem(item);
        if (item?.template?.GetHeroAbilities() is GameItemTemplate[] abilityTemplates)
        {
            heroPerkEntry?.SetAbility(abilityTemplates[0], false);
            heroCommanderPerkEntry?.SetAbility(item.template.Tier < 2 ? abilityTemplates[0] : abilityTemplates[1]);
            for (int i = 0; i < 3; i++)
            {
                if (heroAbilityEntries.Length <= i)
                    break;
                heroAbilityEntries[i].SetAbility(abilityTemplates[i + 2], item.template.Tier <= i);
            }
        }
        if (item is not null && item != GameItem.Empty && selector is HeroItemSelector heroSelector)
        {
            SetWarningForCommander(heroSelector.Commander);
            SetTeamPerkContributor(heroSelector.TeamPerk?.TeamPerkBoostedByHero(item.template) ?? false); // set based on selector team perk settings
        }
    }

    protected override string CreateTooltip(GameItem displayItem, string itemName, string itemAmount, List<string> tooltipDescriptions)
    {
        if ((useCommanderPerkDescription || useHeroPerkDescription) && tooltipDescriptions.Count > 0 && displayItem?.template?.GetHeroAbilities() is GameItemTemplate[] abilityTemplates)
        {
            GameItemTemplate perkTemplate = displayItem.template.Tier < 2 || useHeroPerkDescription ? abilityTemplates[0] : abilityTemplates[1];
            if (perkTemplate is not null)
                tooltipDescriptions[0] = $"{perkTemplate.DisplayName}\n{perkTemplate.Description}";
        }
        return base.CreateTooltip(displayItem, itemName, itemAmount, tooltipDescriptions);
    }

    public override void ClearItem(Texture2D clearIcon)
    {
        base.ClearItem(clearIcon);
        heroPerkEntry?.ClearAbility();
        heroCommanderPerkEntry?.ClearAbility();
        for (int i = 0; i < 3; i++)
        {
            if (heroAbilityEntries.Length <= i)
                break;
            heroAbilityEntries[i].ClearAbility();
        }
        SetTeamPerkContributor(false);
        SetWarningForCommander(null);
    }
}
