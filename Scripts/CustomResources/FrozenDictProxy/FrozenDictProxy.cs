using Godot;
using Godot.Collections;
using System.Collections.Frozen;

public abstract partial class FrozenDictProxy<[MustBeVariant] Tk, [MustBeVariant] T> : Resource
{
    protected abstract Dictionary<Tk, T> DictContents { get; }
    FrozenDictionary<Tk, T> _fDict;
    public FrozenDictionary<Tk, T> FDict => _fDict ??= (DictContents ?? []).ToFrozenDictionary();
}