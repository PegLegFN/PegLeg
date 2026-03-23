using Godot;
using System;

public partial class UISoundHook : Node
{
	[Export]
	string defaultSound;

    public void PlaySound()=> UISounds.PlaySound(defaultSound);
    public void PlaySound(string sound) => UISounds.PlaySound(sound);

    public void StopSound() => UISounds.StopSound(defaultSound);
    public void StopSound(string sound) => UISounds.StopSound(sound);
}
