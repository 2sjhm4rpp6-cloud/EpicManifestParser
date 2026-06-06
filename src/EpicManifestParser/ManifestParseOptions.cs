using System.Net;

using EpicManifestParser.Api;
using OffiUtils;

namespace EpicManifestParser;
// ReSharper disable UseSymbolAlias

/// <summary>
/// Options/Configuration for parsing manifests
/// </summary>
public class ManifestParseOptions
{
    /// <summary>
    /// Used to decompress zlib data in chunks and binary serialized manifests.
    /// </summary>
    public IDecompressor? Decompressor { get; set; }

    /// <summary>
    /// Required for downloading, must have a leading slash!
    /// </summary>
    /// <remarks>
    /// Example: <code>http://epicgames-download1.akamaized.net/Builds/Fortnite/CloudDir/</code><br/>
    /// Distributionpoints can be found here: <see href="https://launcher-public-service-prod06.ol.epicgames.com/launcher/api/public/distributionpoints">here.</see>
    /// </remarks>
    public string? ChunkBaseUrl { get; set; }

    /// <summary>
    /// Your own (optional) <see cref="HttpClient"/> used for downloading, must not have a <see cref="HttpClient.BaseAddress"/> !
    /// </summary>
    public HttpClient? Client { get; set; }

    /// <summary>
    /// Buffer size for downloading chunks, defaults to <value>2097152</value> bytes (2 MiB).
    /// </summary>
    public int ChunkDownloadBufferSize { get; set; } = 2097152;

    /// <summary>
    /// Optional for caching chunks, very recommended.
    /// </summary>
    public string? ChunkCacheDirectory { get; set; }

    /// <summary>
    /// Whether or not to cache the chunks 1:1 as they were downloaded, defaults to <see langword="false"/>.
    /// </summary>
    public bool CacheChunksAsIs { get; set; }

    /// <summary>
    /// Optional for caching manifests when using <see cref="ManifestInfo.DownloadAndParseAsync(ManifestParseOptions, Predicate&lt;ManifestInfoElement&gt;?, Predicate&lt;ManifestInfoElementDownload&gt;?, CancellationToken)"/>.
    /// </summary>
    public string? ManifestCacheDirectory { get; set; }

    /// <summary>
    /// Creates a default <see cref="HttpClient"/> instance optimized for chunk downloading.
    /// </summary>
    /// <returns>The created <see cref="HttpClient"/> instance.</returns>
    public static HttpClient CreateDefaultClient()
    {
        var handler = new SocketsHttpHandler
        {
            UseCookies = false,
            UseProxy = false,
            AutomaticDecompression = DecompressionMethods.None,
            MaxConnectionsPerServer = 256,
            EnableMultipleHttp3Connections = true,
            EnableMultipleHttp2Connections = true,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2)
        };
        var client = new HttpClient(handler)
        {
            DefaultRequestVersion = HttpVersion.Version30,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
            Timeout = TimeSpan.FromSeconds(50)
        };
        client.DefaultRequestHeaders.Accept.ParseAdd("*/*");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("EpicOnlineServicesInstallHelper/5.3.0-54393070+++UE5+Dev-Distro-5.5-5e3057 (http-eventloop) Windows/10.0.26200.1.256.64bit");
        return client;
    }
}
