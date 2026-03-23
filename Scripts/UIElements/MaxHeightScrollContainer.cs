using Godot;
using System;

public partial class MaxHeightScrollContainer : ScrollContainer
{
    [Export]
    Vector2 maxSize;

    Control child;
    public override void _Ready()
    {
        child = GetChild(0) as Control;
        child.ItemRectChanged += ChildRectChanged;
    }

    private void ChildRectChanged()
    {
        var childSize = child.GetCombinedMinimumSize();
        var _maxSize = maxSize;

        if (_maxSize.X == 0)
            _maxSize.X = childSize.X;
        if (_maxSize.Y == 0)
            _maxSize.Y = childSize.Y;

        //GD.Print(_maxSize);

        CustomMinimumSize = new Vector2(Mathf.Min(childSize.X, _maxSize.X), Mathf.Min(childSize.Y, _maxSize.Y));
    }
}
