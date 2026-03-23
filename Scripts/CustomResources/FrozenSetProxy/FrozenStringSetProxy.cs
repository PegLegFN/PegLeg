using Godot;

[GlobalClass]
public partial class FrozenStringSetProxy : FrozenSetProxy<string>
{
	[Export]
	string[] setContents;
	protected override string[] SetContents => setContents;
}
