using System.Text.RegularExpressions;
using Serilog;

namespace SpicetifyDeck.Artwork;

public sealed class ArtworkFetcher(IHttpClientFactory httpClientFactory, ILogger logger)
{
		private const int MaxBytes = 2 * 1024 * 1024;

	public async Task<byte[]?> FetchAsync(string? imageUrl, int size, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(imageUrl) || !Uri.TryCreate(imageUrl, UriKind.Absolute, out var uri))
		{
			return null;
		}

		uri = new Uri(ArtworkUrl.Resize(uri.AbsoluteUri, size), UriKind.Absolute);

		try
		{
			var client = httpClientFactory.CreateClient(nameof(ArtworkFetcher));
			using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
				.ConfigureAwait(false);

			if (!response.IsSuccessStatusCode)
			{
				logger.Debug("Artwork request answered {Status}.", (int)response.StatusCode);
				return null;
			}

			if (response.Content.Headers.ContentLength is { } declared && declared > MaxBytes)
			{
				return null;
			}

			var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
			return bytes.Length > MaxBytes ? null : bytes;
		}
		catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
		{
			logger.Debug(exception, "Could not download the album artwork.");
			return null;
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
