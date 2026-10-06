using System.Net;
using System.Text.RegularExpressions;
using Serilog;

namespace SpicetifyDeck.Artwork;

public sealed class ArtworkFetcher(IHttpClientFactory httpClientFactory, ILogger logger)
{
		private const int MaxBytes = 2 * 1024 * 1024;
	private static readonly HashSet<string> ArtworkHosts = new(StringComparer.OrdinalIgnoreCase)
	{
		"i.scdn.co",
		"mosaic.scdn.co",
		"image-cdn-ak.spotifycdn.com",
		"image-cdn-fa.spotifycdn.com",
		"image-cdn.spotifycdn.com",
	};
	private static readonly HashSet<string> ArtworkMediaTypes = new(StringComparer.OrdinalIgnoreCase)
	{
		"image/jpeg",
		"image/png",
		"image/webp",
		"image/avif",
		"image/gif",
	};

	public async Task<byte[]?> FetchAsync(string? imageUrl, int size, CancellationToken cancellationToken)
	{
		if (!TryGetArtworkUri(imageUrl, out var uri) || uri is null)
		{
			logger.Debug("Rejected an album artwork URL outside the Spotify CDN allowlist.");
			return null;
		}

		uri = new Uri(ArtworkUrl.Resize(uri.AbsoluteUri, size), UriKind.Absolute);

		try
		{
			var client = httpClientFactory.CreateClient(nameof(ArtworkFetcher));
			using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
				.ConfigureAwait(false);

			if (response.StatusCode != HttpStatusCode.OK
				|| response.Content.Headers.ContentType?.MediaType is not { } mediaType
				|| !ArtworkMediaTypes.Contains(mediaType))
			{
				logger.Debug("Artwork request answered {Status}.", (int)response.StatusCode);
				return null;
			}

			if (response.Content.Headers.ContentLength is { } declared && declared > MaxBytes)
			{
				return null;
			}

			return await ReadArtworkAsync(response.Content, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception exception) when (
			!cancellationToken.IsCancellationRequested && exception is (HttpRequestException or TaskCanceledException))
		{
			logger.Debug(exception, "Could not download the album artwork.");
			return null;
		}
	}

	internal static bool TryGetArtworkUri(string? value, out Uri? uri)
	{
		uri = null;
		if (string.IsNullOrEmpty(value))
		{
			return false;
		}

		if (value.StartsWith("spotify:image:", StringComparison.OrdinalIgnoreCase))
		{
			var imageId = value["spotify:image:".Length..];
			if (imageId.Length == 0 || imageId.Any(character => !char.IsAsciiLetterOrDigit(character)))
			{
				return false;
			}

			value = $"https://i.scdn.co/image/{imageId}";
		}

		if (!value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
			|| !Uri.TryCreate(value, UriKind.Absolute, out var parsed)
			|| !parsed.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
			|| !ArtworkHosts.Contains(parsed.Host)
			|| !string.IsNullOrEmpty(parsed.UserInfo)
			|| !parsed.IsDefaultPort)
		{
			return false;
		}

		uri = parsed;
		return true;
	}

	private static async Task<byte[]?> ReadArtworkAsync(HttpContent content, CancellationToken cancellationToken)
	{
		await using var source = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
		using var image = new MemoryStream();
		var buffer = new byte[81920];
		while (true)
		{
			var count = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
			if (count == 0)
			{
				return image.ToArray();
			}

			if (image.Length + count > MaxBytes)
			{
				return null;
			}

			await image.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
		}
	}
}

public static partial class ArtworkUrl
{
	[GeneratedRegex(@"(?<size>64|128|160|200|300|320|640|1200)x\k<size>(?=\.\w+$)", RegexOptions.CultureInvariant)]
	private static partial Regex SizeSegment { get; }

	public static string Resize(string imageUrl, int size)
	{
		var clamped = Math.Clamp(size, 64, 640);
		return SizeSegment.IsMatch(imageUrl)
			? SizeSegment.Replace(imageUrl, $"{clamped}x{clamped}")
			: imageUrl;
	}
}
