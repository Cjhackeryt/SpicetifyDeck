using Serilog;

namespace SpicetifyDeck.Bridge;

public sealed record SpicetifyLocation(string? ExecutablePath, string? Root)
{
	public bool IsInstalled => ExecutablePath is not null;

		public string? ExtensionsDirectory => Root is null ? null : Path.Combine(Root, "Extensions");

		public string? ConfigPath => Root is null ? null : Path.Combine(Root, "config-xpui.ini");
}

public sealed class SpicetifyLocator(ILogger logger)
{
		public const string ExecutableName = "spicetify";

		public const string ConfigFileName = "config-xpui.ini";

	private readonly ILogger _logger = logger.ForContext<SpicetifyLocator>();

		public SpicetifyLocation Locate()
	{
		var executable = FindExecutable();
		if (executable is null)
		{
			_logger.Information("The Spicetify CLI was not found, so the bridge cannot be installed.");
			return new SpicetifyLocation(null, null);
		}

		var root = FindRoot(executable);
		_logger.Information("Spicetify found at {Executable}, using the root {Root}.", executable, root ?? "(none)");

		return new SpicetifyLocation(executable, root);
	}

		private static string? FindExecutable()
	{
		var extensions = OperatingSystem.IsWindows()
			? new[] { ".exe", ".cmd", ".bat" }
			: new[] { string.Empty };

		foreach (var directory in PathDirectories())
		{
			foreach (var extension in extensions)
			{
				var candidate = Path.Combine(directory, ExecutableName + extension);
				if (File.Exists(candidate))
				{
					return candidate;
				}
			}
		}

		var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
		var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
		var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");

		var known = new[]
		{
			Path.Combine(local, "spicetify"),
			Path.Combine(roaming, "spicetify"),
			Path.Combine(home, ".spicetify"),
			Path.Combine(home, "scoop", "shims"),
			Path.Combine(local, "Microsoft", "WinGet", "Links"),
			Path.Combine(programFiles, "spicetify"),
			Path.Combine(programFilesX86, "spicetify"),
			Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "chocolatey", "bin"),
			Path.Combine(home, ".local", "bin"),
			Path.Combine(home, ".spicetify"),
			"/usr/local/bin",
			"/usr/bin",
			"/opt/homebrew/bin",
			"/usr/local/opt/spicetify-cli/bin",
		}
		.Where(Directory.Exists)
		.Distinct(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
		.ToArray();

		if (!string.IsNullOrWhiteSpace(configHome))
		{
			known = [Path.Combine(configHome, "spicetify"), .. known];
		}

		foreach (var directory in known)
		{
			foreach (var extension in extensions)
			{
				var candidate = Path.Combine(directory, ExecutableName + extension);
				if (File.Exists(candidate))
				{
					return candidate;
				}
			}
		}

		return null;
	}

		private static string? FindRoot(string executable)
	{
		var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
		var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");

		var candidates = new List<string>
		{
			Path.GetDirectoryName(executable)!,
			Path.Combine(roaming, "spicetify"),
			Path.Combine(local, "spicetify"),
			Path.Combine(home, ".spicetify"),
			Path.Combine(home, ".config", "spicetify"),
		};
		if (!string.IsNullOrWhiteSpace(configHome))
		{
			candidates.Insert(1, Path.Combine(configHome, "spicetify"));
		}

		var seen = new HashSet<string>(OperatingSystem.IsWindows()
			? StringComparer.OrdinalIgnoreCase
			: StringComparer.Ordinal);
		var unique = candidates.Where(path => seen.Add(path)).ToArray();

		var configured = unique.FirstOrDefault(path => File.Exists(Path.Combine(path, ConfigFileName)));
		if (configured is not null)
		{
			return configured;
		}

		var withExtensions = unique.FirstOrDefault(Directory.Exists);
		return withExtensions ?? (OperatingSystem.IsWindows()
			? Path.Combine(roaming, "spicetify")
			: Path.Combine(configHome ?? Path.Combine(home, ".config"), "spicetify"));
	}

	private static IEnumerable<string> PathDirectories()
	{
		var path = Environment.GetEnvironmentVariable("PATH");
		if (string.IsNullOrEmpty(path))
		{
			yield break;
		}

		foreach (var entry in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{

			if (entry.Length > 0 && entry.IndexOfAny(Path.GetInvalidPathChars()) < 0)
			{
				yield return entry.Trim('"');
			}
		}
	}
}
