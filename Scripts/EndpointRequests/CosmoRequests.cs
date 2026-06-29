using Godot;
using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

public partial class CosmoRequests
{
	// Based off Krowe Mohs RE work

	private static byte[] B64ToBytes(string base64)
	{
		base64 = base64.Replace("-", "+").Replace("_", "/");
		return Convert.FromBase64String(base64);
	}

	
	private static string BytesToB64(byte[] bytes)
	{
		var base64 = Convert.ToBase64String(bytes);
		base64 = base64.Replace("+", "-").Replace("/", "_");
		return B64Ending().Replace(base64, "");
		//return base64;
	}

	[GeneratedRegex("=+$")]
	private static partial Regex B64Ending();

	static string GetCosmoURLFromPath(string cosmoPath, string key, string baseURL)
	{
		byte[] pathBytes = cosmoPath.ToUtf8Buffer();
		byte[] keyBytes = B64ToBytes(key);
		byte[] mergedBytes = [.. pathBytes, .. keyBytes];

		var byteStream = new MemoryStream(mergedBytes);
		byte[] hashedBytes = SHA256.HashData(byteStream);
		var hashText = BytesToB64(hashedBytes);

		return $"{baseURL}{hashText}/png";
	}

	public static string GetCosmoURL(string templateId, string imageType = "locker_preview_image", string gameVer = null, string key = null, string baseURL = null)
	{
		var splitTemplate = templateId.Split(':');
		if (splitTemplate.Length < 2)
			return null;
		templateId = $"{splitTemplate[0]}:{splitTemplate[1].ToLower()}";
		gameVer ??= PegLegResourceManager.MagicNumbers["cosmo"]?["version"]?.ToString() ?? "40.30";
		key ??= PegLegResourceManager.MagicNumbers["cosmo"]?["key"]?.ToString() ?? "czhmP4D5JdqrFCrAM3bdrDRxHpxNJwUckrNbr+XeDHg=";
		baseURL ??= PegLegResourceManager.MagicNumbers["cosmo"]?["baseUrl"]?.ToString() ?? "https://cosmo.fdeb.live.use1a.on.epicgames.com/v1/item/";
		return GetCosmoURLFromPath($"fn/{gameVer}/{templateId}/{imageType}", key, baseURL);
	}

	public static async Task<Image> FetchCosmoImage(string templateId, string imageType = "locker_preview_image", string gameVer = null, string key = null, string baseURL = null)
	{
		//template type might be needed for true uniqueness
		//var uniqueId = templateId.Replace(":", "__");
		var uniqueId = $"{templateId.Split(':')[^1]}-{imageType}";
		if (CatalogRequests.TryGetCosmeticImage(uniqueId, 128) is Image existingTexture)
			return existingTexture;

		var url = GetCosmoURL(templateId, imageType, gameVer, key, baseURL);
		using var result = await WebHelpers.MakeRequest(url).Accepts(WebMedia.Image.Any).Send();
		if (await result.CheckForError())
			return null;

		(Image image, byte[] buffer, string type) = await result.ReadImageWithBuffer();
		//Image image = await result.ReadDownloadImage(testStream);
		if (image is null)
			return null;

		CatalogRequests.RegisterCosmeticImageWithBuffer(ref image, buffer, type, uniqueId, 128);
		return CatalogRequests.TryGetCosmeticImage(uniqueId);
	}
}
