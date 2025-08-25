using Godot;
using System.Collections.Generic;
using System.Text.Json.Nodes;

public partial class TrayIcon : StatusIndicator
{
    NotificationData? _tutorialNotif;
    NotificationData tutorialNotif => _tutorialNotif ??= new()
    {
        header = "PegLeg minimised to Tray",
        body = "Click the Tray Icon or reopen PegLeg to resume.\nNotifications will still be delivered when minimised.\nRight-click the tray icon to fully close PegLeg.",
        icon = inst?.Icon,
        urgent = true,
        firstAction = "Don't Show Again",
        itemColor = Color.FromHtml("#ffcc00"),
        HandleAction = _ =>
        {
            AppConfig.Set("notification", "on_minimised", false);
            return true;
        }
    };

    bool minimised = false;
    bool hasShownTutorial = false;
    PopupMenu menu;
    [Export]
    Control mainAppRoot;
    [Export]
    Texture2D editorIcon;
    [Export]
    Texture2D testBuildIcon;

    static TrayIcon inst;
    const string mainWindowTag = "MainWindow";
    public override void _Ready()
    {
        inst = this;
        menu = GetNode<PopupMenu>(Menu);
        menu.IdPressed += HandleMenu;
#if GODOT_WINDOWS
        GetWindow().CloseRequested += Minimise;
        GetTree().AutoAcceptQuit = false;
#endif
        Pressed += (_, __) => Unminimise();
        menu.Clear();
        menu.AddItem("Close PegLeg", 404);
        if (OS.HasFeature("test"))
            Icon = testBuildIcon;
        hasShownTutorial = !AppConfig.Get("notification", "on_minimised", true);
        if (OS.HasFeature("editor"))
        {
            Visible = false;
            Icon = editorIcon;
        }
        AppConfig.OnConfigChanged += OnConfigChanged;
        runtimeFPS = (int)AppConfig.Get("ui", "fps", 60.0);
        RegisterResponsiveWindow(mainWindowTag);
    }
    static int runtimeFPS = 60;

    private void OnConfigChanged(string section, string key, JsonValue value)
    {
        if (section == "notification" && key == "on_minimised" && value.TryGetValue(out bool showTutoriel))
            hasShownTutorial = !showTutoriel;
        if (section == "ui" && key == "fps" && value.TryGetValue(out float fps))
        {
            runtimeFPS = (int)fps;
            if (responsiveWindowList.Count > 0)
            {
                Engine.MaxFps = Mathf.Max(0, runtimeFPS);
            }
        }
    }

    public void HandleMenu(long id)
    {
        var index = menu.GetItemIndex((int)id);
        switch (id)
        {
            case 404:
                GetTree().Quit();
                break;
        }
    }

    static List<string> responsiveWindowList = [];
    public static void RegisterResponsiveWindow(string windowTag)
    {
        if (responsiveWindowList.Count == 0)
        {
            Engine.MaxFps = Mathf.Max(0, runtimeFPS);
        }
        if(!responsiveWindowList.Contains(windowTag))
            responsiveWindowList.Add(windowTag);
    }

    public static void UnregisterResponsiveWindow(string windowTag)
    {
        responsiveWindowList.Remove(windowTag);
        if (responsiveWindowList.Count == 0)
        {
            Engine.MaxFps = 10;
        }
    }

    void Minimise()
    {
        if (!minimised)
        {
            minimised = true;
            GetWindow().Win64SetVisible(false);
            UnregisterResponsiveWindow(mainWindowTag);
            if (mainAppRoot is not null)
            {
                mainAppRoot.Visible = false;
                mainAppRoot.ProcessMode = ProcessModeEnum.Disabled;
            }
            if (!hasShownTutorial)
            {
                NotificationManager.Push([tutorialNotif]);
                hasShownTutorial = true;
            }
            Visible = true;
        }
        
    }

    public static void UnminimiseDeferred()
    {
        if (!inst.IsInsideTree())
        {
            inst = null;
            return;
        }
        inst.CallDeferred(nameof(inst.Unminimise));
    }

    static Vector2I maximisedOffset = new(0, 23);
    public void Unminimise()
    {
        var window = GetWindow();
        if (minimised)
        {
            minimised = false;
            window.Win64SetVisible(true);
            RegisterResponsiveWindow(mainWindowTag);
            if (mainAppRoot is not null)
            {
                mainAppRoot.ProcessMode = ProcessModeEnum.Inherit;
                mainAppRoot.Visible = true;
            }
            if (window.Mode == Window.ModeEnum.Minimized)
            {
                var safeRect = DisplayServer.ScreenGetUsableRect(window.CurrentScreen);
                GD.Print(safeRect);
                GD.Print(new Rect2I(window.Position- maximisedOffset, window.Size+ maximisedOffset));
                if (safeRect.Position == window.Position - maximisedOffset && safeRect.Size == window.Size + maximisedOffset)
                    window.Mode = Window.ModeEnum.Maximized;
                else
                    window.Mode = Window.ModeEnum.Windowed;
            }
            var mousePos = DisplayServer.MouseGetPosition();
            var mouseScreen = DisplayServer.GetScreenFromRect(new(mousePos, new(1, 1)));
            if (window.CurrentScreen != mouseScreen)
            {
                window.CurrentScreen = mouseScreen;
                window.MoveToCenter();
            }
            if (OS.HasFeature("editor"))
                Visible = false;
        }
        window.GrabFocus();
    }

    public override void _ExitTree()
    {
        if (minimised)
            Unminimise();
        GetWindow().CloseRequested -= Minimise;
        GetTree().AutoAcceptQuit = true;
        menu.IdPressed -= HandleMenu;
        Visible = false;
    }
}
