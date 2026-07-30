using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class CosmeticOfferEntryNew : Control, IListEntry<GameOffer>
{
	[Signal]
	public delegate void NameChangedEventHandler(string name);
	[Signal]
	public delegate void TypeChangedEventHandler(string type);
	[Signal]
	public delegate void SpecialTypeChangedEventHandler(bool isSpecial);
	[Signal]
	public delegate void TooltipChangedEventHandler(string tooltip);

	[Signal]
	public delegate void FreeVisibilityEventHandler(bool visible);
	[Signal]
	public delegate void OwnedVisibilityEventHandler(bool visible);

	[Signal]
	public delegate void BonusTextVisibilityEventHandler(bool visible);
	[Signal]
	public delegate void BonusTextChangedEventHandler(string name);

	[Signal]
	public delegate void PriceAmountEventHandler(string amount);
	[Signal]
	public delegate void BasePriceAmountEventHandler(string amount);
	[Signal]
	public delegate void DiscountAmountEventHandler(string amount);
	[Signal]
	public delegate void DiscountVisibilityEventHandler(bool visible);


	[Signal]
	public delegate void LastSeenTextEventHandler(string amount);
	[Signal]
	public delegate void LastSeenTooltipEventHandler(string text);
	[Signal]
	public delegate void LastSeenVisibilityEventHandler(bool visible);
	[Signal]
	public delegate void LastSeenAlertVisibilityEventHandler(bool visible);
	[Signal]
	public delegate void AlmostAYearVisibilityEventHandler(bool visible);

	[Export]
	float imageFetchDelay = 0.5f;
	[Export]
	float hoverTypeSize = 15;
	[Export]
	float hoverIconExtraScale = 0.1f;
	[Export]
	float hoverJamOffset = 20;
	[Export]
	bool skipLastSeen;
	[Export]
	bool useCustomFilter = false;

	[ExportGroup("Curves")]
	[Export]
	Curve jamRotationCurve;
	[Export]
	Curve jamScaleCurve;
	[ExportGroup("Nodes")]
	[Export]
	Control buffering;
	[Export]
	ShaderHook displayImage;
	[Export]
	ShaderHook background;
	[Export]
	Control textBackground;
	[Export]
	RefreshTimerHook leavingTimer;
	[Export]
	Control[] hoverSpaceTargets;
	[Export]
	Control filterOutPanel;

	Timer imageFetchTimer;
	int IListEntry<GameOffer>.CurrentIndexTarget { get; set; }
	IListProvider<GameOffer> IListEntry<GameOffer>.CurrentListProvider { get; set; }

	public Func<GameOffer, bool> filterOutPredicate;
	public GameOffer currentOffer { get; private set; }
	bool isCar;
	bool isJamTrack;
	int jamBPM;
	double jamBeats;
	float jamRotation;
	float jamScale;
	void IListEntry<GameOffer>.SetListEntryValue(GameOffer newValue) => SetOffer(newValue);

	public override void _Ready()
	{
		filterOutPanel.MouseFilter = (PegLegResourceManager.MagicNumbers["filteredCosmeticsInteractable"]?.GetValue<bool>() ?? false) ? MouseFilterEnum.Ignore : MouseFilterEnum.Stop;
		MouseEntered += SetHoverActive;
		MouseExited += SetHoverInactive;
		CosmeticShopInterfaceNew.OnFiltersChanged += OnFiltersChanged;
	}

	public override void _ExitTree()
	{
		CosmeticShopInterfaceNew.OnFiltersChanged -= OnFiltersChanged;
		currentOffer?.OnCosmeticImageRecieved -= UpdateImageFromRemote;
	}

	float HoverProgress
	{
		get => field;
		set
		{
			field = value;
			for (int i = 0; i < hoverSpaceTargets.Length; i++)
			{
				hoverSpaceTargets[i].CustomMinimumSize = Vector2.Down * (value * hoverTypeSize);
			}
			if (isJamTrack)
			{
				displayImage.OffsetTransformPosition = Vector2.Up * (value * hoverJamOffset);
				UpdateJamTransforms();
			}
			else if (isCar)
			{
				//scale via shader
				displayImage.SetShaderFloat(1 / (1 + (value * hoverIconExtraScale)), "CosmeticZoom");
			}
			else
			{
				displayImage.OffsetTransformScale = Vector2.One * (1 + (value * hoverIconExtraScale));
			}
		}
	}
	static readonly Color filterOutCol = Color.FromHsv(0, 0, 0.4f);

	private void OnFiltersChanged()
	{
		var predicate = useCustomFilter ? filterOutPredicate : CosmeticShopInterfaceNew.CurrentOfferFilter;
		bool filterOut = predicate?.Invoke(currentOffer) == false;
		filterOutPanel.Visible = filterOut;
		Modulate = filterOut ? filterOutCol : Colors.White;
	}

	public void SetOffer(GameOffer newValue)
	{
		if (currentOffer == newValue)
			return;
		if (newValue is null)
		{
			ClearOffer();
			return;
		}
		if (imageFetchTimer is null)
		{
			imageFetchTimer = new()
			{
				WaitTime = imageFetchDelay,
				Autostart = false,
				OneShot = true,
			};
			AddChild(imageFetchTimer);
			imageFetchTimer.Timeout += StartImageLoad;
		}

		currentOffer?.OnCosmeticImageRecieved -= UpdateImageFromRemote;
		currentOffer = newValue;
		currentOffer.OnCosmeticImageRecieved += UpdateImageFromRemote;

		OnFiltersChanged();

		var fnDashOffer = currentOffer.FNDashOffer;

		string displayName =
			currentOffer.CosmeticDisplayName ??
			currentOffer.rawData["devName"]?.ToString() ?? 
			$"<{currentOffer.OfferId}>";

		isJamTrack = GameStorefront.TryGetJamTrack(currentOffer.CosmeticPrimaryTemplate, out var jamMeta);
		jamBPM = isJamTrack ? jamMeta.beatsPerMinute : 0;
		if (isJamTrack)
			displayName = jamMeta.title;

		EmitSignalNameChanged(displayName);
		var price = currentOffer.Price?.quantity ?? 1;
		var basePrice = currentOffer.BasePrice?.quantity ?? 3;
		EmitSignalPriceAmount(price.Notate());

		EmitSignalDiscountVisibility(price != basePrice);
		if (price != basePrice)
		{
			EmitSignalDiscountAmount("-" + (basePrice - price).Notate());
			EmitSignalBasePriceAmount(basePrice.Notate());
		}

		bool isBundle = currentOffer.IsDynamicBundle;
		var offerMainType = currentOffer.CosmeticOfferMainType;
		var primaryTemplateType = currentOffer.CosmeticPrimaryTemplate?.Split(':')[0].Replace("Athena", "");

		var displayTemplateType = currentOffer.CosmeticDisplayType ?? primaryTemplateType;

		string typeText = offerMainType switch
		{
			"Bundle" => "Bundle",
			"Pack" => "Pack",
			"PackAndBonus" => "Pack & Bonus",
			"OutfitPack" => "Outfit Pack",
			"Vehicle" => "Car Body",
			"VehicleAndBonus" => "Car Body & Bonus",
			"MainItemTypeAndBonus" => displayTemplateType + " & Bonus",
			_ => displayTemplateType
		};

		bool isSpecial = false;
		if(fnDashOffer is not null)
		{
			List<string> modifiers = [];
			var cosmetics = fnDashOffer.AllCosmetics;
			if (cosmetics.Any(c => c.HasStyles))
				modifiers.Add("Selectable Styles");
			if (cosmetics.Any(c => c.HasBuiltIn))
				modifiers.Add("Built-In");

			if (modifiers.Count > 0)
			{
				isSpecial = true;
				typeText += $"  + {string.Join(", ", modifiers)}";
			}
		}

		EmitSignalTypeChanged(typeText);
		EmitSignalSpecialTypeChanged(isSpecial);

		//determine icon layout
		bool isCompact = Size == CustomMinimumSize;
		isCar = false;
		if (offerMainType == "VehicleAndBonus" || offerMainType=="Vehicle")
		{
			//car: full rect, fill aspect, shift 0.5, push top in by 30
			displayImage.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			displayImage.SetShaderBool(false, "Fit");
			displayImage.SetShaderFloat(0.5f, "Shift");
			displayImage.OffsetTop = 30;
			isCar = true;
		}
		else if (isBundle || primaryTemplateType == "Character")
		{
			//bundle or character: full rect, fill aspect, shift 0
			displayImage.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			displayImage.SetShaderBool(isCompact, "Fit"); //only fit when in compact mode
			displayImage.SetShaderFloat(0f, "Shift");
			var tileSize = currentOffer.CosmeticTileSize.X;
			if (isCompact)
			{
				//ignore tile size adjustments in compact mode
			} 
			else if (tileSize == 3)
			{
				//	if tile is 3x, push sides in by 30, and pull top out by 25
				displayImage.OffsetLeft = 30;
				displayImage.OffsetRight = -30;
				displayImage.OffsetTop = -25;
			}
			else if (tileSize == 4)
			{
				//	if tile is 4x, push sides in by 50, and pull top out by 30
				displayImage.OffsetLeft = 50;
				displayImage.OffsetRight = -50;
				displayImage.OffsetTop = -30;
			}
			displayImage.OffsetTop += 10;
		}
		else if (primaryTemplateType == "Backpack" || primaryTemplateType == "ItemWrap" || primaryTemplateType == "Dance")
		{
			//back bling or wrap: full rect, fit aspect, shift 0.33, configurable
			displayImage.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			displayImage.SetShaderBool(true, "Fit");
			displayImage.SetShaderFloat(isCompact ? 0 : 0.33f, "Shift"); //set shift to 0 when compact
		}
		else if (primaryTemplateType == "SparksSong" && isJamTrack)
		{
			//jam track: center top (anchor offset 0.15, configurable)
			displayImage.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterTop);
			displayImage.GrowVertical = GrowDirection.End;
			displayImage.AnchorTop = 0.15f;
			//also enable jam track mode for stuff like "dancing" when hovered
		}
		else
		{
			//default: full rect, fit aspect, shift 0
			displayImage.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
			displayImage.SetShaderBool(true, "Fit");
			displayImage.SetShaderFloat(0, "Shift");
			displayImage.OffsetTop += 10;
		}
		displayImage.SetShaderBool(isJamTrack, "IsJamTrack");

		var colours = currentOffer.CosmeticBGColours;
		background.SetShaderBool(colours.Length==3, "ThreeColourGradient");
		background.SetShaderColor(colours[0], "FirstCol");
		background.SetShaderColor(colours[1], "SecondCol");
		if(colours.Length == 3)
			background.SetShaderColor(colours[2], "ThirdCol");
		textBackground?.SelfModulate = currentOffer.CosmeticTextBGColour;


		if (currentOffer.OutDate is not null)
		{
			leavingTimer?.SetCustomRefreshTime(currentOffer.OutDate.Value, currentOffer.InDate);
			leavingTimer?.Visible = (currentOffer.OutDate.Value - currentOffer.InDate.Value).TotalDays < 99;
		}
		else
			leavingTimer?.Visible = false;

		var timeData = fnDashOffer?.GenerateCosmeticTimeData() ?? default;
		if (skipLastSeen)
		{
			EmitSignalLastSeenVisibility(false);
			EmitSignalLastSeenAlertVisibility(false);
			EmitSignalAlmostAYearVisibility(false);
		}
		else
		{
			EmitSignalLastSeenVisibility(timeData.lastSeenDaysAgo > 1);
			EmitSignalLastSeenText(timeData.lastSeenDaysAgo < 99 ? $"{timeData.lastSeenDaysAgo}d" : $"{timeData.lastSeenDaysAgo / 7}w");
			EmitSignalLastSeenTooltip($"""
			Last seen in shop:
			{timeData.lastAddedDate?.ToLocalTime() ?? DateTime.MinValue:d} ({timeData.lastSeenDaysAgo} days ago)
			""");
			EmitSignalLastSeenAlertVisibility(timeData.lastSeenDaysAgo > 500);
			EmitSignalAlmostAYearVisibility(timeData.lastSeenDaysAgo > 500);
		}

		timeData = timeData with { isAddedToday = timeData.isAddedToday || (DateTime.UtcNow - (currentOffer.InDate ?? DateTime.MinValue)).TotalDays < 1 };

		//mark when new
		string bonusText = null;
		if (((currentOffer.OutDate??DateTime.MaxValue)-DateTime.UtcNow).TotalDays<1)
		{
			bonusText = "LEAVES AT RESET";
		}
		else if (fnDashOffer is not null && timeData.isRecentlyNew && timeData.isAddedToday)
		{
			bonusText = "NEW TODAY";
		}
		else if (fnDashOffer is not null && timeData.isRecentlyNew)
		{
			bonusText = "RECENTLY NEW";
		}
		else if (timeData.isAddedToday)
		{
			bonusText = " # ";
		}
		EmitSignalBonusTextVisibility(bonusText is not null);
		EmitSignalBonusTextChanged(bonusText);

		displayImage.OffsetTransformPosition = Vector2.Zero;
		displayImage.OffsetTransformRotation = 0;
		displayImage.OffsetTransformScale = Vector2.One;
		displayImage.SetShaderFloat(1.0f, "CosmeticZoom");
		hoverTween?.Kill();
		HoverProgress = 0;

		//if pack or bundle, list contents
		//otherwise, list description off primary cosmetic
		EmitSignalTooltipChanged(CustomTooltip.GenerateSimpleTooltip(
			displayName+" ",
			null,
			[bonusText == " # " ? null : bonusText, typeText],
			colours[1].ToHtml(),
			currentOffer.OfferId
		));

		ImageTexture localImage = currentOffer.CosmeticCachedDisplayImage;
		displayImage.Texture = localImage ?? PegLegResourceManager.defaultIcon;
		buffering.Visible = localImage is null;
		displayImage.Visible = !buffering.Visible;

		if (buffering.Visible)
			Helpers.Defer(() => imageFetchTimer.Start());
	}

	Tween hoverTween;
	void SetHoverActive() => SetHover(true);
	void SetHoverInactive() => SetHover(false);
	void SetHover(bool hovered)
	{
		hoverTween?.Kill();
		hoverTween = CreateTween();
		hoverTween.TweenProperty(this, nameof(HoverProgress), hovered ? 1 : 0, 0.1f);
	}

	public override void _Process(double delta)
	{
		if (!isJamTrack || jamBPM <= 0)
			return;
		//TODO: if i ever implement jam track preview streaming, time beats based on that instead of an arbitrary timer
		if (HoverProgress <= 0)
		{
			jamBeats = -0.5;
			jamRotation = 0;
			jamScale = 0;
			return;
		}
		if (HoverProgress < 0.98 && jamBeats < 0)
			return;//only prevent beat progress while opening, not while closing
		double bps = jamBPM / 60d;
		jamBeats += bps * delta;
		jamRotation = jamRotationCurve?.Sample((float)((jamBeats+10) % 2.0)) ?? 0;
		jamScale = jamScaleCurve?.Sample((float)((jamBeats + 10) % 1.0)) ?? 0;
		UpdateJamTransforms();
	}

	void UpdateJamTransforms()
	{
		displayImage.OffsetTransformRotation = Mathf.DegToRad(HoverProgress * jamRotation);
		displayImage.OffsetTransformScale = Vector2.One * (1 + (HoverProgress * (hoverIconExtraScale+jamScale)));
	}

	private void StartImageLoad()
	{
		if (currentOffer is null)
			return;
		if (currentOffer.CosmeticLocalDisplayImage is ImageTexture local)
			UpdateImage(local);
		else
			currentOffer.FetchDisplayAssetImage();
	}

	private void UpdateImageFromRemote(ImageTexture texture)
	{
		if (texture is null)
			GD.Print("Failed to get image for: " + currentOffer?.OfferId);
		UpdateImage(texture);
	}

	private void UpdateImage(ImageTexture texture)
	{
		displayImage.Texture = texture ?? PegLegResourceManager.defaultIcon;
		displayImage.Visible = true;
		buffering.Visible = false;
	}

	private void OpenOffer()
	{
		if (currentOffer?.CosmeticURL is string url)
			OS.ShellOpen(url);
		else
			GenericConfirmationWindow.ShowError("Can't determine URL of Car Accessories").StartTask();
	}

	public void ClearOffer()
	{
		if (currentOffer is null)
			return;
		currentOffer.OnCosmeticImageRecieved -= UpdateImage;
		EmitSignalNameChanged("");
		imageFetchTimer?.Stop();
		displayImage.Texture = PegLegResourceManager.defaultIcon;
		currentOffer = null;
		hoverTween?.Kill();
		HoverProgress = 0;
	}
}
