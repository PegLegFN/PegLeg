using Godot;
using System;

public partial class ShareImagePopup : ModalWindow
{
    [Export]
    TextureRect targetRect;
    ImageTexture imageTex;
    Image currentImage;
    static ShareImagePopup inst;

    public override void _Ready()
    {
        base._Ready();
        imageTex = new();
        targetRect.Texture = imageTex;
        inst = this;
    }

    public static void ShowImage(Image newImage)
    {
        if (inst?.IsInsideTree() != true)
            return;
        inst.imageTex.SetImage(newImage);
        inst.currentImage = newImage;
        inst.SetWindowOpen(true);
    }

    public void CopyImage()
    {
#if !GODOT_WINDOWS
        GD.PushWarning("Can't share images on non-windows platforms");
        return;
#endif
        Win64Helpers.ClipboardSetImage(currentImage);
        SetWindowOpen(false);
    }
}
