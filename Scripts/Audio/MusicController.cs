using Godot;

public partial class MusicController : Node
{
    static MusicController instance;

    [Export]
    AudioStreamPlayer musicA;
    [Export]
    AudioStreamPlayer musicB;

    [Export]
    float transitionDelayMin = 5;

    [Export]
    float transitionDelayMax = 15;

    [Export]
    float transitionTime = 15;

    [Export]
    float muteTime = 1;

    public override void _Ready()
    {
        instance = this;
        musicA.Finished += PlayMusic;
        ThemeController.OnThemeChanged += OnThemeUpdated;
        OnThemeUpdated();
    }

    record struct MusicState(AppTheme.MusicPlaylist playlist)
    {
        public bool isOverride = false;
        public AppTheme.MusicPlaylist playlist = playlist;
        public AppTheme.MusicTrack track = null;
        public AppTheme.MusicFile layer = null;
        public float time;
    }

    bool isSwappingState = false;
    MusicState preservedState;
    MusicState currentState;
    MusicState nextState;

    Tween currentTransition;

    void OnThemeUpdated()
    {
        SetMainState(new(ThemeController.activeTheme?.PickPlaylist()));
    }

    void SetMainState(MusicState state)
    {
        if (currentState.isOverride)
            preservedState = state;
        else
            TransitionToState(state with { isOverride = false });
    }

    void SetOverrideState(MusicState state)
    {
        TransitionToState(state with { isOverride = true});
    }

    void ClearOverride()
    {
        if (currentState.isOverride)
            TransitionToState(preservedState);
    }

    async void TransitionToState(MusicState state)
    {
        nextState = state;
        if (isSwappingState || (state.playlist == null && currentState.playlist == null))
            return;
        isSwappingState = true;

        if (currentState.playlist != null)
        {
            EndMusic(muteTime);
            await Helpers.WaitForTimer(muteTime);
        }

        if (nextState.isOverride && !currentState.isOverride)
            preservedState = currentState with { time = musicA.GetPlaybackPosition() };
        currentState = nextState;

        isSwappingState = false;
        BeginMusic();
    }

    void BeginMusic()
    {
        if (currentState.playlist == null)
            return;
        if (currentState.layer != null)
        {
            //resume layer from timestamp
            musicA.Stream = currentState.layer.File;
            musicA.Play(currentState.time);
        }
        else
        {
            PlayMusic();
        }

        if (isSwappingState)
            return;

        if (currentTransition?.IsValid() ?? false)
            currentTransition.Kill();

        currentTransition = GetTree().CreateTween().SetTrans(Tween.TransitionType.Expo);
        currentTransition.Parallel().TweenProperty(musicA, "volume_db", 0, muteTime).SetEase(Tween.EaseType.Out);
    }

    void PlayMusic()
    {
        if (currentState.playlist is null)
            return;

        bool switchTracks = GD.Randf() <= currentState.playlist.trackSwitchChance;
        bool switchLayers = GD.Randf() <= currentState.playlist.layerSwitchChance && !isSwappingState;

        if (!switchTracks && !switchLayers && currentState.track is not null && currentState.layer is not null)
        {
            musicA.Stream = currentState.layer.File;
            musicA.Play();
            return;
        }

        var prevLayer = currentState.layer;
        if (currentState.track is null)
        {
            currentState.layer = null;
            switchTracks = true;
            switchLayers = false;
        }

        if (switchTracks)
            currentState.track = currentState.playlist.PickTrack(currentState.track);

        if (switchLayers || currentState.layer is null)
            currentState.layer = currentState.track.PickLayer(currentState.layer);

        if (prevLayer == currentState.layer)
            switchLayers = false;

        if (switchLayers && prevLayer is not null)
        {

            musicB.VolumeDb = musicA.VolumeDb;
            musicA.VolumeDb = -80;

            musicA.Stream = currentState.layer.File;
            musicB.Stream = prevLayer.File;

            musicA.Play();
            musicB.Play();

            if (currentTransition?.IsValid() ?? false)
                currentTransition.Kill();

            currentTransition = GetTree().CreateTween().SetTrans(Tween.TransitionType.Expo);

            double transitionDelay = GD.RandRange(transitionDelayMin, transitionDelayMax);
            transitionDelay = Mathf.Min(transitionDelay, musicA.Stream.GetLength() - (transitionTime + 1));

            currentTransition.TweenInterval(transitionDelay);
            currentTransition.Parallel().TweenProperty(musicA, "volume_db", 0, transitionTime).SetEase(Tween.EaseType.Out);
            currentTransition.Parallel().TweenProperty(musicB, "volume_db", -80, transitionTime).SetEase(Tween.EaseType.In);
        }
        else
        {
            //if (prevLayer is null)
            //    GD.Print($"layer {currentState.track.IndexOf(currentState.layer) + 1} out of {currentState.track.Layers.Length}");
            musicA.Stream = currentState.layer.File;

            if (prevLayer is null && currentState.playlist.PickIntro() is AppTheme.MusicFile introFile)
            {
                //GD.Print($"using intro");
                musicA.Stream = introFile.File;
                currentState.track = null;
                currentState.layer = introFile;
            }

            musicA.Play();
        }
    }

    void EndMusic(float time)
    {
        if (currentTransition?.IsValid() ?? false)
            currentTransition.Kill();
        currentTransition = GetTree().CreateTween().SetTrans(Tween.TransitionType.Expo).SetParallel();
        currentTransition.TweenProperty(musicA, "volume_db", -80, time).SetEase(Tween.EaseType.In);
        currentTransition.TweenProperty(musicB, "volume_db", -80, time).SetEase(Tween.EaseType.In);
        currentTransition.Finished += () =>
        {
            musicA.Stop();
            musicB.Stop();
        };
    }

    public static void StopMusic() => instance.SetOverrideState(new(null));
    public static void ResumeMusic() => instance.ClearOverride();

    public static void OverridePlaylist(AppTheme.MusicPlaylist playlist)
    {
        if (playlist is null)
            instance.ClearOverride();
        else
            instance.SetOverrideState(new(playlist));
    }
}
