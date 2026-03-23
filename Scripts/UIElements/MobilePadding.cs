using Godot;

public partial class MobilePadding : MarginContainer
{
	[Export]
	Vector4I extraPaddingOnMobile;
	public override void _Ready()
	{
#if GODOT_MOBILE
		OffsetConst("margin_left", extraPaddingOnMobile.X);
        OffsetConst("margin_top", extraPaddingOnMobile.Y);
        OffsetConst("margin_right", extraPaddingOnMobile.Z);
        OffsetConst("margin_bottom", extraPaddingOnMobile.W);
#endif

	}

	void OffsetConst(string key, int amt) =>
		AddThemeConstantOverride(key, GetThemeConstant(key) + amt);
}
