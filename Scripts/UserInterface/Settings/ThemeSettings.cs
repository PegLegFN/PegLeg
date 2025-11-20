using Godot;
using System;
using System.Reflection;

public partial class ThemeSettings : Control
{
	[Export]
	OptionButton themeOptions;
	[Export]
	ShaderHook backgroundPreview;
    [Export]
    OptionButton backgroundOptions;
    [Export]
    OptionButton musicOptions;
    [Export]
    Button musicPreviewButton;
    [Export]
    ProgressBar musicPreviewProgress;
    [Export]
    Control applyBtnSection;

    [ExportGroup("Textures")]
    [Export]
    Texture2D playIcon;
    [Export]
    Texture2D pauseIcon;

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
        backgroundOptions.ItemSelected += PreviewBG;
        musicOptions.ItemSelected += PreviewMusic;

        musicPreviewButton.Pressed += ToggleMusicPreview;

        VisibilityChanged += async () =>
        {
            await Helpers.WaitForFrame();
            if (IsVisibleInTree())
                UpdateActiveTheme();
            else if (musicPreviewPlayState) 
                ToggleMusicPreview();
        };
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
            int index = Array.IndexOf(ThemeController.ThemeKeys, themeName) + 2;
            themeOptions.Selected = index;
            PreviewTheme(index);
        }
    }

    private void PreviewTheme(long index)
    {
        var themeName = (string)themeOptions.GetItemMetadata(themeOptions.Selected);
        bool isSelected = themeName == ThemeController.selectedThemeName;
        themeName = ThemeController.GetWorkingThemeKey(themeName);
        var theme = ThemeController.GetTheme(themeName);

        backgroundOptions.Clear();
        if(themeOptions.Selected==0)
        {
            backgroundOptions.AddItem("Theme Default");
            backgroundOptions.SetItemMetadata(0, -1);
            backgroundOptions.Selected = 0;
            backgroundOptions.Disabled = true;
        }
        else if (theme.Backgrounds.Length == 1)
        {
            backgroundOptions.AddItem(theme.Backgrounds[0].DisplayName);
            backgroundOptions.SetItemMetadata(0, -1);
            backgroundOptions.Selected = 0;
            backgroundOptions.Disabled = true;
        }
        else
        {
            backgroundOptions.AddItem("Shuffle Backgrounds");
            backgroundOptions.SetItemMetadata(0, -1);
            backgroundOptions.AddSeparator();
            backgroundOptions.Disabled = false;

            int preferredBG = AppConfig.Get("theme", $"{themeName}_bgpref", -1);
            bool any = false;
            int curIdx = 2;
            for (int i = 0; i < theme.Backgrounds.Length; i++)
            {
                backgroundOptions.AddItem(theme.Backgrounds[i].DisplayName);
                backgroundOptions.SetItemMetadata(curIdx, i);
                if (i == preferredBG)
                    backgroundOptions.Selected = curIdx;
                curIdx++;
            }
            if(!any)
                backgroundOptions.Selected = 0;
        }
        PreviewBG(backgroundOptions.Selected);

        musicOptions.Clear();
        if (themeOptions.Selected == 0)
        {
            musicOptions.AddItem("Theme Default");
            musicOptions.SetItemMetadata(0, -1);
            musicOptions.Selected = 0;
            musicOptions.Disabled = true;
        }
        else if (theme.Music.Length == 1)
        {
            musicOptions.AddItem(theme.Music[0].DisplayName);
            musicOptions.SetItemMetadata(0, -1);
            musicOptions.Selected = 0;
            musicOptions.Disabled = true;
        }
        else
        {
            musicOptions.AddItem("Shuffle Music");
            musicOptions.SetItemMetadata(0, -1);
            musicOptions.AddSeparator();
            musicOptions.Disabled = false;

            int preferredMus = AppConfig.Get("theme", $"{themeName}_musicpref", -1);
            bool any = false;
            int curIdx = 2;
            for (int i = 0; i < theme.Music.Length; i++)
            {
                musicOptions.AddItem(theme.Music[i].DisplayName);
                musicOptions.SetItemMetadata(curIdx, i);
                if (i == preferredMus)
                    musicOptions.Selected = curIdx;
                curIdx++;
            }
            if (!any)
                musicOptions.Selected = 0;
        }
        PreviewMusic(musicOptions.Selected);
        if (musicPreviewPlayState)
            ToggleMusicPreview();
        if (isSelected)
            applyBtnSection.Visible = false;
    }

    private void PreviewBG(long index)
    {
        applyBtnSection.Visible = true;
        var themeName = (string)themeOptions.GetItemMetadata(themeOptions.Selected);
        var theme = ThemeController.GetTheme(themeName);
        var bgIdx = (int)backgroundOptions.GetItemMetadata((int)index);
        if (bgIdx < 0 || bgIdx >= theme.Backgrounds.Length)
        {
            backgroundPreview.Texture = theme.PickBackground().File;
            return;
        }
        backgroundPreview.Texture = theme.Backgrounds[bgIdx].File;
    }

    private void PreviewMusic(long index)
    {
        applyBtnSection.Visible = true;

        if (musicPreviewPlayState)
            ToggleMusicPreview();
    }

    public bool musicPreviewPlayState = false;
    private void ToggleMusicPreview()
    {
        if (musicPreviewPlayState)
        {
            musicPreviewPlayState = false;
            musicPreviewButton.Icon = playIcon;
            MusicController.OverridePlaylist(null);
            return;
        }
        var themeName = (string)themeOptions.GetItemMetadata(themeOptions.Selected);
        var theme = ThemeController.GetTheme(themeName);
        var musIdx = (int)musicOptions.GetItemMetadata(musicOptions.Selected);
        var previewPlaylist = theme.PickPlaylist();
        if (musIdx >= 0 && musIdx < theme.Music.Length)
            previewPlaylist = theme.Music[musIdx];
        musicPreviewPlayState = true;
        musicPreviewButton.Icon = pauseIcon;
        GD.Print("Previewing: " + previewPlaylist.DisplayName);
        MusicController.OverridePlaylist(previewPlaylist);
    }

    public void ApplyTheme()
    {
        GD.Print("Applying theme changes");
        var themeName = (string)themeOptions.GetItemMetadata(themeOptions.Selected);
        if (themeOptions.Selected != 0)
        {
            AppConfig.Set("theme", $"{themeName}_bgpref", (int)backgroundOptions.GetItemMetadata(backgroundOptions.Selected));
            AppConfig.Set("theme", $"{themeName}_musicpref", (int)musicOptions.GetItemMetadata(musicOptions.Selected));
        }
        ThemeController.SetActiveTheme(themeName);
    }
}
