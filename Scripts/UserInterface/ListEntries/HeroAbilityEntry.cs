using Godot;

public partial class HeroAbilityEntry : Node
{
	[Signal]
	public delegate void NameChangedEventHandler(string name);

	[Signal]
	public delegate void DescriptionChangedEventHandler(string description);

	[Signal]
	public delegate void NameAndDescriptionChangedEventHandler(string nameAndDescription);

	[Signal]
	public delegate void TooltipChangedEventHandler(string tooltip);

	[Signal]
	public delegate void IconChangedEventHandler(Texture2D name);

	[Signal]
	public delegate void LockChangedEventHandler(string lockText);

	[Signal]
	public delegate void WarningChangedEventHandler(string warningText);

	[Signal]
	public delegate void LockVisibleEventHandler(bool showLocked);

	[Signal]
	public delegate void WarningVisibleEventHandler(bool showWarning);

	[Export]
	string lockText;

	[Export]
	bool defaultClearIconToNull;

	public void SetAbility(GameItemTemplate heroAbility, bool locked = false, string warning = null)
	{
		string name = heroAbility?.DisplayName;
		string description = heroAbility?.Description;

		EmitSignal(SignalName.NameChanged, name);
		EmitSignal(SignalName.DescriptionChanged, description);
		EmitSignal(SignalName.NameAndDescriptionChanged, name + "\n" + description);
		EmitSignal(SignalName.TooltipChanged, CustomTooltip.GenerateSimpleTooltip(name, null, [description]));

		EmitSignal(SignalName.IconChanged, heroAbility?.GetTexture());

		EmitSignal(SignalName.WarningVisible, warning is not null);
		EmitSignal(SignalName.WarningChanged, warning ?? "");

		EmitSignal(SignalName.LockVisible, locked);
		EmitSignal(SignalName.LockChanged, locked ? lockText : "");
	}

	public void ClearAbility() => ClearAbility(defaultClearIconToNull ? null : PegLegResourceManager.defaultIcon);
	public void ClearAbility(Texture2D clearIcon)
	{
		EmitSignal(SignalName.NameChanged, "");
		EmitSignal(SignalName.DescriptionChanged, "");
		EmitSignal(SignalName.NameAndDescriptionChanged, "");
		EmitSignal(SignalName.TooltipChanged, "");

		EmitSignal(SignalName.IconChanged, clearIcon);
		EmitSignal(SignalName.WarningVisible, false);
		EmitSignal(SignalName.LockVisible, false);
	}
}
