using Godot;
using System;

public partial class BaseContextComponent : Node
{
    [Export]
    Control disabledPanel;
    public ContextMenu menu { protected get; set; }
    protected void SetDisabled(bool val)
    {
        if (disabledPanel is not null)
            disabledPanel.Visible = val;
    }
    public virtual string Id { get; }
    public virtual void Update(ContextMenuHook hook) { }
}