
using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.NetworkInformation;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using HttpClient = System.Net.Http.HttpClient;

public static class WebHelpers
{
    static HttpClient plClient = null;
    public static HttpClient PLClient
    {
        get
        {
            if (plClient is not null)
                return plClient;
            plClient = new HttpClient();
            plClient.DefaultRequestHeaders.Add("User-Agent", $"PegLeg/PegLeg-{AppConfig.PegLegVersion}");
            return plClient;
        }
    }

    public class BoundHttpsRequestMessage : HttpRequestMessage
    {
        public BoundHttpsRequestMessage() : base() { }
        public BoundHttpsRequestMessage(HttpClient client, HttpMethod method, Uri uri) : base(method, uri)
        {
            BoundClient = client;
        }
        public HttpClient BoundClient { get; set; }
        public GameAccount BoundAccount { get; set; }
    }

    public static async Task<bool> Ping(string hostnameOrAddress)
    {
        using Ping ping = new();
        bool success = false;
        try
        {
            var reply = await ping.SendPingAsync(hostnameOrAddress);
            success = reply.Status == IPStatus.Success;
        }
        catch(Exception e)
        {
            GD.PrintErr(e);
        }
        return success;
    }

    public static BoundHttpsRequestMessage MakeRequest(this Uri uri, string path, HttpMethod method = null) =>
        new(PLClient, method ?? HttpMethod.Get, new(uri, path));

    public static BoundHttpsRequestMessage MakeRequest(string uri, HttpMethod method = null) =>
        new(PLClient, method ?? HttpMethod.Get, new(uri));

    public static T SetAuthorisation<T>(this T msg, AuthenticationHeaderValue auth) where T: HttpRequestMessage
    {
        msg.Headers.Authorization = auth;
        return msg;
    }

    public static T SetAccount<T>(this T msg, GameAccount account = null) where T : BoundHttpsRequestMessage
    {
        account ??= GameAccount.ActiveAccount;
        msg.BoundAccount = account;
        msg.Headers.Authorization = account.AuthHeader;
        return msg;
    }

    public static T AddHeader<T>(this T msg, string name, string value) where T : HttpRequestMessage
    {
        msg.Headers.Add(name, value);
        return msg;
    }

    public static T AddCosmeticHeader<T>(this T msg) where T : HttpRequestMessage
    {
        msg.Headers.Add("x-api-key", Helpers.cosmeticSalsa);
        return msg;
    }

    public static T SetFormContent<T>(this T msg, string formContent = "") where T : HttpRequestMessage
    {
        msg.Content?.Dispose();
        msg.Content = new StringContent(formContent, Encoding.UTF8, "application/x-www-form-urlencoded");
        return msg;
    }

    public static T SetContent<T>(this T msg, HttpContent content) where T : HttpRequestMessage
    {
        msg.Content?.Dispose();
        msg.Content = content;
        return msg;
    }

    public static MultipartFormDataContent AddStringContent(this MultipartFormDataContent multipartFormContent, string name, string content)
    {
        multipartFormContent.Add(new StringContent(content, Encoding.UTF8, "application/text"), name);
        return multipartFormContent;
    }
    public static MultipartFormDataContent AddTextFileContent(this MultipartFormDataContent multipartFormContent, string name, string content, string filename = "content.txt")
    {
        multipartFormContent.Add(new StringContent(content, Encoding.UTF8, "application/text"), name, filename);
        return multipartFormContent;
    }
    public static MultipartFormDataContent AddImageContent(this MultipartFormDataContent multipartFormContent, string name, Image content, string filename="image")
    {
        multipartFormContent.Add(new ByteArrayContent(content.SavePngToBuffer()), name, filename+".png");
        return multipartFormContent;
    }

    public static T SetStringContent<T>(this T msg, string stringContent) where T : HttpRequestMessage
    {
        msg.Content?.Dispose();
        msg.Content = new StringContent(stringContent, Encoding.UTF8, "application/text");
        return msg;
    }

    public static T SetJsonContent<T>(this T msg, string jsonTextContent = "{}") where T : HttpRequestMessage
    {
        msg.Content?.Dispose();
        msg.Content = new StringContent(jsonTextContent, Encoding.UTF8, "application/json");
        return msg;
    }

    public static T SetJsonContent<T>(this T msg, JsonObject jsonContent) where T : HttpRequestMessage
    {
        jsonContent ??= [];
        msg.Content?.Dispose();
        msg.Content = new StringContent(jsonContent.ToString(), Encoding.UTF8, "application/json");
        return msg;
    }



    public static async Task<HttpResponseMessage> Send(this BoundHttpsRequestMessage msg, bool disposeMsg = true)
    {
        if (msg.BoundAccount is not null)
        {
            await msg.BoundAccount.Authenticate();
            msg.Headers.Authorization = msg.BoundAccount.AuthHeader;
        }
        var response = await CloneAndSend(msg, disposeMsg);

        //TODO: configurable retry attempt count
        for (int i = 0; i < 2; i++)
        {
            if (response.StatusCode != HttpStatusCode.ServiceUnavailable)
                break;
            //TODO: configurable retry delay
            await Task.Delay(1000);
            response = await CloneAndSend(msg, disposeMsg);
        }

        if (
            msg.BoundAccount is not null &&
            !response.IsSuccessStatusCode &&
            response.Headers.TryGetValues("x-epic-error-code", out var errCode) && 
            errCode.FirstOrDefault() == "1031"
        )
        {
            GD.Print("token invalid, exiring token and retrying with new token...");
            msg.BoundAccount.ForceExpireToken();
            await msg.BoundAccount.Authenticate();
            msg.Headers.Authorization = msg.BoundAccount.AuthHeader;
            response = await CloneAndSend(msg, disposeMsg);
        }
        msg.Dispose();
        return response;
    }

    static async Task<HttpResponseMessage> CloneAndSend(BoundHttpsRequestMessage msg, bool disposeMsg) =>
        await (await msg.CloneMessageAsync()).SendTo(msg.BoundClient, disposeMsg);

    public static async Task<T> CloneMessageAsync<T>(this T req) where T : HttpRequestMessage, new()
    {
        T clone = new()
        {
            Method = req.Method,
            RequestUri = req.RequestUri
        };

        // Copy the request's content (via a MemoryStream) into the cloned object
        var ms = new MemoryStream();
        if (req.Content != null)
        {
            await req.Content.CopyToAsync(ms).ConfigureAwait(false);
            ms.Position = 0;
            clone.Content = new StreamContent(ms);

            // Copy the content headers
            foreach (var h in req.Content.Headers)
                clone.Content.Headers.Add(h.Key, h.Value);
        }

        clone.Version = req.Version;

        foreach (KeyValuePair<string, object> option in req.Options)
            clone.Options.Set(new HttpRequestOptionsKey<object>(option.Key), option.Value);

        foreach (KeyValuePair<string, IEnumerable<string>> header in req.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        return clone;
    }

    public static async Task<HttpResponseMessage> SendTo(this HttpRequestMessage msg, HttpClient client, bool disposeMsg = true)
    {
        var r = await client.SendAsync(msg);
        if (disposeMsg)
            msg.Dispose();
        return r;
    }

    public class DownloadProgressHandle : IProgress<(long, long)>
    {
        public event Action OnProgress;
        long curVal;
        long maxVal;
        public long CurrentValue => curVal;
        public float ProgressPercent => (float)(maxVal > 0 ? (curVal*100.0) / maxVal : 0);
        public long MaxValue => maxVal;
        public void Report((long, long) value)
        {
            curVal = value.Item1;
            maxVal = value.Item2;
            OnProgress?.Invoke();
        }
    }

    public static ActionProgress AsProgress(this Action<long, long> action) => new(action);
    public class ActionProgress(Action<long, long> action) : IProgress<(long, long)>
    {
        public void Report((long, long) tuple) => action?.Invoke(tuple.Item1, tuple.Item2);
    }

    public static async Task SendAsDownload(this BoundHttpsRequestMessage msg, Stream dest, IProgress<(long, long)> progress = null, CancellationToken ct = default)
    {
        using var response = await msg.SendAsDownloadR(dest, progress, ct);
    }

    public static async Task<HttpResponseMessage> SendAsDownloadR(this BoundHttpsRequestMessage msg, Stream dest, IProgress<(long, long)> progress = null, CancellationToken ct = default)
    {
        var response = await msg.BoundClient.SendAsync(msg, HttpCompletionOption.ResponseHeadersRead, ct);
        var contentLength = response.Content.Headers.ContentLength;

        using var download = await response.Content.ReadAsStreamAsync(ct);

        // Ignore progress reporting when no progress reporter was 
        // passed or when the content length is unknown
        if (progress == null || !contentLength.HasValue)
        {
            await download.CopyToAsync(dest, ct);
            return response;
        }

        // Convert absolute progress (bytes downloaded) into relative progress (0% - 100%)
        var relativeProgress = new Progress<long>(totalBytes => progress.Report((totalBytes, contentLength.Value)));
        // Use extension method to report progress while downloading
        await download.CopyToAsync(dest, 81920, relativeProgress, ct);
        progress.Report((contentLength.Value, contentLength.Value));
        return response;
    }

    public static Task<JsonNode> ReadJson(this HttpResponseMessage response) => 
        response.ReadJson<JsonNode>();

    public static Task<T> ReadJson<T>(this HttpResponseMessage response, JsonSerializerOptions options = null)
    {
        if (response.Content?.Headers?.ContentType?.MediaType != "application/json")
            return Task.FromResult<T>(default);
        return response.Content.ReadFromJsonAsync<T>(options);
    }

    public static async Task<Image> ReadImage(this HttpResponseMessage response)
    {
        var mediaType = response.Content?.Headers?.ContentType?.MediaType;
        if (!mediaType.StartsWith("image/"))
            return null;
        string subtype = mediaType.Split("/")[1];
        Image image = new();
        Error status = subtype switch
        {
            "jpeg" => image.LoadJpgFromBuffer(await response.Content.ReadAsByteArrayAsync()),
            "png" => image.LoadPngFromBuffer(await response.Content.ReadAsByteArrayAsync()),
            "webp" => image.LoadWebpFromBuffer(await response.Content.ReadAsByteArrayAsync()),
            _ => Error.CantOpen
        };
        if (status == Error.Ok)
            return image;
        return null;
    }

    public static async Task<bool> CheckForError(this HttpResponseMessage response, bool showErrorPopup = false, bool logError = true)
    {
        (var res, _) = await response.CheckForErrorJson(showErrorPopup, logError);
        return res;
    }

    public static async Task<(bool, JsonNode)> CheckForErrorJson(this HttpResponseMessage response, bool showErrorPopup = false, bool logError = true)
    {
        if (response.IsSuccessStatusCode)
            return (false, null);
        GameAccount boundAccount = null;
        if (response.RequestMessage is BoundHttpsRequestMessage boundMsg)
            boundAccount = boundMsg.BoundAccount;
        if (response.Headers.TryGetValues("x-epic-error-code", out var errCode))
        {
            string code = errCode.FirstOrDefault();
            if (code == "1031")
            {
                GD.Print("token invalid, expiring token");
                if (boundAccount is not null)
                    boundAccount?.ForceExpireToken();
            }
            else if (code == "1012")
            {
                //waiting for link code to complete, error should be silent
                logError = false;
                showErrorPopup = false;
            }
            else if (code == "18130" && response.RequestMessage.Method == HttpMethod.Delete)
            {
                //attempting to delete nonexistant device, error should be silent
                logError = false;
                showErrorPopup = false;
            }
        }
        string fallbackErrorCode = null;
        if (response.Headers.TryGetValues("x-epic-error-name", out var errName))
            fallbackErrorCode= errName.FirstOrDefault();

        JsonNode errorContent = null;
        try
        {
            errorContent = await response.ReadJson();
        }
        catch (ObjectDisposedException)
        {
            GD.Print("error response disposed");
        }

        if (logError)
        {
            string logMsg = $"Web Request Error when sending {response?.RequestMessage?.Method} to {response?.RequestMessage?.RequestUri}{(boundAccount is null ? "" : $" as {boundAccount.DisplayName}")}";
            logMsg += $"\nStatusCode: {(int)(response?.StatusCode ?? HttpStatusCode.Gone)}, ReasonPhrase: {response?.ReasonPhrase}";
            if (errorContent is not null)
                logMsg += $"\nContent: \n{errorContent.ToJsonString()}";
            else if (fallbackErrorCode is not null)
                logMsg += $"\nEpic Error Name: {fallbackErrorCode}";
            logMsg = logMsg.FixLogLines();
            GD.PrintRich($"[color=orange]{logMsg}[/color]");
            if (OS.HasFeature("editor"))
                GD.PushWarning(logMsg);
        }

        if (showErrorPopup)
        {
            GenericConfirmationWindow.ShowConfirmation(
                "Uh oh! Something Goofed",
                "Continue",
                contextText:
                    errorContent?["errorMessage"]?.ToString() ??
                    response.ReasonPhrase ??
                    "An uncaught web error occured",
                warningText:
                    errorContent?["errorCode"]?.ToString() ??
                    fallbackErrorCode ??
                    response.StatusCode.ToString(),
                allowCancel: false
            ).StartTask();
        }

        return (true, errorContent);
    }
}
