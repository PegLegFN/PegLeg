using Godot;

public partial class CurtainOpener : Node
{
	[Export]
	ShaderHook curtain;
	float curtainOpenDuration = 0.15f;
	public override async void _Ready()
	{
		curtain.Visible = true;

		await Helpers.WaitForFrames(10);
		await Helpers.WaitForTimer(0.1f);

		var tween = GetTree().CreateTween().SetParallel();
		tween.TweenProperty(curtain, "SH_Progress", 1, curtainOpenDuration);
		tween.Finished += () =>
		{
			curtain.MouseFilter = Control.MouseFilterEnum.Ignore;
			curtain.Visible = false;
		};
	}
}
