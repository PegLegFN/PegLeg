using Godot;
using System;

public partial class ThemeSettings : Control
{
	[Export]
	OptionButton themeOptions;
	[Export]
	ShaderHook backgroundPreview;

    public override void _Ready()
    {
		var themeList = ThemeController.ThemeKeys;
        themeOptions.Clear();
        themeOptions.AddItem("Default (Current Seasonal Zone)");
        themeOptions.SetItemMetadata(0, "");
        themeOptions.AddSeparator("Themes");

        themeOptions.AddItem("Blank");
        themeOptions.SetItemMetadata(2, "builtin_blank");

        int curIdx = 3;
        for (int i = 0; i < themeList.Length; i++)
        {
            if (!ThemeController.HasTheme(themeList[i]) || themeList[i] == "builtin_blank")
                continue;
            themeOptions.AddItem(ThemeController.GetTheme(themeList[i]).displayName);
            themeOptions.SetItemMetadata(curIdx, themeList[i]);
            curIdx++;
        }

        themeOptions.ItemSelected += PreviewTheme;
    }

    public void UpdateActiveTheme()
    {
        var themeName = ThemeController.selectedThemeName;
        if (themeName == "")
        {
            themeOptions.Selected = 0;
            PreviewTheme(0);
        }
        else
        {
            int index = Array.IndexOf(ThemeController.ThemeKeys, themeName) +2;
            themeOptions.Selected = index;
            PreviewTheme(index);
        }
    }

    private void PreviewTheme(long index)
    {
        var themeName = (string)themeOptions.GetItemMetadata((int)index);
        backgroundPreview.Texture = ThemeController.GetTheme(themeName).PickBackground().File;
    }

    public void ApplyTheme()
    {
        if (themeOptions.Selected == -1)
            return;
        var themeName = (string)themeOptions.GetItemMetadata(themeOptions.Selected);
        GD.Print("Applying: " + themeName);
        ThemeController.SetActiveTheme(themeName);
    }
}
