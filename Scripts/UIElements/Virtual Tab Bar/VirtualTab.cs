using Godot;
using System.Linq;

[Tool]
public partial class VirtualTab : Control
{
	[Signal]
	public delegate void ToggledEventHandler(bool newVal);

	[Export]
	bool ignoreMode;

	[Export(PropertyHint.Enum, "Left:-1,Middle:0,Right:1")]
	public int Mode
	{
		get => field;
		set => SetMode(field = value);
	}
	[Export]
	public string Text
	{
		get => field;
		set
		{
			field = value;
			if (label is null)
				return;
			label.Text = Text;
			label.Visible = !string.IsNullOrWhiteSpace(Text);
			labelPadding?.Visible = (iconRect?.Visible ?? false) && label.Visible;
		}
	}
	[Export(PropertyHint.MultilineText)]
	public string Tooltip
	{
		get => field;
		set
		{
			field = value;
			button?.TooltipText = value;
		}
	}
	[Export]
	public string Metadata { get; set; }
	[Export]
	public Texture2D Icon
	{
		get => field;
		set
		{
			field = value;
			if (iconRect is null)
				return;
			iconRect.Visible = value is not null;
			iconRect.Texture = value;
			labelPadding?.Visible = (iconRect?.Visible ?? false) && label.Visible;
		}
	}
	[Export(PropertyHint.Range,"1,1.5")]
	public float IconScale
	{
		get => field;
		set
		{
			field = value;
			iconRect?.OffsetTransformScale = value * Vector2.One;
		}
	} = 1.0f;
	[Export]
	public bool IsPressed
	{
		get => button?.ButtonPressed ?? false;
		set => button?.ButtonPressed = value;
	}
	[Export]
	public bool Disabled
	{
		get => button?.Disabled ?? false;
		set => button?.Disabled = value;
	}
	[Export]
	public Color Tint
	{
		get => field;
		set
		{
			field = value;
			iconRect?.SelfModulate = value;
		}
	} = Colors.White;

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
		iconRect?.OffsetTransformScale = IconScale * Vector2.One;
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
		Text = text;
		Icon = icon;
		Tooltip = tooltip;
	}
}
