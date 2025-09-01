using Godot;
using Godot.Collections;
using System.Collections.Frozen;

public abstract partial class FrozenDictProxy<[MustBeVariant] Tk, [MustBeVariant] T> : Resource
{
    protected abstract Dictionary<Tk, T> DictContents { get; }
    FrozenDictionary<Tk, T> _fDict;
    public FrozenDictionary<Tk, T> FDict => _fDict ??= (DictContents ?? []).ToFrozenDictionary();
    public bool TryGetValue(Tk key, out T val)
    {
        if (FDict?.TryGetValue(key, out val) == true)
            return true;
        val = default;
        return false;
    }
}