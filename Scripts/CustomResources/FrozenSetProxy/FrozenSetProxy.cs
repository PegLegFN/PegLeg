using Godot;
using System.Collections.Frozen;

public abstract partial class FrozenSetProxy<[MustBeVariant] T> : Resource
{
	protected abstract T[] SetContents { get; }
	FrozenSet<T> _fSet;
	public FrozenSet<T> FSet => _fSet ??= (SetContents ?? []).ToFrozenSet();
	public bool Contains(T val) => FSet.Contains(val);
}
