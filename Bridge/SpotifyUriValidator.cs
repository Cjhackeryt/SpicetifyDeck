namespace SpicetifyDeck.Bridge;

public static class SpotifyUriValidator
{
	private static readonly HashSet<string> SpotifyWebHosts = new(StringComparer.OrdinalIgnoreCase)
	{
		"open.spotify.com",
	};

	private static readonly HashSet<string> PageKinds = new(StringComparer.Ordinal)
	{
		"artist",
		"album",
		"playlist",
		"collection",
		"show",
		"episode",
	};

	public static bool TryGetOpenTarget(string? input, out string target)
	{
		target = string.Empty;
		if (string.IsNullOrEmpty(input)
			|| !string.Equals(input, input.Trim(), StringComparison.Ordinal)
			|| input.Any(char.IsControl))
		{
			return false;
		}

		if (input.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase))
		{
			if (!IsValidSpotifyUri(input, allowRoot: true))
			{
				return false;
			}

			target = input;
			return true;
		}

		if (!input.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
			|| !Uri.TryCreate(input, UriKind.Absolute, out var uri)
			|| !uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
			|| !SpotifyWebHosts.Contains(uri.Host)
			|| !string.IsNullOrEmpty(uri.UserInfo)
			|| !uri.IsDefaultPort)
		{
			return false;
		}

		target = uri.AbsoluteUri;
		return true;
	}

	public static string? NormalizePageUri(string? input)
	{
		if (!IsValidSpotifyUri(input, allowRoot: false))
		{
			return null;
		}

		var parts = input!["spotify:".Length..].Split(':');
		var kind = parts[0].ToLowerInvariant();
		return parts.Length == 2
			&& PageKinds.Contains(kind)
			&& IsSafePageId(parts[1])
			? $"spotify:{kind}:{parts[1]}"
			: null;
	}

	private static bool IsValidSpotifyUri(string? input, bool allowRoot)
	{
		if (string.IsNullOrEmpty(input)
			|| !input.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase)
			|| input.Any(char.IsWhiteSpace)
			|| input.Any(char.IsControl))
		{
			return false;
		}

		if (allowRoot && input.Equals("spotify:", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		var parts = input["spotify:".Length..].Split(':');
		if (parts.Length < 2 || parts.Any(string.IsNullOrEmpty))
		{
			return false;
		}

		foreach (var character in string.Join(':', parts))
		{
			if (!char.IsLetterOrDigit(character)
				&& character is not (':' or '-' or '_' or '.' or '~' or '%' or '!' or '$'
					or '&' or '\'' or '(' or ')' or '*' or '+' or ',' or ';' or '='))
			{
				return false;
			}
		}

		for (var index = 0; index < input.Length; index++)
		{
			if (input[index] != '%')
			{
				continue;
			}

			if (index + 2 >= input.Length || !Uri.IsHexDigit(input[index + 1]) || !Uri.IsHexDigit(input[index + 2]))
			{
				return false;
			}

			index += 2;
		}

		return true;
	}

	private static bool IsSafePageId(string id) =>
		id.Length > 0 && id.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
}
