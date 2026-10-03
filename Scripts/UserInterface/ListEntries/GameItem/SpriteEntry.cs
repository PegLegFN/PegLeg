using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

public partial class SpriteEntry : GameItemEntry
{
	[Export]
	ShaderHook bgShader;
	[Export]
	Control icon;
	[Export]
	Control levelInfo;
	[Export]
	Label levelLabel;
	[Export]
	Control summonInfo;
	[Export]
	Label summonLabel;
	[Export]
	Control neededInfo;
	[Export]
	Label neededLabel;
	[Export]
	Control masteryIndicator;
	[Export]
	float speed;

	public override void _Ready()
	{
		base._Ready();
		MouseEntered += () =>
		{
			hovered = true;
			UISounds.PlaySound("Hover");
			MoveToFront();
			TweenProgress();
		};
		MouseExited += () =>
		{
			hovered = false;
			TweenProgress();
		};
		Pressed += Squish;
	}

	public void SetPressed(bool isPressed)
	{
		pressed = isPressed;
		TweenProgress();
	}

	Tween progressTween;
	bool hovered = false;
	bool pressed = false;
	private void TweenProgress()
	{
		progressTween?.Kill();
		progressTween = CreateTween();
		float target = hovered ? (pressed ? 0.5f : 1) : 0;
		var duration = MathF.Abs(Progress - target) / speed;
		progressTween.TweenProperty(this, "Progress", target, duration)
			.SetTrans(Tween.TransitionType.Quad);
	}

	float Progress
	{
		get => field;
		set
		{
			field = value;
			bgShader.SetShaderFloat(value, "progress");
		}
	}

	Tween squishTween;
	Tween rotateTween;
	void Squish()
	{
		if (!canSquish)
			return;
		UISounds.PlaySound("Feddy");

		squishTween?.Kill();
		squishTween = CreateTween().SetTrans(Tween.TransitionType.Sine).SetParallel();
		squishTween.TweenProperty(icon, "offset_transform_scale", new Vector2(1.25f, 0.75f), 0.1);
		squishTween.TweenProperty(icon, "offset_transform_scale", new Vector2(0.9f, 1.2f), 0.1).SetDelay(0.1);
		squishTween.TweenProperty(icon, "offset_transform_scale", new Vector2(1f, 1f), 0.1).SetDelay(0.2);

		//rotateTween?.Kill();
		//rotateTween = CreateTween().SetTrans(Tween.TransitionType.Back).SetParallel();
		//rotateTween.TweenProperty(icon, "offset_transform_rotation", Mathf.DegToRad(-5), 0.05).SetDelay(0.1);
		//rotateTween.TweenProperty(icon, "offset_transform_rotation", Mathf.DegToRad(5), 0.1).SetDelay(0.15);
		//rotateTween.TweenProperty(icon, "offset_transform_rotation", Mathf.DegToRad(0), 0.05).SetDelay(0.25);
	}

	public override void ClearItem(Texture2D clearIcon)
	{
		base.ClearItem(clearIcon);
		canSquish = false;
		specialPerk = null;
	}

	bool canSquish = false;

	protected override void UpdateItem(GameItem updatedItem)
	{
		if (!IsInstanceValid(this) || !IsInsideTree())
			return;
		base.UpdateItem(updatedItem);
		if (displayItem is null)
			return;

		var augments = displayItem.template?["Augments"]?.Deserialize<string[]>() ?? [];

		bgShader.SetShaderBool(augments.Length > 1, "special");
		var quantity = displayItem.quantity;
		levelInfo.Visible = quantity == 2;
		canSquish = quantity == 2;
		if (levelInfo.Visible)
		{
			var xp = displayItem.attributes?["xp"]?.GetValue<int>() ?? 0;
			//text from XP
			var level = xp switch
			{
				>= 4000 => 5,
				>= 2200 => 4,
				>= 1000 => 3,
				>= 400 => 2,
				_ => 1
			};
			levelLabel.Text = level.ToString();
			levelInfo.TooltipText = $"{xp.Notate()} XP";
		}
		summonInfo.Visible = false;

		bool mastered = displayItem.attributes?["ml"]?.GetValue<bool>() == true;
		if (quantity == 1 && displayItem.attributes?["summonCost"]?.GetValue<int>() is int cost)
		{
			summonInfo.Visible = true;
			summonLabel.Text = (mastered ? cost / 2 : cost).Notate();
		}

		masteryIndicator.Visible = mastered;
		bgShader.SetShaderBool(quantity == 1, "lost");
		bgShader.SetShaderBool(quantity == 3, "locked");//cant make an item with 0 quantity (well i could but that would be rude to the codebase), so locked items are repressented as quantity 3

		var needType = displayItem.customData["needed"]?.GetValue<int>() ?? 0;
		neededInfo.Visible = needType > 0;
		if (neededInfo.Visible)
		{
			neededInfo.SelfModulate = needType == 1 ? Colors.Aqua : Colors.OrangeRed;
			neededLabel.Text = needType == 1 ? "You Need" : "They Need";
		}

		specialPerk = augments.Length > 1 ? GameItemTemplate.Get(augments[1]) : null;
		SetTooltip();
	}

	GameItemTemplate specialPerk;

	protected override void SetTooltip()
	{
		if (displayItem is null)
			return;

		var desc = DisplayDescription;

		if(specialPerk is not null)
		{
			desc += $"\n\n{specialPerk.ItemName.ToUpper()}: {specialPerk.ItemDescription}";
		}

		List<string> tooltipDescriptions =
		[
			desc
		];
		if (displayItem.GetSearchTags() is JsonArray tagArray && tagArray.Count > 0)
			tooltipDescriptions.Add("Search Tags: " + tagArray.Select(t => t?.ToString()).Where(t => !t.StartsWith("hidetag_")).ToArray().Join(", "));

		if (displayItem.template is null)
			tooltipDescriptions[0] = "Err: Missing Template";

		var tooltip = CustomTooltip.GenerateSimpleTooltip(
			displayItem.template?.ItemName ?? displayItem.templateId?.Split(":")[1],
			null,
			[.. tooltipDescriptions],
			(displayItem.template?.RarityColor ?? missingRarityColor).ToHtml(),
			extra: new() {
				["rainbow"]= (displayItem.template?["Augments"]?.AsArray().Count ?? 0) > 1
			}
		);
		EmitSignalTooltipChanged(tooltip);
	}
}
