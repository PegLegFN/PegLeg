using Godot;
using System;

public partial class HeroEntry : GameItemEntry
{
    [Export(PropertyHint.ArrayType)]
    HeroAbilityEntry[] heroAbilityEntries;

    [Export]
    HeroAbilityEntry heroPerkEntry;

    [Export]
    HeroAbilityEntry heroCommanderPerkEntry;

    protected override void UpdateItem(GameItem item)
    {
        base.UpdateItem(item);
        if (item?.template?.GetHeroAbilities() is GameItemTemplate[] abilityTemplates)
        {
            heroPerkEntry?.SetAbility(abilityTemplates[0], false);
            heroCommanderPerkEntry?.SetAbility(item.template.Tier < 2 ? abilityTemplates[0] : abilityTemplates[1]);
            for (int i = 0; i < 3; i++)
            {
                if (heroAbilityEntries.Length <= i)
                    break;
                heroAbilityEntries[i].SetAbility(abilityTemplates[i + 2], item.template.Tier <= i);
            }
        }
    }
}
