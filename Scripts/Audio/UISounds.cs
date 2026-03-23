using Godot;
using System;
using System.Collections.Generic;

public partial class UISounds : Node
{
    static UISounds instance;
    Dictionary<string, AudioStreamPlayer> audioDict = [];

    public override void _Ready()
    {
        instance = this;
        foreach (var item in GetChildren())
        {
            if(item is AudioStreamPlayer audio)
                audioDict[item.Name] = audio;
        }
    }

    public static void PlaySound(string soundName)
    {
        if(instance?.audioDict.ContainsKey(soundName) ?? false)
            instance.audioDict[soundName].Play();
    }
    public static void StopSound(string soundName)
    {
        if (instance?.audioDict.ContainsKey(soundName) ?? false)
            instance.audioDict[soundName].Stop();
    }
}
