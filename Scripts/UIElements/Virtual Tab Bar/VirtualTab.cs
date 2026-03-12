using Godot;
using System;
using System.Linq;

[Tool]
public partial class VirtualTab : Control
{
    [Signal]
    public delegate void ToggledEventHandler(bool newVal);

    [Export]
    bool ignoreMode;

    int _mode;
    string _text;
    string _tooltip;
    Texture2D _icon;

    [Export(PropertyHint.Enum, "Left:-1,Middle:0,Right:1")]
    public int Mode
    {
        get => _mode;
        set
        {
            _mode=value;
            SetMode(Mode);
        }
    }
    [Export]
    public string Text
    {
        get => _text;
        set
        {
            _text = value;
            if (label is null)
                return;
            label.Text = Text;
            label.Visible = (iconRect?.Visible ?? false) || !string.IsNullOrWhiteSpace(Text);
            if (labelPadding is not null)
                labelPadding.Visible = label.Visible;
        }
    }
    [Export(PropertyHint.MultilineText)]
    public string Tooltip
    {
        get => _tooltip;
        set
        {
            _tooltip = value;
            if (button is not null)
                button.TooltipText = Tooltip;
        }
    }
    [Export]
    public Texture2D Icon
    {
        get => _icon;
        set
        {
            _icon = value;
            if (iconRect is null)
                return;
            iconRect.Visible = Icon is not null;
            iconRect.Texture = Icon;
            if (label is not null)
                label.Visible = !iconRect.Visible || !string.IsNullOrWhiteSpace(Text);
            if (labelPadding is not null)
                labelPadding.Visible = label.Visible;
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
    [Export]
    public bool Disabled
    {
        get => button?.Disabled ?? false;
        set
        {
            if (button is not null)
                button.Disabled = value;
        }
    }
    [Export]
    public Color Tint
    {
        get => iconRect?.SelfModulate ?? Colors.White;
        set
        {
            if (iconRect is not null)
                iconRect.SelfModulate = value;
        }
    }

    [ExportGroup("Nodes")]
    [Export]
    Button button;
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
        SetMode(Mode);
        SetContent(Text, Icon, Tooltip);
    }

    public VirtualTabBar.TabData TabData => new()
    {
        text = label.Text,
        tooltip = button.TooltipText,
        hidden = !Visible,
        disabled = Disabled,
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
        if (button is null || ignoreMode)
            return;
        button.ThemeTypeVariation = mode switch
        {
            -1 => "LeftCheckButton",
            0 => "MiddleCheckButton",
            1 => "RightCheckButton",
            _ => "EmptyCheckButton"
        };
    }

    public void SetFromTabData(VirtualTabBar.TabData tabData)
    {
        SetContent(tabData.text, tabData.icon, tabData.tooltip);
        Visible = !tabData.hidden;
        Disabled = tabData.disabled;
    }


    public void SetContent(string text, Texture2D icon = null, string tooltip = null)
    {
        this.Text = text;
        this.Icon = icon;
        this.Tooltip = tooltip;
    }
}
