using System.Text.RegularExpressions;
using Serilog;

namespace SpicetifyDeck.Bridge;

public sealed partial class BridgeScriptGenerator(BridgeEndpoint endpoint, BridgeCredentials credentials, ILogger logger)
{
		public const string EndpointPlaceholder = "__SPICETIFYDECK_ENDPOINT__";

		public const string TokenPlaceholder = "__SPICETIFYDECK_TOKEN__";

		public const string FileName = "spicetifydeck-bridge.js";

		public const string TemplateDirectory = "Bridge";

		[GeneratedRegex(@"__SPICETIFYDECK_[A-Z_]+__")]
	private static partial Regex Placeholders { get; }

		public string? Endpoint => endpoint.SocketUrl;

		public string Render()
	{
		var template = ReadTemplate();
		var socketUrl = endpoint.SocketUrl ?? "ws://127.0.0.1:0/spicetify/bridge/socket";

		return template
			.Replace(EndpointPlaceholder, socketUrl, StringComparison.Ordinal)
			.Replace(TokenPlaceholder, credentials.Token, StringComparison.Ordinal);
	}

		public static bool HasTemplate()
	{
		var template = ReadTemplate();
		return template.Length > 0
			&& template.Contains(EndpointPlaceholder, StringComparison.Ordinal)
			&& template.Contains(TokenPlaceholder, StringComparison.Ordinal);
	}

		public static string? TemplateVersion
	{
		get
		{
			var match = VersionPattern().Match(ReadTemplate());
			var version = match.Groups["version"].Value;
			return version.Length > 0 ? version : null;
		}
	}

		public string? Inspect(string? path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return "The bridge file was not found.";
		}

		if (!File.Exists(path))
		{
			return "The bridge file was not found.";
		}

		string content;
		try
		{
			content = File.ReadAllText(path);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return "The bridge file could not be read.";
		}

		if (content.Length == 0)
		{
			return "The bridge file is empty.";
		}

		var leftover = Placeholders.Match(content);
		if (leftover.Success)
		{
			return $"The bridge file still contains {leftover.Value}.";
		}

		if (!content.Contains(credentials.Token, StringComparison.Ordinal))
		{

			return "The bridge file carries a token from an earlier SpicetifyDeck run.";
		}

		if (!content.Contains(endpoint.SocketUrl ?? "\u0000", StringComparison.Ordinal))
		{
			return "The bridge file does not point at this Macro Deck instance.";
		}

		return null;
	}

		public bool IsCurrent(string? path)
	{
		if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
		{
			return false;
		}

		try
		{
			return string.Equals(File.ReadAllText(path), Render(), StringComparison.Ordinal);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{

			logger.Debug(exception, "Could not read the installed bridge to compare it.");
			return false;
		}
	}

		public string? Write(string? extensionsDirectory)
	{
		if (string.IsNullOrWhiteSpace(extensionsDirectory))
		{
			return null;
		}

		try
		{
			Directory.CreateDirectory(extensionsDirectory);

			var path = Path.Combine(extensionsDirectory, FileName);
			var script = Render();

			if (!File.Exists(path) || !string.Equals(File.ReadAllText(path), script, StringComparison.Ordinal))
			{
				File.WriteAllText(path, script);
				logger.Information("Wrote the Spicetify bridge extension to {Path}.", path);
			}

			return path;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
		{
			logger.Warning(exception, "Could not write the bridge extension to {Directory}.", extensionsDirectory);
			return null;
		}
	}

		public bool Delete(string? extensionsDirectory)
	{
		if (string.IsNullOrWhiteSpace(extensionsDirectory))
		{
			return false;
		}

		try
		{
			var path = Path.Combine(extensionsDirectory, FileName);
			if (!File.Exists(path))
			{
				return true;
			}

			File.Delete(path);
			logger.Information("Removed the Spicetify bridge extension at {Path}.", path);
			return true;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
		{
			logger.Warning(exception, "Could not remove the bridge extension from {Directory}.", extensionsDirectory);
			return false;
		}
	}

		private static string ReadTemplate()
	{
		foreach (var directory in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
		{
			var path = Path.Combine(directory, TemplateDirectory, FileName);
			if (File.Exists(path))
			{
				return File.ReadAllText(path);
			}
		}

		return string.Empty;
	}

	[GeneratedRegex(@"VERSION\s*=\s*""(?<version>[^""]+)""")]
	private static partial Regex VersionPattern();
}
