public abstract partial class AbstractContextComponent : BaseContextComponent
{
	public abstract override string Id { get; }
	public abstract override void Update(ContextMenuHook hook);
}