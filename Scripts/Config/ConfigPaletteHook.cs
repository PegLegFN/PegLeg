using Godot;
using System;

[Tool]
public partial class ConfigPaletteHook : Control
{
	[Export]
	string paletteName;
	[Export]
	string displayName
	{
		get => field;
		set
		{
			field = value;
			label?.Text = field;
		}
	}
	[Export]
	int index = -1;
	[ExportGroup("Nodes")]
	[Export]
	ColorPickerButton colourBtn;
	[Export]
	Label label;
	[Export]
	Button defaultButton;
	PopupPanel popup;
	ColorPicker picker;

	Color defaultColor;

	public override void _Ready()
	{
		label.Text = displayName;
		if (Engine.IsEditorHint())
			return;
		popup = colourBtn.GetPopup();
		picker = colourBtn.GetPicker();

		//set defaultColor from palette
	}
}
