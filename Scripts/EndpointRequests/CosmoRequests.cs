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

	static string GetCosmoURLFromPath(string cosmoPath, string key)
	{
		byte[] pathBytes = cosmoPath.ToUtf8Buffer();
		byte[] keyBytes = B64ToBytes(key);
		byte[] mergedBytes = [.. pathBytes, .. keyBytes];

		var byteStream = new MemoryStream(mergedBytes);
		byte[] hashedBytes = SHA256.HashData(byteStream);
		var hashText = BytesToB64(hashedBytes);

		return $"https://cosmo.fdeb.live.use1a.on.epicgames.com/v1/item/{hashText}/png";
	}

	public static string GetCosmoURL(string templateId, string gameVer = null, string key = null)
	{
		var splitTemplate = templateId.Split(':');
		if (splitTemplate.Length < 2)
			return null;
		templateId = $"{splitTemplate[0]}:{splitTemplate[1].ToLower()}";
		gameVer ??= PegLegResourceManager.MagicNumbers["cosmo"]?["version"]?.ToString() ?? "40.30";
		key ??= PegLegResourceManager.MagicNumbers["cosmo"]?["key"]?.ToString() ?? "czhmP4D5JdqrFCrAM3bdrDRxHpxNJwUckrNbr+XeDHg=";
		return GetCosmoURLFromPath($"fn/{gameVer}/{templateId}/locker_preview_image", key);
	}

	public static async Task<Image> FetchCosmoImage(string templateId, string gameVer = null, string key = null)
	{
		//template type might be needed for true uniqueness
		//var uniqueId = templateId.Replace(":", "__");
		var uniqueId = templateId.Split(':')[^1];
		if (CatalogRequests.TryGetCosmeticImage(uniqueId, 128) is Image existingTexture)
			return existingTexture;

		var url = GetCosmoURL(templateId, gameVer, key);
		using var result = await WebHelpers.MakeRequest(url).Accepts(WebMedia.Image.Any).Send();
		if (await result.CheckForError())
			return null;

		Image image = await result.ReadImage();
		//Image image = await result.ReadDownloadImage(testStream);
		if (image is null)
			return null;
		CatalogRequests.RegisterCosmeticImage(ref image, uniqueId, 128);
		return CatalogRequests.TryGetCosmeticImage(uniqueId);
	}
}
