using Godot;
using System;

public abstract partial class BaseContextComponent : Node
{
    [Export]
    Control disabledPanel;
    public ContextMenu menu { protected get; set; }
    protected void SetDisabled(bool val)
    {
        if (disabledPanel is not null)
            disabledPanel.Visible = val;
    }
    public abstract string Id { get; }
    public abstract void Update(ContextMenuHook hook);
}