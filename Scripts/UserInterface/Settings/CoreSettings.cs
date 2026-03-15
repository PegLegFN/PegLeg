using Godot;

public partial class CoreSettings : Control
{
    [Signal]
    public delegate void IsMobileEventHandler(bool value);
    [Export]
    string bootScenePath = "res://Scenes/boot_scene.tscn";
    [ExportGroup("Text")]
    [ExportSubgroup("Lite")]
    [Export]
    string liteActiveText;
    [Export]
    string liteInactiveText;
    [Export]
    string liteActiveButtonText;
    [Export]
    string liteInactiveButtonText;
    [ExportSubgroup("Mobile")]
    [Export]
    string mobileActiveText;
    [Export]
    string mobileInactiveText;
    [Export]
    string mobileActiveButtonText;
    [Export]
    string mobileInactiveButtonText;
    [ExportGroup("Nodes")]
    [Export]
    Label liteLabel;
    [Export]
    Button liteButton;
    [Export]
    Label mobileLabel;
    [Export]
    Button mobileButton;

    public override void _Ready()
    {
        EmitSignalIsMobile(OS.HasFeature("editor") || OS.HasFeature("mobile"));

        bool liteActive = AppConfig.Get("core", "litemode", false);
        liteLabel.Text = liteActive ? liteActiveText : liteInactiveText;
        liteButton.Text = liteActive ? liteActiveButtonText : liteInactiveButtonText;

        bool mobileActive = !AppConfig.Get("core", "disable_mobile", true);
        mobileLabel.Text = mobileActive ? mobileActiveText : mobileInactiveText;
        mobileButton.Text = mobileActive ? mobileActiveButtonText : mobileInactiveButtonText;
    }

    public void ToggleLiteMode()
    {
        //using var _ = LoadingOverlay.CreateToken();
        AppConfig.Set("core", "litemode", !AppConfig.Get("core", "litemode", false));
        //await Helpers.WaitForFrames(10);
        GetTree().ChangeSceneToFile(bootScenePath);
    }

    public void ToggleMobileMode()
    {
        //using var _ = LoadingOverlay.CreateToken();
        AppConfig.Set("core", "disable_mobile", !AppConfig.Get("core", "disable_mobile", true));
        //await Helpers.WaitForFrames(10);
        GetTree().ChangeSceneToFile(bootScenePath);
    }
}
