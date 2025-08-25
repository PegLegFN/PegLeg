using Godot;
using System;

public partial class DevTextOverlay : ModalWindow
{
    [Export]
    TextEdit textEdit;
    static DevTextOverlay inst;

    public override void _Ready()
    {
        base._Ready();
        inst = this;
    }

    public static void ShowText(string text)
    {
        if (inst?.IsInsideTree() != true)
            return;
        inst.textEdit.Text = text;
        inst.SetWindowOpen(true);
    }
}
