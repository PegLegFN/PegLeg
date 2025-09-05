using Godot;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HttpClient = System.Net.Http.HttpClient;

public partial class DiscordWebhookProxy
{
    static Dictionary<string, DiscordWebhookProxy> proxyDict = [];

    static HttpClient discordClient = new() { BaseAddress = new("https://discord.com") };

    public static bool TryGetProxy(string internalId, out DiscordWebhookProxy proxy) => proxyDict.TryGetValue(internalId, out proxy);

    string displayName;
    public string InternalName { get; private set; }
    Func<Task<Image[]>> imageGenerator;
    string uuid = Guid.NewGuid().ToString();

    public DiscordWebhookProxy(string displayName, string internalName, Func<Task<Image[]>> imageGenerator)
    {
        this.displayName = displayName;
        InternalName = internalName;
        this.imageGenerator = imageGenerator;
        proxyDict[internalName] = this;
    }

    public async Task CreateSyncMessage()
    {
        if (!AppConfig.Get("webhooks", InternalName + "_enabled", false))
            return;
        var urlEnding = AppConfig.Get("webhooks", InternalName + "_url", "");
        if (!UrlRegex().Match(urlEnding).Success)
        {
            GD.Print($"WH url failed: \"{urlEnding}\"");
            return;
        }
        var syncThreadId = AppConfig.Get("webhooks", InternalName + "_syncThread", "");

        var executionResponse = await discordClient
            .MakeRequest($"/api/webhooks/{urlEnding}?wait=true&thread_id={syncThreadId}", HttpMethod.Post)
            .SetJsonContent(new JsonObject() { ["username"] = "Sync: " + displayName, ["content"] = "Blank Sync Message" })
            .Send();
        if (!executionResponse.IsSuccessStatusCode)
        {
            GD.Print($"WH execution response failed: {executionResponse.ReasonPhrase}");
            return;
        }
        var executionJson = await executionResponse.Content.ReadFromJsonAsync<JsonObject>();
        if (executionJson?["id"]?.ToString() is not string syncId)
        {
            GD.Print($"WH message id does not exist (how did this happen?): \n{executionJson}");
            return;
        }
        GD.Print("WH sync message created");
        AppConfig.Set("webhooks", InternalName + "_sync", syncId);
    }

    public async Task Execute()
    {
        if (!AppConfig.Get("webhooks", InternalName + "_enabled", false))
            return;
        var urlEnding = AppConfig.Get("webhooks", InternalName + "_url", "");
        if (!UrlRegex().Match(urlEnding).Success)
        {
            GD.Print($"WH url failed: \"{urlEnding}\"");
            return;
        }
        var syncId = AppConfig.Get("webhooks", InternalName + "_sync", "");
        var syncThreadId = AppConfig.Get("webhooks", InternalName + "_syncThread", "");
        var editResponse = await discordClient
            .MakeRequest($"/api/webhooks/{urlEnding}/messages/{syncId}?wait=true&thread_id={syncThreadId}", HttpMethod.Patch)
            .SetJsonContent(new JsonObject() { ["content"] = uuid })
            .Send();
        if (!editResponse.IsSuccessStatusCode)
        {
            GD.Print($"WH edit response failed: {editResponse.ReasonPhrase}");
            return;
        }
        await Task.Delay(1000*3);
        var winnerResponse = await discordClient
            .MakeRequest($"/api/webhooks/{urlEnding}/messages/{syncId}?thread_id={syncThreadId}", HttpMethod.Get)
            .Send();
        if (!winnerResponse.IsSuccessStatusCode)
        {
            GD.Print($"winner response failed: {winnerResponse.ReasonPhrase}");
            return;
        }
        var winnerJson = await winnerResponse.Content.ReadFromJsonAsync<JsonObject>();
        if (winnerJson["content"]?.ToString() != uuid)
        {
            GD.Print($"WH did not win (\"{winnerJson["content"]?.ToString()}\" != \"{uuid}\")");
            return;
        }
        GD.Print("WH winner");

        var imageTask = imageGenerator?.Invoke();
        if (imageTask is null)
        {
            GD.Print($"WH image task null");
            return;
        }
        var images = await imageTask;
        if (images.Length == 0)
        {
            GD.Print($"WH no images");
            return;
        }
        MultipartFormDataContent formContent = [];

        formContent.AddStringContent("username", displayName);
        for (int i = 0; i < Mathf.Min(10, images.Length); i++)
        {
            string filename = images[i].ResourceName;
            if (string.IsNullOrEmpty(filename))
                filename = "image";
            formContent.AddImageContent("file" + i, images[i], filename);
        }

        var stringContent = await formContent.ReadAsStringAsync();

        var executionResponse = await discordClient
            .MakeRequest($"/api/webhooks/{urlEnding}", HttpMethod.Post)
            .SetContent(formContent)
            .Send();
        if(!executionResponse.IsSuccessStatusCode)
        {
            GD.Print($"WH execution response failed: {executionResponse.ReasonPhrase}");
            return;
        }
        GD.Print($"WH executed successfully");
    }

    [GeneratedRegex("^\\w*/(?:\\w|_)*$")]
    private static partial Regex UrlRegex();
}
