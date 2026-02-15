using Godot;
using System;

public partial class TimelineContentContainer : Control
{
    [Export]
    Control stripRoot;
    ScrollContainer parentSC;
	Control firstChild;
	Control secondChild;

    public T GetAncestor<T>() where T : Node
    {
        Node current = this;
        while (current.GetParentOrNull<Node>() is Node parent)
        {
            if (parent is T sc)
            {
                return sc;
            }
            current = parent;
        }
        return null;
    }

	public override async void _Ready()
	{
        ProcessPriority = 1;
        parentSC = GetAncestor<ScrollContainer>();
		if (parentSC is null)
			return;

        stripRoot.ItemRectChanged += FlagForUpdate;
        parentSC.ItemRectChanged += FlagForUpdate;
        parentSC.GetChild<Control>(0).ItemRectChanged += FlagForUpdate;
        //if (GetAncestor<TimelineStripPlacer>() is TimelineStripPlacer placer)
        //    placer.contentContainers.Add(this);

        var childCount = GetChildCount();
		if (childCount > 0)
        {
            firstChild = GetChild<Control>(0);
			firstChild.AnchorBottom = 1;
            firstChild.AnchorTop = 0;
            firstChild.AnchorLeft = 0;
            firstChild.AnchorRight = 0;
        }
        if (childCount > 1)
        {
            secondChild = GetChild<Control>(1);
            secondChild.AnchorBottom = 1;
            secondChild.AnchorTop = 0;
            secondChild.AnchorLeft = 1;
            secondChild.AnchorRight = 1;
        }
        await Helpers.WaitForFrames(3);
		if (childCount > 0)
			UpdatePadding();
    }

    public void FlagForUpdate()
    {
        updateNext = true;
    }

    bool updateNext = false;

    public override void _Process(double delta)
    {
        if (updateNext)
            UpdatePadding();
        updateNext = false;
    }

    public void UpdatePadding()
    {
        if (parentSC is null || !stripRoot.Visible)
            return;

        firstChild.ResetOffsets();
        secondChild?.ResetOffsets();

        float availableSpace = Size.X - (firstChild.Size.X + (secondChild?.Size.X ?? 0));
        availableSpace = Mathf.Max(availableSpace, 1);
		var rect = GetGlobalRect();
		var parentRect = parentSC.GetGlobalRect();

		firstChild.OffsetLeft = Mathf.Clamp((parentRect.Position.X+5) - rect.Position.X, 0, availableSpace);
		if(secondChild is not null)
            secondChild.OffsetRight = -Mathf.Clamp(rect.End.X - (parentRect.End.X-5), 0, availableSpace);
    }
}
