
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;

public static class WebHelpers
{
    public class BoundHttpsRequestMessage(HttpClient client, HttpMethod method, string uri) : HttpRequestMessage(method, uri)
    {
        public HttpClient BoundClient { get; set; } = client;
    }

    public static BoundHttpsRequestMessage MakeRequest(this HttpClient client, string uri, HttpMethod method = null) =>
        new(client, method ?? HttpMethod.Get, uri);
    

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

    public static T SetJsonContent<T>(this T msg, string jsonContent = "{}") where T : HttpRequestMessage
    {
        msg.Content?.Dispose();
        msg.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");
        return msg;
    }

    public static async Task<HttpResponseMessage> Send(this BoundHttpsRequestMessage msg) =>
        await msg.BoundClient.SendAsync(msg);

    public static async Task<HttpResponseMessage> SendTo(this HttpRequestMessage msg, HttpClient endpoint) => 
        await endpoint.SendAsync(msg);
}
