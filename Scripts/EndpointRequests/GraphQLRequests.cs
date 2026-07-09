using Godot;
using GraphQL;
using GraphQL.Client.Http;
using GraphQL.Client.Serializer.SystemTextJson;
using System.Linq;

public static class GraphQLRequests
{
	public static GraphQLHttpClient Buffer { get; private set; } = new("https://api.buffer.com", new SystemTextJsonSerializer());

	public static bool CheckForErrors<T>(this GraphQLResponse<T> request, bool print = true)
	{
		if ((request.Errors?.Length ?? 0) == 0)
			return false;
		if (print)
			GD.Print("GraphQL Error(s):\n" + string.Join("\n", request.Errors.Select(e => e.Message)));
		return true;
	}

	public static GraphQLHttpClient SetAuth(this GraphQLHttpClient gqlClient, string authKey)
	{
		gqlClient.HttpClient.DefaultRequestHeaders.Authorization = new("Bearer", authKey);
		return gqlClient;
	}
}
