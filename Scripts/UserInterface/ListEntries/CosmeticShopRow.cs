using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

public partial class CosmeticShopRow : Control
{
    [Export]
    PackedScene cosmeticShopEntry;
    [Export]
    Control entryParent;
    [Export]
    Control loadingCubes;
    [Export]
    Vector2 cellUnits = new(150,150);
    [Export]
    Vector2 cellSpacing = new(5,5);
    [Export]
    Control contentNode;
    [Export]
    float largeSize = 333;
    [Export]
    float smallSize = 178;
    [Export]
    float largeContentSize = 325;
    [Export]
    float smallContentSize = 175;
    [Export]
    float v2MaxWidth = 800;
    [Export]
    float v2Height = 300;
    [Export]
    bool v2Expand = false;

    public JsonObject PageData { get; set; }

    public override void _Ready()
    {
        loadingCubes.Visible = true;
        entryParent.Visible = false;
    }
    List<CosmeticShopOfferEntry> activeEntries = [];
    public async Task PopulatePage(CosmeticShopInterface parent)
    {
        if (PageData is null)
            return;
        int offerCount = 0;
        int maxVisibleOffers = v2Expand ? Mathf.Min(PageData.Select(o => int.Parse(o.Value["tileSize"].ToString().Split("_")[1])).Sum(), 4) : 4;
        Vector2 cellUnitsV2 = new((v2MaxWidth - (cellSpacing.X * (maxVisibleOffers - 1))) / maxVisibleOffers, v2Height);
        foreach (var offer in PageData)
        {
            offerCount++;
            if (offerCount > 4)
                break;
            var entry = cosmeticShopEntry.Instantiate<CosmeticShopOfferEntry>();
            var splitCellText = offer.Value["tileSize"].ToString().Split("_");
            Vector2 cellSize = new(int.Parse(splitCellText[1]), int.Parse(splitCellText[3]));
            Vector2 finalSize = (cellSize * (cellUnitsV2 + cellSpacing)) - cellSpacing;
            entry.CustomMinimumSize = finalSize;
            entryParent.AddChild(entry);
            entry.PopulateEntry(offer.Value.AsObject(), cellSize);
            parent.RegisterOffer(entry);
            activeEntries.Add(entry);

            await Helpers.WaitForFrame();
            if (!IsInsideTree())
                return;
        }
        entryParent.Visible = true;
        loadingCubes.Visible = false;
    }

    public bool FilterPage(Predicate<CosmeticShopOfferEntry> predicate) =>
        Visible = activeEntries.Any(predicate.ToFunc());
}
