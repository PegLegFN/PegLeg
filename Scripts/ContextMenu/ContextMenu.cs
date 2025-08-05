using Godot;
using System;
using System.Collections.Generic;

public partial class ContextMenu : Window
{
    static ContextMenu inst;
    [Export(PropertyHint.Dir)]
    string contextComponentFolder;
    [Export]
    Control componentParent;
    [Export]
    Control listTarget;
    [Export]
    Control listAnimation;
    [Export]
    Control scaleAnimation;
    Dictionary<string, BaseContextComponent> contextComponentDict = [];
    static readonly Vector2[] fullPassthrough = new Vector2[2];

    public override void _Ready()
    {
        inst = this;
#if GODOT_WINDOWS
        Visible = true;
        this.Win64RemoveFromTaskbar();
#else
        MousePassthroughPolygon = [];
#endif
        SetCtxVisible(false);

        var compNames = DirAccess.GetFilesAt(contextComponentFolder);
        for (int i = 0; i < compNames.Length; i++)
        {
            string name = compNames[i];
            if (name.EndsWith(".remap"))
                name = name[..^6];
            var cScene = ResourceLoader.Load<PackedScene>($"{contextComponentFolder}/{name}");
            var cNode = cScene.Instantiate();
            if (cNode is not BaseContextComponent comp)
            {
                cNode.QueueFree();
                continue;
            }
            contextComponentDict.TryAdd(comp.Id, comp);
            comp.menu = this;
        }

        FocusExited += CloseMenu;
    }

    void SetCtxVisible(bool visible)
    {
#if GODOT_WINDOWS
        MousePassthroughPolygon = visible ? [] : fullPassthrough;
#else
        Visible = visible;
#endif
    }

    List<HSeparator> activeSeparators = [];
    List<BaseContextComponent> activeComponents = [];
    Tween animTween;
    bool open;
    bool isOpening = false;

    public static void ShowMenu(ContextMenuHook hook) => inst?.ShowMenuInst(hook);
    async void ShowMenuInst(ContextMenuHook hook)
    {
        scaleAnimation.Scale = Vector2.Zero;
        await Helpers.WaitForFrame();
        SetCtxVisible(false);
        var compList = hook.componentList.components;
        bool hasComps = false;
        for (int i = 0; i < compList.Length; i++)
        {
            string componentId = compList[i];
            if (componentId.StartsWith("d_"))
            {
                if (!AppConfig.Get("advanced", "developer", false))
                    continue;
                componentId = componentId[2..];
            }
            hasComps = true;
            if (componentId == "-")
            {
                HSeparator sep = new();
                componentParent.AddChild(sep);
                activeSeparators.Add(sep);
                continue;
            }
            if (contextComponentDict.TryGetValue(componentId, out var comp) && !activeComponents.Contains(comp))
            {
                componentParent.AddChild(comp);
                activeComponents.Add(comp);
                comp.Update(hook);
            }
        }
        if (!hasComps)
            return;

        if (open)
            Clear();
        open = true;
        isOpening = true;

        if (animTween?.IsRunning() == true)
            animTween.Stop();

        Position = Vector2I.One*-100;
        listTarget.Size = Vector2.Zero;
        await Helpers.WaitForFrame();

        //var targetListSize = listTarget.GetCombinedMinimumSize();
        var targetListSize = listTarget.Size;
        //GD.Print("tar: " + targetListSize);
        listAnimation.CustomMinimumSize = targetListSize;
        scaleAnimation.Size = Vector2.Zero;
        Size = (Vector2I)scaleAnimation.Size;
        await Helpers.WaitForFrame();

        var ds = DisplayServer.Singleton;
        var targetPos = ds.MouseGetPosition();
        var fullSize = Size;
        var oobPush = -fullSize;
        if (hook.attachTo is not null)
        {
            var window = hook.attachTo.GetWindow();
            var hscale = (float)window.ContentScaleSize.X / window.Size.X;
            var vscale = (float)window.ContentScaleSize.Y / window.Size.Y;
            var scale = Mathf.Max(hscale, vscale);
            var rect = hook.attachTo.GetGlobalRect();
            var scaledPos = rect.Position / scale;
            var scaledSize = rect.Size / scale;
            bool horizontal = hook.attachHorizontally;
            targetPos = window.Position + (Vector2I)(scaledPos + scaledSize * (horizontal ? Vector2.Right : Vector2.Down));
            if (horizontal)
            {
                oobPush.Y += (int)scaledSize.Y;
                oobPush.X -= (int)scaledSize.X;
            }
            else
            {
                oobPush.Y -= (int)scaledSize.Y;
                oobPush.X += (int)scaledSize.X;
            }
        }
        var screen = ds.GetScreenFromRect(new(targetPos, Vector2.One));
        //var clamp = new Rect2I(ds.ScreenGetPosition(screen), ds.ScreenGetSize(screen));
        var clamp = ds.ScreenGetUsableRect(screen);
        var clampMax = clamp.Position + (clamp.Size - Size);

        bool flipH = false;
        bool flipV = false;
        if (targetPos.X > clampMax.X)
        {
            flipH = true;
            targetPos.X += oobPush.X;
        }
        if (targetPos.Y > clampMax.Y)
        {
            flipV = true;
            targetPos.Y += oobPush.Y;
        }

        targetPos.X = Mathf.Min(targetPos.X, clampMax.X);
        targetPos.Y = Mathf.Min(targetPos.Y, clampMax.Y);

        targetPos.X = Mathf.Max(targetPos.X, clamp.Position.X);
        targetPos.Y = Mathf.Max(targetPos.Y, clamp.Position.Y);

        listAnimation.CustomMinimumSize = targetListSize * Vector2.Right;
        scaleAnimation.Size = Vector2.Zero;
        //await Helpers.WaitForFrame();
        scaleAnimation.Position = Vector2.Zero;
        if (flipH)
        {
            scaleAnimation.Position += scaleAnimation.Size * Vector2.Right;
        }
        if (flipV)
        {
            scaleAnimation.Position += (fullSize - scaleAnimation.Size) * Vector2.Down;
        }
        Size = (Vector2I)scaleAnimation.Size;

        Position = targetPos;
        SetCtxVisible(true);
        GrabFocus();
        isOpening = false;
        await Helpers.WaitForFrame();
        await Helpers.WaitForFrame();

        animTween = CreateTween().SetParallel().SetTrans(Tween.TransitionType.Sine);
        animTween.TweenProperty(scaleAnimation, "scale", Vector2.One, 0.15f).SetEase(Tween.EaseType.Out);
        if (flipH)
        {
            animTween.TweenProperty(scaleAnimation, "position:x", 0, 0.15f).SetEase(Tween.EaseType.Out);
        }
        if (flipV)
        {
            animTween.TweenProperty(scaleAnimation, "position:y", 0, 0.15f).SetDelay(0.1f);
        }
        animTween.TweenProperty(listAnimation, "custom_minimum_size", targetListSize, 0.15f).SetDelay(0.1f);
        animTween.Finished += () =>
        {
            Size = (Vector2I)scaleAnimation.Size;
        };
    }

    public async void CloseMenu()
    {
        if (!open)
            return;
        await Helpers.WaitForFrame();
        if (isOpening)
            return;
        scaleAnimation.Scale = Vector2.Zero;
        await Helpers.WaitForFrame();
        SetCtxVisible(false);
        open = false;
        Clear();
    }

    void Clear()
    {
        foreach (var sep in activeSeparators)
        {
            componentParent.RemoveChild(sep);
            sep.QueueFree();
        }
        foreach (var comp in activeComponents)
        {
            componentParent.RemoveChild(comp);
            comp.Update(null);
        }
        activeComponents.Clear();
        activeSeparators.Clear();
    }
}
