using Godot;
using System;
using System.Linq;

public partial class ChoiceCardEntry : GameItemEntry
{
	[Export]
	GameItemEntry displayEntry;
	[Export]
	ShaderHook cardShader;
	[Export]
	SubViewport cardViewport;
	[Export]
	Label ownedLabel;

	protected override void UpdateItem(GameItem updatedItem)
	{
		displayEntry.SetItem(updatedItem);
		cardViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;

		var prefix = updatedItem.template.GetTemplatePrefix();
		var rarityLevel = updatedItem.template.RarityLevel;
		var matchCount = GameAccount.ActiveAccount.GetProfile("campaign").GetItems(updatedItem.template.Type, i =>
		{
			if (i.template?.GetTemplatePrefix() != prefix)
				return false;
			return i.template.RarityLevel >= rarityLevel;
		}).Length;
		ownedLabel.Text = $"You own {matchCount} of equal or better rarity";
	}

	public override void ClearItem(Texture2D clearIcon)
	{
		displayEntry.ClearItem(clearIcon);
		cardViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
		ownedLabel.Text = "";
	}

	public float FlipProgress
	{
		get => cardShader.GetShaderFloat("NormalisedManual");
		set => cardShader.SetShaderFloat(Mathf.Clamp(value, 0, 1), "NormalisedManual");
	}
	public float BurnProgress
	{
		get => cardShader.GetShaderFloat("BurnProgress");
		set => cardShader.SetShaderFloat(Mathf.Clamp(value, 0, 1), "BurnProgress");
	}

	public float LabelOpacity
	{
		get => ownedLabel.SelfModulate.A;
		set => ownedLabel.SelfModulate = Colors.Transparent.Lerp(Colors.White, Mathf.Clamp(value, 0, 1));
	}
}
