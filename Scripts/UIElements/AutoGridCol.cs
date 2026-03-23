using Godot;

[Tool]
public partial class AutoGridCol : GridContainer
{
	[Export]
	float colWidth = 100;

	Control Parent => GetParent() as Control;

	public override void _Ready()
	{
		Parent.ItemRectChanged += RectChanged;
		RectChanged();
	}

	public override Vector2 _GetMinimumSize() => new(colWidth, 0);

	private void RectChanged()
	{
		int spacing = GetThemeConstant("h_separation");
		float availableSpace = Parent.GetRect().Size.X;
		Columns = Mathf.FloorToInt((availableSpace + spacing) / (colWidth + spacing));
	}
}
