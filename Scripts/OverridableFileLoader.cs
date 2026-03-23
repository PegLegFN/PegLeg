using Godot;
using System;
using System.Text.Json.Nodes;

public static class OverridableFileLoader
{
    const string builtInPath = "res://BuiltInData";
    const string customDataPath = "user://CustomData";

    public static JsonObject LoadJsonFile(string path, bool mergeObject = false)
    {
        string fullBuiltInPath = builtInPath + "/" + path;
        JsonObject builtInObject = null;
        if (FileAccess.FileExists(fullBuiltInPath))
        {
            using var builtInFile = FileAccess.Open(fullBuiltInPath, FileAccess.ModeFlags.Read);
            builtInObject = JsonNode.Parse(builtInFile.GetAsText())?.AsObject();
        }

        string fullCustomPath = customDataPath + "/" + path;
        JsonObject customObject = null;
        if (FileAccess.FileExists(fullCustomPath))
        {
            using var customFile = FileAccess.Open(fullCustomPath, FileAccess.ModeFlags.Read);
            customObject = JsonNode.Parse(customFile.GetAsText())?.AsObject();
        }

        if (customObject is null)
            return builtInObject;

        if(builtInObject is null)
            return customObject;

        foreach (var kvp in customObject)
        {
            builtInObject[kvp.Key] = kvp.Value.SafeDeepClone();
        }
        return builtInObject;
    }

    public static Image LoadImageFile(string path)
    {
        string fullPath = customDataPath + "/" + path;
        if (FileAccess.FileExists(fullPath))
        {
            Image image = new();
            if (image.Load(fullPath) != Error.Ok)
                return null;
            return image;
        }
        fullPath = builtInPath + "/" + path;
        return ResourceLoader.Load<Image>(fullPath, "Image");
    }

    public static ImageTexture LoadTextureFile(string path)
    {
        var image = LoadImageFile(path);
        if(image is null)
            return null;
        var imageTex = ImageTexture.CreateFromImage(image);
        return imageTex;
    }

    public static AudioStream LoadAudioFile(string path, int customWavMixRate = 96000)
    {
        string fullPath = customDataPath + "/" + path;
        if (FileAccess.FileExists(fullPath))
        {
            using var audioFile = FileAccess.Open(fullPath, FileAccess.ModeFlags.Read);
            if (fullPath.EndsWith(".wav"))
            {
                return new AudioStreamWav()
                {
                    Data = audioFile.GetBuffer((long)audioFile.GetLength()),
                    Format = AudioStreamWav.FormatEnum.Format16Bits,
                    MixRate = customWavMixRate,
                };
            }
            else if (fullPath.EndsWith(".mp3"))
            {
                return new AudioStreamMP3()
                {
                    Data = audioFile.GetBuffer((long)audioFile.GetLength()),
                };
            }
            else if (fullPath.EndsWith(".ogg"))
                return AudioStreamOggVorbis.LoadFromFile(fullPath);

            return null;
        }

        fullPath = builtInPath + "/" + path;

        if (ResourceLoader.Load(fullPath, "AudioStream") is AudioStream stream)
            return stream;
        return null;
    }
}
