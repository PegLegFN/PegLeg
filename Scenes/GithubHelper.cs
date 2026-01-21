using Godot;
using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

public static class GithubHelper
{
    static RegEx defaultVersionRegex;
    static RegEx DefaultVersionRegex
    {
        get
        {
            if (defaultVersionRegex is not null)
                return defaultVersionRegex;
            defaultVersionRegex = new();
            defaultVersionRegex.Compile($"^v(\\d+)\\.(\\d+)\\.(\\d+)(?:-b(\\d+))?$");
            return defaultVersionRegex;
        }
    }

    static readonly Uri githubApi = new("https://api.github.com");
    static JsonSerializerOptions serialiserOptions = new() { IncludeFields = true, WriteIndented = true };

    public record struct ReleaseData
    {
        public string name;
        public string tag_name;
        public string body;
        public bool prerelease;
        public ReleaseAsset[] assets;

        [JsonIgnore]
        public readonly ReleaseVersion Version => TryGetVersion(out var v) ? v : v;
        public readonly bool TryGetVersion(out ReleaseVersion version, RegEx versionRegex = null) => 
            ReleaseVersion.Parse(tag_name, out version, versionRegex);

    }
    public record struct ReleaseAsset
    {
        public string name;
        public string browser_download_url;

        public async Task DownloadTo(Stream dest, IProgress<(long, long)> progress = null, CancellationToken ct = default)
        {
            await WebHelpers.MakeRequest(browser_download_url)
                .SendAsDownload(dest, progress, ct);
        }
    }

    public record struct ReleaseVersion(int major, int minor, int patch) : IComparable<ReleaseVersion>
    {
        public static bool Parse(
            string versionText,
            out ReleaseVersion version,
            RegEx versionRegex = null
        )
        {
            versionRegex ??= DefaultVersionRegex;
            version = default;
            if (versionRegex.Search(versionText) is not RegExMatch standardMatch)
                return false;
            var groups = standardMatch.Strings;
            int major = int.TryParse(groups[1], out var mj) ? mj : 0;
            int minor = int.TryParse(groups[2], out var mn) ? mn : 0;
            int patch = int.TryParse(groups[3], out var pt) ? pt : 0;
            if (groups.Length > 4 && int.TryParse(groups[4], out var beta)) 
            {
                patch *= 100;
                patch += beta;
            }
            if (major.ToString() != groups[1])
            {
                GD.Print($"Incorrect number format in Major version number ({major} != {groups[1]}, \"{versionText}\")");
                return false;
            }
            if (minor.ToString() != groups[2])
            {
                GD.Print($"Incorrect number format in Minor version number ({minor} != {groups[2]}, \"{versionText}\")");
                return false;
            }
            if (patch.ToString() != groups[3])
            {
                GD.Print($"Incorrect number format in Patch version number ({patch} != {groups[3]}, \"{versionText}\")");
                return false;
            }
            version = new(major, minor, patch);
            return true;
        }

        public int PatchNum => patch / 100;
        public int BetaNum => patch % 100;
        public bool IsBeta => BetaNum > 0;

        public readonly int CompareTo(ReleaseVersion other)
        {
            if (major != other.major)
                return major.CompareTo(other.major);
            if (minor != other.minor)
                return minor.CompareTo(other.minor);
            return patch.CompareTo(other.patch);
        }

        public static bool operator >(ReleaseVersion first, ReleaseVersion second) => first.CompareTo(second) > 0;
        public static bool operator <(ReleaseVersion first, ReleaseVersion second) => first.CompareTo(second) < 0;
        public static bool operator >=(ReleaseVersion first, ReleaseVersion second) => first.CompareTo(second) >= 0;
        public static bool operator <=(ReleaseVersion first, ReleaseVersion second) => first.CompareTo(second) <= 0;

        public override readonly string ToString() => $"v{major}.{minor}.{patch}";
    }

    public static async Task<ReleaseData[]> FetchReleases(string user, string repo)
    {
        using var releasesResponse = await githubApi
            .MakeRequest($"/repos/{user}/{repo}/releases")
            .Send();
        if (await releasesResponse.CheckForError())
            return [];
        return await releasesResponse
                .Content
                .ReadFromJsonAsync<ReleaseData[]>(serialiserOptions);
    }
}
