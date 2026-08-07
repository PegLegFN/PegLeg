using Godot;
using System.Linq;

public partial class PerkEntry : Control
{
	[Signal]
	public delegate void NameChangedEventHandler(string name);
	[Signal]
	public delegate void RarityIconChangedEventHandler(Texture2D rarityIcon);
	[Signal]
	public delegate void RarityIconVisibilityChangedEventHandler(bool newValue);
	[Signal]
	public delegate void LockVisibilityChangedEventHandler(bool newValue);
	[Signal]
	public delegate void LockTextChangedEventHandler(string newValue);
	[Signal]
	public delegate void LockColorChangedEventHandler(Color newValue);
	[Signal]
	public delegate void ElementIconChangedEventHandler(Texture2D rarityIcon);
	[Signal]
	public delegate void ElementIconVisibilityChangedEventHandler(bool newValue);
	[Signal]
	public delegate void InteractableChangedEventHandler(bool newValue);
	[Signal]
	public delegate void PressedEventHandler(int index, string alterationId, bool replaceable);

	static readonly string[] rarityTemplates =
	[
		"AccountResource:reagent_alteration_generic",
		"AccountResource:reagent_alteration_upgrade_uc",
		"AccountResource:reagent_alteration_upgrade_r",
		"AccountResource:reagent_alteration_upgrade_vr",
		"AccountResource:reagent_alteration_upgrade_sr",
		"AccountResource:reagent_alteration_gameplay_generic"
	];

	[Export]
	public Control rarityBar;
	Control[] rarityBarSegments;

	string linkedAlteration;
	int linkedIndex;
	bool isLocked;

	public override void _Ready()
	{
		rarityBarSegments = [.. rarityBar?.GetChildren().AsEnumerable().Cast<Control>().Reverse() ?? []];
	}

	public void SetPerkAlteration(string alterationId, bool hasRarity = false, int index = 0)
	{
		linkedAlteration = alterationId;
		linkedIndex = index;
		if (alterationId is null)
		{
			EmitSignal(SignalName.NameChanged, "Select perk to preview");
			ApplyRarity(true, -1);
			EmitSignal(SignalName.ElementIconVisibilityChanged, false);
			return;
		}
		if (GameItemTemplate.Get(alterationId) is not GameItemTemplate alteration)
		{
			EmitSignal(SignalName.ElementIconVisibilityChanged, false);
			if (alterationId == "")
			{
				EmitSignal(SignalName.NameChanged, "Empty Perk Slot");
				ApplyRarity(false);
				return;
			}
			EmitSignal(SignalName.NameChanged, "Unknown Perk (Probably Legacy)");
			ApplyRarity(true, -1);
			return;
		}

		EmitSignal(SignalName.NameChanged, alteration.DisplayName);

		if (hasRarity)
		{
			int rarity = alteration.RarityLevel;
			if (alterationId.StartsWith("Alteration:aid_g_"))
				rarity = 6;
			ApplyRarity(true, rarity - 1);
			EmitSignal(SignalName.RarityIconChanged, GameItemTemplate.Get(rarityTemplates[rarity - 1]).GetTexture());
			EmitSignal(SignalName.RarityIconVisibilityChanged, true);
		}
		else
			ApplyRarity(false);

		if (alteration.ContainsKey("ImagePaths"))
		{
			EmitSignal(SignalName.ElementIconChanged, alteration.GetTexture());
			EmitSignal(SignalName.ElementIconVisibilityChanged, true);
		}
		else
			EmitSignal(SignalName.ElementIconVisibilityChanged, false);
	}

	void ApplyRarity(bool showRarity, int rarityIdx = -1)
	{
		EmitSignal(SignalName.RarityIconVisibilityChanged, showRarity);
		rarityBar?.Visible = showRarity && rarityIdx >= 0 && rarityIdx < 5;
		if (!showRarity)
			return;
		EmitSignal(SignalName.RarityIconChanged, rarityIdx >= 0 ? GameItemTemplate.Get(rarityTemplates[rarityIdx]).GetTexture() : PegLegResourceManager.defaultIcon);
		rarityBar.Modulate = rarityIdx >= 0 && rarityIdx < 6 ? PaletteHelper.RarityColours[rarityIdx] : Colors.White;
		for (int i = 0; i < rarityBarSegments.Length; i++)
		{
			rarityBarSegments[i].SelfModulate = i < rarityIdx + 1 ? Colors.White : Colors.Black;
		}
	}

	public void SetInteractable(bool newValue)
	{
		EmitSignal(SignalName.InteractableChanged, newValue);
	}

	public void SetLocked(bool newValue)
	{
		isLocked = newValue;
		EmitSignal(SignalName.LockVisibilityChanged, newValue);
	}

	public void SetLockLevel(int level)
	{
		EmitSignal(SignalName.LockTextChanged, "Lv " + level);
	}

	public void SetLockRarity(int rarity)
	{
		EmitSignal(SignalName.LockColorChanged, PaletteHelper.RarityColours[rarity]);
	}

	public void Press()
	{
		EmitSignal(SignalName.Pressed, linkedIndex, linkedAlteration, isLocked);
	}
}
