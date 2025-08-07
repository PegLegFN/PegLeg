using Godot;
using Godot.Collections;

[GlobalClass]
public partial class FrozenStringToFloatProxy : FrozenDictProxy<string, float>
{
    [Export]
    Dictionary<string, float> dictContents;
    protected override Dictionary<string, float> DictContents => dictContents;
}