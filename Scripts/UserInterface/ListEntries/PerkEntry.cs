using Godot;
using System;

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

    string linkedAlteration;
    int linkedIndex;
    bool isLocked;

    public void SetPerkAlteration(string alterationId, bool hasRarity = false, int index = 0)
    {
        linkedAlteration = alterationId;
        linkedIndex = index;
        if (alterationId is not null)
        {
            if (GameItemTemplate.Get(alterationId) is GameItemTemplate alteration)
            {
                EmitSignal(SignalName.NameChanged, alteration.DisplayName);

                if (hasRarity)
                {
                    int rarity = alteration.RarityLevel;
                    if (alterationId.StartsWith("Alteration:aid_g_"))
                        rarity = 6;
                    EmitSignal(SignalName.RarityIconChanged, GameItemTemplate.Get(rarityTemplates[rarity-1]).GetTexture());
                    EmitSignal(SignalName.RarityIconVisibilityChanged, true);
                }
                else
                    EmitSignal(SignalName.RarityIconVisibilityChanged, false);

                if (alteration.ContainsKey("ImagePaths"))
                {
                    EmitSignal(SignalName.ElementIconChanged, alteration.GetTexture());
                    EmitSignal(SignalName.ElementIconVisibilityChanged, true);
                }
                else
                    EmitSignal(SignalName.ElementIconVisibilityChanged, false);
            }
            else if(alterationId == "")
            {
                EmitSignal(SignalName.NameChanged, "Empty Perk Slot");
                EmitSignal(SignalName.RarityIconVisibilityChanged, false);
                EmitSignal(SignalName.ElementIconVisibilityChanged, false);
            }
            else
            {
                EmitSignal(SignalName.NameChanged, "Unknown Perk (Probably Legacy)");
                EmitSignal(SignalName.RarityIconVisibilityChanged, true);
                EmitSignal(SignalName.RarityIconChanged, PegLegResourceManager.defaultIcon);
                EmitSignal(SignalName.ElementIconVisibilityChanged, false);
            }

        }
        else
        {
            EmitSignal(SignalName.NameChanged, "Select perk to preview");
            EmitSignal(SignalName.RarityIconVisibilityChanged, true);
            EmitSignal(SignalName.RarityIconChanged, PegLegResourceManager.defaultIcon);
            EmitSignal(SignalName.ElementIconVisibilityChanged, false);
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
        EmitSignal(SignalName.LockColorChanged, GameItemTemplate.rarityColours[rarity]);
    }

    public void Press()
    {
        EmitSignal(SignalName.Pressed, linkedIndex, linkedAlteration, isLocked);
    }
}
