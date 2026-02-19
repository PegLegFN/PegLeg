using Godot;
using System;
using System.Linq;

[Tool]
public partial class VirtualTab : Control
{
    [Signal]
    public delegate void ToggledEventHandler(bool newVal);

    int _mode;
    string _text;
    string _tooltip;
    Texture2D _icon;

    [Export(PropertyHint.Enum, "Left:-1,Middle:0,Right:1")]
    int mode
    {
        get => _mode;
        set
        {
            _mode=value;
            SetMode(mode);
        }
    }
    [Export]
    string text
    {
        get => _text;
        set
        {
            _text = value;
            SetContent(text, icon, tooltip);
        }
    }
    [Export(PropertyHint.MultilineText)]
    string tooltip
    {
        get => _tooltip;
        set
        {
            _tooltip = value;
            SetContent(text, icon, tooltip);
        }
    }
    [Export]
    Texture2D icon
    {
        get => _icon;
        set
        {
            _icon = value;
            SetContent(text, icon, tooltip);
        }
    }
    [Export]
    public bool IsPressed
    {
        get => button?.ButtonPressed ?? false;
        set
        {
            if (button is not null)
                button.ButtonPressed = value;
        }
    }

    [ExportGroup("Nodes")]
    [Export]
    CheckButton button;
    [Export]
    Label label;
    [Export]
    Control labelPadding;
    [Export]
    TextureRect iconRect;

    public bool pooled;

    public override void _Ready()
    {
        button ??= (CheckButton)FindChildren("*", "CheckButton", true).FirstOrDefault();
        label ??= (Label)FindChildren("*", "Label", true).FirstOrDefault();
        iconRect ??= (TextureRect)FindChildren("*", "TextureRect", true).FirstOrDefault();
        if (button is not null || Engine.IsEditorHint())
            button.Toggled += TryPressTab;
        SetMode(mode);
        SetContent(text, icon, tooltip);
    }


    public VirtualTabBar.TabData TabData => new()
    {
        text = label.Text,
        tooltip = button.TooltipText,
        hidden = !Visible,
        icon = iconRect.Texture
    };

    VirtualTabBar tabBar = null;
    public void SetTabBar(VirtualTabBar tabBar)
    {
        this.tabBar = tabBar;
    }

    void TryPressTab(bool newVal)
    {
        tabBar?.PressTab(this, newVal);
        EmitSignalToggled(newVal);
    }

    public void SetMode(int mode)
    {
        if (button is null)
            return;
        button.ThemeTypeVariation = mode switch
        {
            -1 => "LeftCheckButton",
            0 => "MiddleCheckButton",
            1 => "RightCheckButton",
            _ => "EmptyCheckButton"
        };
    }

    public void SetContent(string text, Texture2D icon = null, string tooltip = null)
    {
        if (button is null || label is null || iconRect is null)
            return;
        label.Text = text;
        iconRect.Visible = icon is not null;
        iconRect.Texture = icon;
        label.Visible = !iconRect.Visible || !string.IsNullOrWhiteSpace(text);
        if (labelPadding is not null)
            labelPadding.Visible = label.Visible;
        button.TooltipText = tooltip;
    }
}
