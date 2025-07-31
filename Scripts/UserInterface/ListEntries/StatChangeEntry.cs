using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class StatChangeEntry : Control
{
	[Export]
	PackedScene statChangeScene;
	[Export]
	Control statChangeParent;
	[Export]
	GameItemEntry currentItem;
	[Export]
	Button[] rarityToggles;
	[Export]
	Button[] tierToggles;
    [Export]
    Button crystalToggle;
    List<Control> statChanges;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
        for (int i = 0; i < tierToggles.Length; i++)
        {
			int idx = i;
			tierToggles[i].Pressed += () => SwitchTier(idx);
        }
		ButtonGroup tierBG = new() { AllowUnpress = false };
        for (int i = 0; i < rarityToggles.Length; i++)
        {
			rarityToggles[i].ButtonGroup = tierBG;
			rarityToggles[i].Pressed += SwitchRarity;
        }
		crystalToggle.Toggled += SwitchCrystal;
    }

	bool itemDirty = false;
    public override void _Process(double delta)
    {
		if (itemDirty)
        {
            UpdateChanges();
			itemDirty = false;
        }
    }

    bool isCrystal = false;
	void SwitchCrystal(bool newVal)
	{
		newVal &= currentTier >= 3;
        if (newVal!=isCrystal)
			itemDirty = true;
        isCrystal = crystalToggle.ButtonPressed = newVal;
	}

	int currentTier = 4;
	void SwitchTier(int newTier)
	{
		newTier = Mathf.Min(newTier, currentRarity + 1);
		if (newTier != currentTier)
            itemDirty = true;
        currentTier = newTier;
        for (int i = 0; i < tierToggles.Length; i++)
        {
			tierToggles[i].ButtonPressed = currentTier >= i;
        }
		if (currentTier < 3 && isCrystal)
			SwitchCrystal(false);
    }

    int currentRarity = 4;
    void SwitchRarity()
	{
        int newRarity = Array.IndexOf(rarityToggles, rarityToggles.FirstOrDefault(t => t.ButtonPressed));
        if (newRarity!=currentRarity)
            itemDirty = true;
        if (currentTier > currentRarity + 1)
			SwitchTier(currentRarity + 1);
	}

	string baseItem;
	Dictionary<string, Dictionary<string, StatChange>> changes;


    public void SetStatChanges(Dictionary<string, Dictionary<string, StatChange>> changes, string baseItem)
	{
		this.baseItem = baseItem;
		this.changes = changes;
		var keys = changes.Keys.ToArray();
		rarityToggles[0].Disabled = !keys.Any(k => k.Contains("_C_", StringComparison.OrdinalIgnoreCase));
        rarityToggles[1].Disabled = !keys.Any(k => k.Contains("_UC_", StringComparison.OrdinalIgnoreCase));
        rarityToggles[2].Disabled = !keys.Any(k => k.Contains("_R_", StringComparison.OrdinalIgnoreCase));
        rarityToggles[3].Disabled = !keys.Any(k => k.Contains("_VR_", StringComparison.OrdinalIgnoreCase));
        rarityToggles[4].Disabled = !keys.Any(k => k.Contains("_SR_", StringComparison.OrdinalIgnoreCase));
        for (int i = rarityToggles.Length-1; i >= 0; i--)
        {
			if (!rarityToggles[i].Disabled)
			{
				rarityToggles[i].ButtonPressed = true;
				SwitchRarity();
				SwitchTier(Mathf.Max(i, 0));
                break;
			}	
        }


		UpdateChanges();
	}

	void UpdateChanges()
	{
		string tierReplacement = currentTier switch
		{
			0 => "_T01",
			1 => "_T02",
			2 => "_T03",
			3 => "_T04",
			4 => "_T05",
			_ => "_T00",
		};
        string coreReplacement = isCrystal ? "_Crystal" : "_Ore";
        string rarityReplacement = currentRarity switch
        {
            0 => "_C",
            1 => "_UC",
            2 => "_R",
            3 => "_VR",
            4 => "_SR",
            _ => "_C",
        };
		string finalItem = baseItem
			.Replace("{r}", rarityReplacement)
			.Replace("{c}", coreReplacement)
			.Replace("{t}", tierReplacement);
		currentItem.SetItem(GameItemTemplate.Get(finalItem)?.CreateInstance());

		//load stats
    }
}
