using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;

public partial class CardPackEntry : GameItemEntry
{
	[Signal]
	public delegate void LlamaPressedEventHandler(string itemId);

	[Signal]
	public delegate void Color1ChangedEventHandler(Color color);
	[Signal]
	public delegate void Color2ChangedEventHandler(Color color);
	[Signal]
	public delegate void Color3ChangedEventHandler(Color color);
	[Signal]
	public delegate void GradientChangedEventHandler(Gradient gradient);

	[Export]
	bool includeAmountInName;
	[Export]
	public bool debug = false;

	const string defaultPreviewImage = "PinataStandardPack";
	//static JsonObject llamaColorData;

	Color[] currentLlamaColors;
	public Gradient currentLlamaGradient { get; private set; }

	protected override void UpdateItem(GameItem item)
	{
		if (!IsInstanceValid(this) || !IsInsideTree())
			return;
		if (item is null)
		{
			ClearItem();
			return;
		}

		if (item.template?.Type != "CardPack")
		{
			GD.Print("not a cardpack");
			base.UpdateItem(item);
			var basisColor = item.template?.RarityColor ?? missingRarityColor;
			SetColours([
				basisColor,
				basisColor*1.05f,
				basisColor*1.1f,
				basisColor*1.15f,
			]);
			return;
		}
		displayItem = item;

		string name = item.template.ItemName;
		int amount = Mathf.Max(item.customData["stackQuantity"]?.GetValue<int>() ?? 0,  item.quantity);
		amount = Mathf.Max(item.customData["shopQuantity"]?.GetValue<int>() ?? 0, amount);
		//string nameWithAmount = amount >= 0 ? $"{name} ({amount} left)" : name;
		string nameWithAmount = name;
		string description = item.template.ItemDescription;

		EmitSignalNameChanged((includeAmountInName && amount >= 0) ? nameWithAmount : name);
		EmitSignalDescriptionChanged(description);
		EmitSignalNotificationChanged(!item.IsSeen);
		SetInteractable(DefaultInteractable(displayItem));

		string amountText = amount.ToString();
		if (addXToAmount)
			amountText = "x" + amountText;
		if (amount <= (showSingleItemAmount ? 0 : 1))
			amountText = null;
		EmitSignalAmountChanged(amountText ?? null);


		int llamaTier = item.customData?["llamaTier"]?.GetValue<int>() ?? 0;
		if (item.template.Rarity == "Legendary")
			llamaTier = 2;//force gold tier for legendary rarity llamas
		string llamaPinataName =
			(item.template.TryGetTexturePath(out var imagePath) ? imagePath : null)
			?.ToString().Split("\\")[^1];
		if (llamaPinataName?.StartsWith(defaultPreviewImage) ?? false)
		{
			llamaPinataName = llamaTier switch
			{
				2 => "Gold",
				1 => "Silver",
				_ => "Standard"
			};
		}

		var colors = PegLegResourceManager.LlamaData.FindColours(llamaPinataName);

		if (item.attributes?.ContainsKey("options") == true)
			colors[0] = item?.template?.RarityColor ?? colors[0];

		SetColours(colors);

		EmitSignalTooltipChanged(
			CustomTooltip.GenerateSimpleTooltip(
				name,
				amountText,
				[description],
				colors[0].ToHtml()
			)
		);

		//var packIcon = item.GetTexture(FnItemTextureType.PackImage);
		//if (!llamaPinataName.Contains("Pinata") || (name?.Contains("Mini") ?? false))
		//{
		//    GD.Print(llamaPinataName);
		//    packIcon = null;
		//}

		EmitSignalIconChanged(item.GetTexture());
		EmitSignalSubtypeIconChanged(item.GetTexture(FnItemTextureType.PackImage));
	}

	void SetColours(Color[] colors)
	{
		currentLlamaColors = colors;
		currentLlamaGradient ??= new()
		{
			Offsets = [0, 0.25f, 0.5f, 0.75f],
			InterpolationMode = Gradient.InterpolationModeEnum.Constant,
		};
		currentLlamaGradient.Colors = currentLlamaColors;

		EmitSignalRarityChanged(currentLlamaColors[0]);
		EmitSignalColor1Changed(currentLlamaColors[1]);
		EmitSignalColor2Changed(currentLlamaColors[2]);
		EmitSignalColor3Changed(currentLlamaColors[3]);
		EmitSignalGradientChanged(currentLlamaGradient);
	}

	public override void ClearItem(Texture2D clearTexture)
	{
		base.ClearItem(clearTexture);
		EmitSignalNameChanged("Select a Llama");
		EmitSignalIconChanged(GameItem.llamaTierIcons[0]);
		EmitSignalSubtypeIconChanged(PegLegResourceManager.defaultIcon);
	}

	public override void EmitPressedSignal()
	{
		selectionGraphics?.ButtonPressed = true;
		if (currentItem?.uuid is not null)
			EmitSignalLlamaPressed(currentItem.uuid);
	}
}
