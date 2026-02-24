using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

[Tool]
public partial class ContextMenu : Window
{
    static ContextMenu inst;
    [Export(PropertyHint.Dir)]
    string contextComponentFolder;
    [Export]
    PackedScene[] componentScenes;
    [Export]
    Control componentParent;
    [Export]
    Control listTarget;
    [Export]
    Control listAnimation;
    [Export]
    Control scaleAnimation;
    Dictionary<string, AbstractContextComponent> contextComponentDict = [];
    static readonly Vector2[] fullPassthrough = [new(),new()];
    static readonly Vector2[] noPassthrough = [];

#if TOOLS
    [ExportToolButton("Update Components")]
    Callable SceneRefreshBtn => Callable.From(LoadPackedScenes);
    void LoadPackedScenes()
    {
        var compNames = DirAccess.GetFilesAt(contextComponentFolder);
        componentScenes = new PackedScene[compNames.Length];
        for (int i = 0; i < compNames.Length; i++)
        {
            string name = compNames[i];
            if (name.EndsWith(".remap"))
                name = name[..^6];
            componentScenes[i] = ResourceLoader.Load<PackedScene>($"{contextComponentFolder}/{name}");
        }
    }
#endif

    public override async void _Ready()
    {
        if (Engine.IsEditorHint())
        {
            Visible = false;
            return;
        }
        inst = this;
        Visible = true;
        SetCtxVisible(false);
        await Helpers.WaitForFrame();
        this.Win64RemoveFromTaskbar();

        componentScenes = [.. componentScenes.Distinct()];
        //GD.Print($"Components: " + componentScenes.Length);
        for (int i = 0; i < componentScenes.Length; i++)
        {
            var cScene = componentScenes[i];
            var cNode = cScene.Instantiate();
            if (cNode is not AbstractContextComponent comp)
            {
                cNode.QueueFree();
                //GD.Print("Bad Component");
                continue;
            }
            contextComponentDict.TryAdd(comp.Id, comp);
            comp.menu = this;
        }
        //GD.Print($"Components: " + contextComponentDict.Count);

        FocusExited += CloseMenu;
    }

    public void SetCtxVisible(bool visible)
    {
        if (OS.HasFeature("mobile"))
        {
            Visible = visible;
        }
        else
        {
            MousePassthroughPolygon = visible ? noPassthrough : fullPassthrough;
        }
    }

    List<HSeparator> activeSeparators = [];
    List<AbstractContextComponent> activeComponents = [];
    Tween animTween;
    bool open;
    bool blockClosing = false;
    ulong openedAt = 0;

    public static void ShowMenu(ContextMenuHook hook) => inst?.ShowMenuInst(hook);
    async void ShowMenuInst(ContextMenuHook hook)
    {
        scaleAnimation.Scale = Vector2.Zero;
        await Helpers.WaitForFrame();
        SetCtxVisible(false);
        var compList = hook.componentList.components;
        bool hasComps = false;
        openedAt = Time.GetTicksMsec();

        if (open)
            Clear();

        //GD.Print($"Listed Components: {string.Join(", ", compList)}");
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
            else
            {
                GD.Print($"Component not found: {componentId}");
            }
        }
        if (!hasComps)
            return;

        open = true;
        blockClosing = true;

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
        else
        {
            if (OS.HasFeature("mobile"))
            {
                var window = GetTree().Root;
                GD.Print("winSize: " + window.Size);
                GD.Print("contentSize: " + window.ContentScaleSize);
                var hscale = (float)window.ContentScaleSize.X / window.Size.X;
                var vscale = (float)window.ContentScaleSize.Y / window.Size.Y;
                var scale = Mathf.Max(hscale, vscale);
                GD.Print("fromPos: " + targetPos);
                GD.Print("scale: " + scale);
                targetPos = (Vector2I)((Vector2)targetPos * scale);
                GD.Print("toPos: " + targetPos);
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
        EnableMenuClosureAfter(0.1f);
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

    async void EnableMenuClosureAfter(float time)
    {
        await Helpers.WaitForTimer(time);
        blockClosing = false;
    }

    public async void CloseMenu()
    {
        if (!open)
            return;
        await Helpers.WaitForFrame();
        if (blockClosing)
        {
            GrabFocus();
            return;
        }
        GetTree().Root.GrabFocus();
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
