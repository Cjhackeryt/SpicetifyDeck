using System.Text;
using System.Text.RegularExpressions;

namespace SpicetifyDeck.Bridge;

public static partial class SpicetifyConfig
{
		[GeneratedRegex(
		@"^(?<key>\s*extensions)(?<spacer>\s*)=[ ]*(?<value>.*)$",
		RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.CultureInvariant)]
	private static partial Regex ExtensionsLine { get; }

		public static IReadOnlyList<string> ParseList(string value) =>
		value.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
			.Where(entry => !entry.StartsWith("spicetify v", StringComparison.OrdinalIgnoreCase))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToArray();

		public static IReadOnlyList<string> ReadExtensions(string? root)
	{
		if (string.IsNullOrWhiteSpace(root))
		{
			return [];
		}

		try
		{
			var path = Path.Combine(root, SpicetifyLocator.ConfigFileName);
			if (!File.Exists(path))
			{
				return [];
			}

			var match = ExtensionsLine.Match(File.ReadAllText(path));
			if (!match.Success)
			{
				return [];
			}

			return ParseList(match.Groups["value"].Value);
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return [];
		}
	}

		public static bool IsExtensionEnabled(string? root) =>
		ReadExtensions(root).Contains(BridgeScriptGenerator.FileName, StringComparer.OrdinalIgnoreCase);

		public static bool RemoveExtension(string? root, string fileName)
	{
		if (string.IsNullOrWhiteSpace(root))
		{
			return false;
		}

		var path = Path.Combine(root, SpicetifyLocator.ConfigFileName);

		try
		{
			if (!File.Exists(path))
			{
				return false;
			}

			var content = File.ReadAllText(path);
			var remaining = ReadExtensions(root)
				.Where(entry => !entry.Equals(fileName, StringComparison.OrdinalIgnoreCase))
				.ToArray();

			var match = ExtensionsLine.Match(content);
			if (!match.Success)
			{

				return false;
			}

			var replacement = $"{match.Groups["key"].Value}{match.Groups["spacer"].Value}= {string.Join('|', remaining)}";
			var updated = ExtensionsLine.Replace(content, replacement, 1);

			File.WriteAllText(path, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
			return true;
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{
			return false;
		}
	}
}
