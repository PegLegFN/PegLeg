using Godot;
using Godot.Collections;

[GlobalClass]
public partial class FrozenStringToStringProxy : FrozenDictProxy<string, string>
{
    [Export]
    Dictionary<string, string> dictContents;
    protected override Dictionary<string, string> DictContents => dictContents;
}