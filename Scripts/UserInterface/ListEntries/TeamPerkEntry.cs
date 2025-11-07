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
        EmitSignalProgressVisible(displayItem?.templateId.StartsWith("TeamPerk:", StringComparison.OrdinalIgnoreCase) == true);
        EmitSignalWarningVisible(false);
    }

    public void SetTeamProgress(int matchCount, bool isIncompatible)
    {
        var displayTemplate = displayItem?.template;
        if (displayTemplate?.Type.Equals("TeamPerk", StringComparison.OrdinalIgnoreCase) != true)
            return;
        bool progressive = displayTemplate["ProgressiveBonus"]?.GetValue<bool>() ?? false;
        int requirementAmt = displayTemplate["SupportRequirements"]?["MinimumQuantity"]?.GetValue<int>() ?? 1;
        //GD.Print("req: "+requirementAmt);
        EmitSignalProgressText(progressive ? $"x{matchCount}" : $"{Mathf.Min(matchCount, requirementAmt)}/{requirementAmt}");
        EmitSignalStateColor((matchCount >= requirementAmt && !isIncompatible) ? activeColor : inactiveColor);
        EmitSignalWarningVisible(isIncompatible);
        if (isIncompatible)
            EmitSignalWarningText(displayTemplate["CommanderRequirement"]?["Description"]?.ToString());
    }

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
