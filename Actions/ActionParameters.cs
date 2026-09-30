using System.Text.Json;

namespace SpicetifyDeck.Actions;

public static class ActionParameters
{
	public static string? ReadString(IReadOnlyDictionary<string, object> values, string name) =>
		values.TryGetValue(name, out var value) && value is not null ? value.ToString() : null;

	public static bool ReadBoolean(IReadOnlyDictionary<string, object> values, string name, bool fallback)
	{
		var raw = ReadString(values, name);
		return raw is not null && bool.TryParse(raw, out var parsed) ? parsed : fallback;
	}

	public static double ReadDouble(IReadOnlyDictionary<string, object> values, string name, double fallback)
	{
		if (!values.TryGetValue(name, out var value) || value is null)
		{
			return fallback;
		}

		return value switch
		{
			double d => double.IsFinite(d) ? d : fallback,
			float f => float.IsFinite(f) ? f : fallback,
			int i => i,
			long l => l,
			decimal m => (double)m,
			JsonElement { ValueKind: JsonValueKind.Number } element =>
				element.TryGetDouble(out var parsed) && double.IsFinite(parsed) ? parsed : fallback,
			_ => double.TryParse(value.ToString(), System.Globalization.NumberStyles.Any,
				System.Globalization.CultureInfo.InvariantCulture, out var text)
				? text
				: fallback,
		};
	}

	public static int ReadInt(IReadOnlyDictionary<string, object> values, string name, int fallback) =>
		(int)Math.Round(ReadDouble(values, name, fallback));
}

public static class SpotifyUris
{
		public static string? ForKind(string? input, string kind)
	{
		var value = input?.Trim();
		if (string.IsNullOrEmpty(value))
		{
			return null;
		}

		if (value.StartsWith("spotify:", StringComparison.OrdinalIgnoreCase))
		{
			return value;
		}

		var id = IdOf(value, kind);
		return id is null ? null : $"spotify:{kind}:{id}";
	}

		public static string? Normalize(string? input)
	{
		var value = input?.Trim();
		return string.IsNullOrEmpty(value) ? null : value;
	}

		private static string? IdOf(string value, string kind)
	{
		if (!value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
		{
			return LooksLikeId(value) ? value : null;
		}

		var withoutQuery = value.Split('#')[0].Split('?')[0];
		var segments = withoutQuery.Split('/', StringSplitOptions.RemoveEmptyEntries);

		var query = value.Contains('?', StringComparison.Ordinal) ? value.Split('?')[1] : string.Empty;
		var fromQuery = query
			.Split('&', StringSplitOptions.RemoveEmptyEntries)
			.Select(pair => pair.Split('=', 2))
			.FirstOrDefault(pair => pair.Length == 2 && pair[0].Equals("id", StringComparison.OrdinalIgnoreCase))?[1];

		if (!string.IsNullOrEmpty(fromQuery))
		{
			return fromQuery;
		}

		if (segments.Length >= 2 && segments[^2].Equals(kind, StringComparison.OrdinalIgnoreCase))
		{
			return segments[^1];
		}

		return segments.Length > 0 && LooksLikeId(segments[^1]) ? segments[^1] : null;
	}

		public static string? KindOf(string? uri)
	{
		if (string.IsNullOrWhiteSpace(uri))
		{
			return null;
		}

		var parts = uri.Split(':');
		return parts.Length >= 2 ? parts[^2].ToLowerInvariant() : null;
	}

	private static bool LooksLikeId(string value) =>
		value.Length is > 10 and <= 128 &&
		value.All(character => char.IsLetterOrDigit(character) || character is '-' or '_');
}
