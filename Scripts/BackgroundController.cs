using Godot;
using System;

public partial class BackgroundController : Node
{
    [Export]
    Texture2D defaultBGTex;
    [Export]
    TextureRect bgMain;
    [Export]
    TextureRect bgOverlay;
    [Export]
    float fadeTime = 0.5f;

    public override void _Ready()
    {
        ThemeController.OnThemeChanged += OnThemeUpdated;
        currentBG = ThemeController.activeTheme?.PickBackground()?.File ?? defaultBGTex;
        bgMain.Texture = currentBG;
        bgOverlay.Modulate = Colors.Transparent;
    }

    Texture2D currentBG;
    Tween currentTransition;

    private void OnThemeUpdated()
    {
        bgOverlay.Texture = currentBG;
        currentBG = ThemeController.activeTheme?.PickBackground()?.File ?? defaultBGTex;
        bgMain.Texture = currentBG;
        bgOverlay.Modulate = Colors.White;

        if (currentTransition?.IsValid() ?? false)
            currentTransition.Kill();
        currentTransition = GetTree().CreateTween();
        currentTransition.Parallel().TweenProperty(bgOverlay, "modulate", Colors.Transparent, fadeTime);
    }

    public override void _ExitTree()
    {
        ThemeController.OnThemeChanged -= OnThemeUpdated;
    }
}
