using Godot;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

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
    bool debug = false;

    const string defaultPreviewImage = "PinataStandardPack";
    static JsonObject llamaColorData;

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

        string name = item.template.DisplayName;
        int amount = item.customData["stackQuantity"]?.GetValue<int>() ?? item.quantity;
        int shopAmount = item.customData["shopQuantity"]?.GetValue<int>() ?? amount;
        string nameWithAmount = amount >= 0 ? $"{name} ({shopAmount} left)" : name;
        string description = item.template.Description;

        EmitSignal(SignalName.NameChanged, (includeAmountInName && shopAmount >= 0) ? nameWithAmount : name);
        EmitSignal(SignalName.DescriptionChanged, description);
        EmitSignal(SignalName.NotificationChanged, !item.IsSeen);

        string amountText = amount.ToString();
        if (addXToAmount)
            amountText = "x" + amountText;
        if (amount <= (showSingleItemAmount ? 0 : 1))
            amountText = null;
        EmitSignal(SignalName.AmountChanged, amountText ?? null);


        int llamaTier = item.customData?["llamaTier"]?.GetValue<int>() ?? 0;
        if (debug)
            GD.Print("cardPackTier: " + llamaTier);
        string llamaPinataName =
            (item.template.TryGetTexturePath(out var imagePath) ? imagePath : null)
            ?.ToString().Split("\\")[^1];
        if (llamaPinataName?.StartsWith(defaultPreviewImage) ?? false)
        {
            if (debug)
                GD.Print("using tier");
            llamaPinataName = llamaTier switch
            {
                2 => "Gold",
                1 => "Silver",
                _ => "Standard"
            };
        }

        Color? rarityColor = null;
        if (item.attributes?.ContainsKey("options") ?? false)
            rarityColor = item.template.RarityColor;

        llamaColorData ??= OverridableFileLoader.LoadJsonFile("llamaColors.json");
        JsonArray colorData = llamaColorData?.FirstOrDefault(kvp => llamaPinataName?.StartsWith(kvp.Key) ?? false).Value?.AsArray();

        SetColours([
            rarityColor ?? Color.FromString(colorData?[0]?.ToString() ?? "", new("#0073ffff")),
            Color.FromString(colorData?[1]?.ToString() ?? "", new("#e600c3e3")),
            Color.FromString(colorData?[2]?.ToString() ?? "", new("#aa00ffd4")),
            Color.FromString(colorData?[3]?.ToString() ?? "", new("#00eaff8f"))
        ]);


        EmitSignalTooltipChanged(
            CustomTooltip.GenerateSimpleTooltip(
                name,
                amountText,
                [description],
                currentLlamaColors[0].ToHtml()
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
        EmitSignal(SignalName.Color1Changed, currentLlamaColors[1]);
        EmitSignal(SignalName.Color2Changed, currentLlamaColors[2]);
        EmitSignal(SignalName.Color3Changed, currentLlamaColors[3]);
        EmitSignal(SignalName.GradientChanged, currentLlamaGradient);
    }

    public override void ClearItem(Texture2D clearTexture)
    {
        base.ClearItem(clearTexture);
        EmitSignal(SignalName.NameChanged, "Select a Llama");
        EmitSignal(SignalName.IconChanged, GameItem.llamaTierIcons[0]);
        EmitSignal(SignalName.SubtypeIconChanged, PegLegResourceManager.defaultIcon);
    }

    public override void EmitPressedSignal()
    {
        if (selectionGraphics is not null)
            selectionGraphics.ButtonPressed = true;
        if (currentItem?.uuid is not null)
            EmitSignal(SignalName.LlamaPressed, currentItem.uuid);
    }
}
