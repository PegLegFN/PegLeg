using Godot;

public abstract partial class BaseTitleCtx : AbstractContextComponent
{
    [Export]
    ShaderHook bg;
    [Export]
    Label titleLabel;
    [Export]
    Control titleContainer;
    [Export]
    float maxWidth = 300;
    public sealed override void Update(ContextMenuHook hook)
    {
        titleLabel.TextOverrunBehavior = TextServer.OverrunBehavior.NoTrimming;
        //titleContainer.CustomMinimumSize = Vector2.Zero;
        titleLabel.CustomMinimumSize = Vector2.Zero;
        titleLabel.Text = GetTitle(hook);
        if (titleLabel.GetMinimumSize().X >= maxWidth)
        {
            //titleContainer.CustomMinimumSize = new(maxWidth, 0);
            titleLabel.CustomMinimumSize = new(maxWidth, 0);
            titleLabel.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        }
        if(GetTexture(hook) is Texture2D tex)
        {
            bg.Texture = tex;
            bg.SetShaderBool(false, "ColorMode");
        }
        else
        {
            bg.SetShaderColor(GetColor(hook), "Color");
            bg.SetShaderBool(true, "ColorMode");
            bg.SetTimeOffset(Time.GetTicksMsec() - 200);
        }
    }

    protected abstract string GetTitle(ContextMenuHook hook);

    protected virtual Color GetColor(ContextMenuHook hook) => Colors.Transparent;
    protected virtual Texture2D GetTexture(ContextMenuHook hook) => null;
}
