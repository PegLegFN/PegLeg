using Godot;
using System;
using System.Collections.Generic;

public partial class TeamPerkEntry : GameItemEntry
{
	[Signal]
	public delegate void WarningVisibleEventHandler(bool visible);
	[Signal]
	public delegate void WarningTextEventHandler(string warningText);
	[Signal]
	public delegate void StateColorEventHandler(Color color);
	[Signal]
	public delegate void ProgressVisibleEventHandler(bool visible);
	[Signal]
	public delegate void ProgressTextEventHandler(string progressText);

	[Export]
	Color activeColor;
	[Export]
	Color inactiveColor;
	[Export]
	Color warningColor;

	protected override void UpdateItem(GameItem updatedItem)
	{
		base.UpdateItem(updatedItem);
		if (selector is HeroItemSelector heroSelector)
		{
			EmitSignalProgressVisible(false);
			SetWarningForCommander(heroSelector.Commander);
		}
		else
		{
			EmitSignalProgressVisible(displayItem?.templateId.StartsWith("TeamPerk:", StringComparison.OrdinalIgnoreCase) == true);
			SetTeamProgress(0);
			SetWarningForCommander(null);
		}
	}
	public void SetWarningForCommander(GameItemTemplate commanderTemplate)
	{
		string warning = null;
		compatible = currentItem?.template.PerkCompatibleWithCommander(commanderTemplate, out warning) != false;
		EmitSignalWarningVisible(!compatible);
		EmitSignalWarningText(warning ?? commanderTemplate?.TemplateId);
		UpdateColor();
	}

	bool compatible = false;
	bool meetsRequirements = false;

	public void SetTeamProgress(int matchCount)
	{
		var displayTemplate = displayItem?.template;
		if (displayTemplate?.Type.Equals("TeamPerk", StringComparison.OrdinalIgnoreCase) != true)
			return;
		bool progressive = displayTemplate["ProgressiveBonus"]?.GetValue<bool>() ?? false;
		int requirementAmt = displayTemplate["SupportRequirements"]?["MinimumQuantity"]?.GetValue<int>() ?? 1;
		meetsRequirements = matchCount >= requirementAmt;
		UpdateColor();
		EmitSignalProgressText(progressive ? $"x{matchCount}" : $"{Mathf.Min(matchCount, requirementAmt)}/{requirementAmt}");
	}

	void UpdateColor() => EmitSignalStateColor(compatible ? (meetsRequirements ? activeColor : inactiveColor) : warningColor);

	protected override string CreateTooltip(GameItem item, string itemName, string itemAmount, List<string> tooltipDescriptions)
	{
		if (item?.templateId.StartsWith("TeamPerk:", StringComparison.OrdinalIgnoreCase) == true && item?.template is GameItemTemplate teamPerkTemplate)
		{
			bool progressive = teamPerkTemplate["ProgressiveBonus"]?.GetValue<bool>() ?? false;

			string supportRequirementText = teamPerkTemplate["SupportRequirements"]?["Description"]?.ToString();
			if (progressive)
				tooltipDescriptions[0] = $"{supportRequirementText}\n{tooltipDescriptions[0]}";
			else
				tooltipDescriptions[0] += $"\n{supportRequirementText}";

			if (teamPerkTemplate["CommanderRequirement"]?["Description"]?.ToString() is string commanderRequirementText)
				tooltipDescriptions[0] += $"\n{commanderRequirementText}";
		}
		return base.CreateTooltip(item, itemName, itemAmount, tooltipDescriptions);
	}

	public override void ClearItem(Texture2D clearIcon)
	{
		base.ClearItem(clearIcon);
		EmitSignalProgressVisible(false);
		EmitSignalWarningVisible(false);
	}
}
