using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

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

	protected override void SetTooltip()
	{
		if (displayItem is null)
			return;
		if (displayItem.template?.Type != "Hero")
		{
			base.SetTooltip();
			return;
		}

		List<string> tooltipDescriptions =
		[
			DisplayDescription,
			//"Item Id: " + item.templateId,
		];
		if (displayItem.GetSearchTags() is JsonArray tagArray && tagArray.Count > 0)
			tooltipDescriptions.Add("Search Tags: " + tagArray.Select(t => t?.ToString()).Where(t => !t.StartsWith("hidetag_")).ToArray().Join(", "));

		string perkTemplateId = null;
		if ((useCommanderPerkDescription || useHeroPerkDescription) && displayItem.template.GetHeroAbilities() is GameItemTemplate[] abilityTemplates)
			perkTemplateId = (displayItem.template.Tier < 2 || useHeroPerkDescription ? abilityTemplates[0] : abilityTemplates[1])?.TemplateId;

		var tooltip = CustomTooltip.GenerateSimpleTooltip(
			displayItem.template?.DisplayName ?? displayItem.templateId?.Split(":")[1],
			null,
			perkTemplateId is not null ? null : [.. tooltipDescriptions],
			(displayItem.template?.RarityColor ?? missingRarityColor).ToHtml(),
			abilities: perkTemplateId is not null ? [perkTemplateId] : null
		);
		EmitSignalTooltipChanged(tooltip);
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
