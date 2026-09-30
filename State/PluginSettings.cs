using System.Text.Json;

namespace SpicetifyDeck.State;

public sealed class PluginSettings
{
		public const string FileName = "spicetifydeck.settings.json";

	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
	{
		WriteIndented = true,
	};

	public PluginSettings()
	{
		DataDirectory = Environment.GetEnvironmentVariable("MACRO_DECK_PLUGIN_DATA_DIRECTORY")
			?? Path.Combine(AppContext.BaseDirectory, "data");

		UnavailableTimeout = ReadTimeSpan("SPICETIFYDECK_UNAVAILABLE_TIMEOUT", TimeSpan.FromSeconds(30));
		ProgressRefreshInterval = ReadTimeSpan("SPICETIFYDECK_PROGRESS_INTERVAL", TimeSpan.FromSeconds(1));
		CommandTimeout = ReadTimeSpan("SPICETIFYDECK_COMMAND_TIMEOUT", TimeSpan.FromSeconds(10));
		BridgePort = ReadPort();
	}

		public string DataDirectory { get; }

		public TimeSpan UnavailableTimeout { get; set; }

		public TimeSpan ProgressRefreshInterval { get; private set; }

		public TimeSpan CommandTimeout { get; set; }


		public int BridgePort { get; set; } = Bridge.BridgeEndpoint.DefaultPort;

		public string SettingsPath => Path.Combine(DataDirectory, FileName);

		public void Save()
	{
		try
		{
			Directory.CreateDirectory(DataDirectory);
			var document = new SettingsDocument
			{
				UnavailableTimeoutSeconds = (int)UnavailableTimeout.TotalSeconds,
				ProgressRefreshIntervalMs = (int)ProgressRefreshInterval.TotalMilliseconds,
				CommandTimeoutSeconds = (int)CommandTimeout.TotalSeconds,
			};

			File.WriteAllText(SettingsPath, JsonSerializer.Serialize(document, Json));
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
		{

		}
	}

		public void Load()
	{
		SettingsDocument? document;
		try
		{
			if (!File.Exists(SettingsPath))
			{
				return;
			}

			document = JsonSerializer.Deserialize<SettingsDocument>(File.ReadAllText(SettingsPath), Json);
		}
		catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
		{

			return;
		}

		if (document is null)
		{
			return;
		}

		if (document.UnavailableTimeoutSeconds > 0)
		{
			UnavailableTimeout = TimeSpan.FromSeconds(document.UnavailableTimeoutSeconds);
		}

		if (document.ProgressRefreshIntervalMs > 0)
		{
			ProgressRefreshInterval = TimeSpan.FromMilliseconds(document.ProgressRefreshIntervalMs);
		}

		if (document.CommandTimeoutSeconds > 0)
		{
			CommandTimeout = TimeSpan.FromSeconds(document.CommandTimeoutSeconds);
		}

	}

		private static int ReadPort()
	{
		var raw = Environment.GetEnvironmentVariable("SPICETIFYDECK_BRIDGE_PORT");
		return int.TryParse(raw, out var port) && port is > 0 and < 65536
			? port
			: Bridge.BridgeEndpoint.DefaultPort;
	}

	private static TimeSpan ReadTimeSpan(string variable, TimeSpan fallback)
	{
		var raw = Environment.GetEnvironmentVariable(variable);
		if (string.IsNullOrWhiteSpace(raw))
		{
			return fallback;
		}

		return int.TryParse(raw, out var seconds) && seconds > 0
			? TimeSpan.FromSeconds(seconds)
			: TimeSpan.TryParse(raw, out var parsed) && parsed > TimeSpan.Zero
				? parsed
				: fallback;
	}

	private sealed class SettingsDocument
	{
		public int UnavailableTimeoutSeconds { get; set; }

		public int ProgressRefreshIntervalMs { get; set; }

		public int CommandTimeoutSeconds { get; set; }

	}
}
