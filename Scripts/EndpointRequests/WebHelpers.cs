
using Godot;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using HttpClient = System.Net.Http.HttpClient;

public static class WebHelpers
{
    public class BoundHttpsRequestMessage(HttpClient client, HttpMethod method, string uri) : HttpRequestMessage(method, uri)
    {
        public HttpClient BoundClient { get; set; } = client;
    }

    public static BoundHttpsRequestMessage MakeRequest(this HttpClient client, string uri, HttpMethod method = null) =>
        new(client, method ?? HttpMethod.Get, uri);

    public static BoundHttpsRequestMessage MakeLinkRequest(this HttpClient client, string link, HttpMethod method = null) =>
        new(client, method ?? HttpMethod.Get, link[client.BaseAddress.OriginalString.Length..]);


    public static HttpRequestMessage MakeRequest(string uri, HttpMethod method = null) => new(method ?? HttpMethod.Get, uri);
    public static T SetAuthorisation<T>(this T msg, AuthenticationHeaderValue auth) where T: HttpRequestMessage
    {
        msg.Headers.Authorization = auth;
        return msg;
    }

    public static T AddHeader<T>(this T msg, string name, string value) where T : HttpRequestMessage
    {
        msg.Headers.Add(name, value);
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

    public static async Task<HttpResponseMessage> Send(this BoundHttpsRequestMessage msg, bool disposeMsg = true) =>
        await msg.SendTo(msg.BoundClient);

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
}
