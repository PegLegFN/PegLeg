using Godot;
using System.Collections.Generic;
using System.Linq;

public partial class AppConfig
{
    public AudioConfig audio = new();
    public class AudioConfig
    {
        public Dictionary<string, AudioChannel> channels;
        public struct AudioChannel()
        {
            public float volume = 1;
            public bool muted;
        }
    }
}
public partial class VolumeController : Node
{
    static List<Window> appWindows = [];
    public override async void _Ready()
    {
        await Helpers.WaitForFrame();
        GetTree().NodeAdded += RegisterWindow;
        GetTree().NodeRemoved += UnregisterWindow;
        RegisterWindow(GetWindow());
        musicMuted = appWindows.All(WindowUnfocused);
        musicVolumeScalar = musicMuted ? 0 : 1;
        RefreshVolumeLevels();
    }

    private void RegisterWindow(Node node)
    {
        if (node is not Window window)
            return;
        appWindows.Add(window);
        window.FocusEntered += RefreshMusicMuteState;
        window.FocusExited += RefreshMusicMuteState;
        RefreshMusicMuteState();
    }

    private void UnregisterWindow(Node node)
    {
        if (node is not Window window)
            return;
        appWindows.Remove(window);
        window.FocusEntered -= RefreshMusicMuteState;
        window.FocusExited -= RefreshMusicMuteState;
        RefreshMusicMuteState();
    }

    static bool WindowUnfocused(Window w) => !w.HasFocus() || w.Mode == Window.ModeEnum.Minimized;

    static bool musicMuted = false;
    static float musicVolumeScalar = 1;
    float MusicVolumeScalar
    {
        get => musicVolumeScalar;
        set
        {
            musicVolumeScalar = value;
            RefreshMusicVolume();
        }
    }
    Tween musicMuteTween = null;

    void RefreshMusicMuteState()
    {
        //GD.Print("focused: "+string.Join(", ",appWindows.Where(w => !WindowUnfocused(w)).Select(w => w.Name)));
        bool newState = appWindows.All(WindowUnfocused);
        if (newState == musicMuted)
            return;
        musicMuted = newState;
        if (musicMuteTween?.IsRunning() ?? false)
            musicMuteTween.Kill();
        if (GetTree() is not SceneTree tree)
            return;
        musicMuteTween = tree.CreateTween();
        musicMuteTween.TweenProperty(this, "MusicVolumeScalar", musicMuted ? 0 : 1, 0.5f);
        musicMuteTween.Play();
    }

    static void RefreshMusicVolume()
    {
        var idx = AudioServer.GetBusIndex("Music");
        AudioServer.SetBusVolumeDb(idx, GetBusMuted("Music") ? -80 : GetBusVolume("Music", true));
    }

    public static void RefreshVolumeLevels()
    {
        for (int i = 0; i < AudioServer.BusCount; i++)
        {
            string busName = AudioServer.GetBusName(i);
            AudioServer.SetBusVolumeDb(i, GetBusMuted(busName) ? -80 : GetBusVolume(busName, true));
        }
    }

    public static float GetBusVolume(string busName, bool processed = false)
    {
        var baseVal = AppConfig.Get<float>("audio", $"{busName}_volume", busName == "Master" ? -20 : 0);
        return processed ? ProcessBusVolume(busName, baseVal) : baseVal;
    }

    public static bool GetBusMuted(string busName)
    {
        return AppConfig.Get("audio", $"{busName}_muted", false);
    }

    public static void SetBusVolume(string busName, float newValue, bool print=false)
    {
        if (!GetBusMuted(busName))
            AudioServer.SetBusVolumeDb(AudioServer.GetBusIndex(busName), ProcessBusVolume(busName, newValue));
        AppConfig.Set("audio", $"{busName}_volume", newValue, print);
    }

    public static void SetBusMuted(string busName, bool newValue)
    {
        int busIdx = AudioServer.GetBusIndex(busName);
        AudioServer.SetBusVolumeDb(busIdx, newValue ? -80 : GetBusVolume(busName, true));
        AppConfig.Set("audio", $"{busName}_muted", newValue);
    }

    static float ProcessBusVolume(string busName, float baseValue)
    {
        if (busName != "Music")
            return baseValue;
        var invScalar = 1 - musicVolumeScalar;
        var expScalar = 1 - (invScalar * invScalar);
        var lerped = Mathf.Lerp(-100, baseValue, expScalar);
        //GD.Print($"lerp to {baseValue} ({musicVolumeScalar}) = {lerped}");
        return lerped;
    }

    public static void ToggleBusMuted(string busName) =>
        SetBusMuted(busName, !GetBusMuted(busName));
}
