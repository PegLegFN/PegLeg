using Godot;

public partial class CoreSettings : Control
{
    [Signal]
    public delegate void IsMobileEventHandler(bool value);
    [Export]
    string bootScenePath = "res://Scenes/boot_scene.tscn";

    public override void _Ready()
    {
        EmitSignalIsMobile(OS.HasFeature("mobile"));
    }

    public void SetLiteMode(bool newValue)
    {
        //using var _ = LoadingOverlay.CreateToken();
        AppConfig.Set("core", "litemode", newValue);
        //await Helpers.WaitForFrames(10);
        GetTree().ChangeSceneToFile(bootScenePath);
    }

    public void SetMobileMode(bool newValue)
    {
        //using var _ = LoadingOverlay.CreateToken();
        AppConfig.Set("core", "disable_mobile", !newValue);
        //await Helpers.WaitForFrames(10);
        GetTree().ChangeSceneToFile(bootScenePath);
    }
}
