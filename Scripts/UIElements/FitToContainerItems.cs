using Godot;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

[Tool]
public partial class FitToContainerItems : Control
{
	[Export]
	Container target;
	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		if (target is null)
			return;
        target.SortChildren += Fit;
        target.ItemRectChanged += Fit;
	}

    public override void _Process(double delta)
    {
		if (!Engine.IsEditorHint() || target is null)
			return;
		//Fit();
    }

    void Fit()
	{
		var childRects = target.GetChildren().Select(n=>(Control)n).Where(c=>c is not null).Select(c=>c.GetGlobalRect());
		var minX = childRects.Select(r => r.Position.X).Min();
        var minY = childRects.Select(r => r.Position.Y).Min();
        var maxX = childRects.Select(r => r.End.X).Max();
        var maxY = childRects.Select(r => r.End.Y).Max();
		GlobalPosition = new(minX, minY);
		Size = new(maxX - minX, maxY - minY);
    }

    public override void _ExitTree()
    {
        if (target is null)
            return;
        target.SortChildren -= Fit;
        target.ItemRectChanged -= Fit;
    }
}
