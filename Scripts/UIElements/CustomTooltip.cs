using Godot;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json.Nodes;

public partial class CustomTooltip : Control
{
	[ExportGroup("Title")]
	[Export]
	Control titleContent;
	[Export]
	ShaderHook titleBanner;
	[Export]
	Label titleTextLabel;
	[Export]
	Control titleQuantityLayout;
	[Export]
	Label titleQuantityLabel;
	[ExportGroup("Description")]
	[Export]
	Control descriptionContent;
	[Export]
	float descriptionMaxWidth = 300;
	[Export]
	Label[] descriptionLayers;
	[ExportGroup("Offer")]
	[Export]
	Control offerContent;
	[Export]
	GameItemEntry offerCostEntry;
	[Export]
	GameItemEntry offerInventoryEntry;
	[Export]
	Label offerStockLabel;
	[ExportGroup("Abilities")]
	[Export]
	Control abilityContent;
	[Export]
	HeroAbilityEntry supportPerk;
	[Export]
	HeroAbilityEntry commanderPerk;
	[ExportGroup("Survivor")]
	[Export]
	Control survivorContent;
	[Export]
	TextureRect personalityIcon;
	[Export]
	Label personalityLabel;
	[Export]
	TextureRect survivorSecondIcon;
	[Export]
	Label survivorSecondLabel;

	public override void _Ready()
	{
		abilityContent.Size = abilityContent.GetCombinedMinimumSize() * Vector2.Right;
	}

	public static string GenerateSimpleTooltip(string title, string quantity = null, string[] description = null, string bannerCol = null, string offerId = null, string[] abilities = null, JsonObject survivorData = null, JsonObject extra = null)
	{
		JsonObject content = new()
		{
			["extra"] = extra,
			["title"] = new JsonObject()
			{
				["label"] = title,
				["quantity"] = quantity
			}
		};

		if (description is not null)
		{
			JsonArray descriptionArray = [.. description];
			content["description"] = descriptionArray;
		}
		JsonObject banner = null;
		if (bannerCol is not null)
		{
			banner ??= [];
			banner["color"] = bannerCol;
		}
		if (banner is not null)
			content["title"]["banner"] = banner;

		if (offerId is not null)
			content["offer"] = offerId;

		if (abilities is not null)
			content["abilities"] = new JsonArray([..abilities]);

		if (survivorData is not null)
			content["survivorData"] = survivorData.SafeDeepClone();

		return content.ToString();
	}

	public async void SetTooltip(string content)
	{
		JsonObject contentObject;

		if (!content.StartsWith('{'))
		{
			contentObject = new JsonObject()
			{
				["description"] = new JsonArray() { content }
			};
		}
		else
		{
			contentObject = JsonNode.Parse(content).AsObject();
		}

		JsonObject extraObject = contentObject["extra"]?.AsObject() ?? [];

		if (contentObject["title"]?.AsFlexibleObject("label") is JsonObject titleData)
		{
			titleContent.Visible = true;
			titleTextLabel.Text = titleData["label"]?.ToString();
			if (titleData["quantity"]?.ToString() is string quantityLabel)
			{
				titleQuantityLayout.Visible = true;
				titleQuantityLabel.Text = quantityLabel;
			}
			else
				titleQuantityLayout.Visible = false;
			if (titleData["banner"] is JsonObject bannerData)
			{
				titleBanner.Visible = true;
				titleBanner.SetTimeOffset(Time.GetTicksMsec() - 200);
				//set banner appearance
				titleBanner.SetShaderBool(extraObject["rainbow"]?.GetValue<bool>() == true, "rainbow");
				if (bannerData["color"]?.ToString() is string bannerColor)
				{
					//GD.Print(bannerColor);
					titleBanner.SetShaderColor(new Color(bannerColor), "Color");
					//titleBanner.SetShaderBool(true, "ColorMode");
				}
			}
			else
				titleBanner.Visible = false;
		}
		else
			titleContent.Visible = false;

		if (contentObject["description"] is JsonArray descriptionArray)
		{
			descriptionContent.Visible = true;
			if (descriptionArray.Count > descriptionLayers.Length)
				descriptionArray[descriptionLayers.Length - 1] = string.Join("\n", descriptionArray.Slice((descriptionLayers.Length - 1)..).Select(n => n.ToString()));
			for (int i = 0; i < descriptionLayers.Length; i++)
			{
				if (
					i >= descriptionArray.Count ||
					descriptionArray[i]?.ToString() is not string descText ||
					string.IsNullOrWhiteSpace(descText))
				{
					descriptionLayers[i].Visible = false;
					continue;
				}
				descriptionLayers[i].Visible = true;
				descriptionLayers[i].Text = descText.FixNewlines();

				descriptionLayers[i].AutowrapMode = TextServer.AutowrapMode.Off;
				descriptionLayers[i].CustomMinimumSize = Vector2.Zero;
				descriptionLayers[i].Size = Vector2.Zero;
				//GD.Print(descriptionLayers[i].GetMinimumSize());

				//descriptionLayers[i].UpdateMinimumSize();

				if (descriptionLayers[i].GetMinimumSize().X >= descriptionMaxWidth)
				{
					descriptionLayers[i].CustomMinimumSize = new(descriptionMaxWidth, 0);
					descriptionLayers[i].AutowrapMode = TextServer.AutowrapMode.WordSmart;
				}
			}
		}
		else
			descriptionContent.Visible = false;

		offerContent.Visible = false;
		if (contentObject["offer"] is JsonObject offerObj)
		{
			offerContent.Visible = true;
			offerStockLabel.Text = (offerObj["stock"]?.GetValue<int>().ToString()) ?? "Inf";
			var template = GameItemTemplate.Get(offerObj["costType"].ToString());
			offerCostEntry.SetItem(template.CreateInstance(offerObj["costAmount"].GetValue<int>()));
			if (GameAccount.ActiveAccount is GameAccount acc && acc.isAuthed)
			{
				offerInventoryEntry.Visible = true;
				var profileId = offerObj["costProfile"]?.ToString() ?? FnProfileTypes.AccountItems;
				var profile = await acc.GetProfile(profileId).Query();
				offerInventoryEntry.SetItem(profile.GetFirstTemplateItem(template.TemplateId));
			}
			else
				offerInventoryEntry.Visible = false;
		}
		else if (contentObject["offer"] is JsonValue val && val.GetValueKind() == System.Text.Json.JsonValueKind.String)
		{
			var acc = GameAccount.ActiveAccount;
			if (await acc.Authenticate())
			{
				var offerId = val.ToString();
				var offer = GameStorefront.GetExistingOffer(offerId);
				offerCostEntry.SetItem(offer.Price);
				offerInventoryEntry.SetItem(offer.GetCurrencyItem());
				var limit = await acc.GetStockLimit(offer);
				offerStockLabel.Text = $"In Stock: {limit}";
				offerStockLabel.Visible = limit < 999;
				offerContent.Visible = true;
			}
		}

		abilityContent.Visible = false;
		if (contentObject["abilities"] is JsonArray abilityArr)
		{
			var support = abilityArr.Count > 0 ? GameItemTemplate.Get(abilityArr[0].ToString()) : null;
			var command = abilityArr.Count > 1 ? GameItemTemplate.Get(abilityArr[1].ToString()) : null;

			abilityContent.Visible = support is not null || command is not null;

			supportPerk.Visible = support is not null;
			supportPerk.SetAbility(support);

			commanderPerk.Visible = command is not null;
			commanderPerk.SetAbility(command);
		}

		survivorContent.Visible = false;
		if (contentObject["survivorData"] is JsonObject survivorObj)
		{
			survivorContent.Visible = true;
			var personality = survivorObj["personality"]?.ToString();
			var setBonus = survivorObj["setBonus"]?.ToString();
			var subType = survivorObj["subType"]?.ToString();

			personalityIcon.Texture = PegLegResourceManager.supplimentaryData.PersonalityIcons.GetValueOrDefault(personality, PegLegResourceManager.defaultIcon);
			personalityLabel.Text = GameItem.ParseSurvivorAttribute(personality);

			survivorSecondIcon.Visible = setBonus is not null || subType is not null;
			survivorSecondLabel.Visible = survivorSecondIcon.Visible;
			if (setBonus is not null)
			{
				survivorSecondIcon.Texture = PegLegResourceManager.supplimentaryData.SetBonusIcons.GetValueOrDefault(setBonus, PegLegResourceManager.defaultIcon);
				survivorSecondLabel.Text = GameItem.ParseSurvivorAttribute(setBonus);
			}
			else if (subType is not null)
			{
				survivorSecondIcon.Texture = GameItemTemplate.GetSubtypeTexture(subType == "Martial Artist" ? "MartialArtist" : subType);
				survivorSecondLabel.Text = subType;
			}
		}
	}
}
