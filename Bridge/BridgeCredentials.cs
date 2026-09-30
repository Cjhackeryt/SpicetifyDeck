using System.Security.Cryptography;
using System.Text.Json;
using Serilog;

namespace SpicetifyDeck.Bridge;

public sealed class BridgeCredentials
{
		public const string FileName = "bridge.json";

	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
	{
		WriteIndented = true,
	};

	private readonly ILogger _logger;
	private readonly string _path;
	private readonly Lock _gate = new();
	private string _token;

		public string? InstalledEndpoint => Endpoint;

	private BridgeCredentials(string path, string token, BridgeRecord? record, ILogger logger)
	{
		_path = path;
		_token = token;
		_logger = logger;
		Endpoint = record?.Endpoint;
		InstalledAtUtc = record?.InstalledAtUtc;
		ExtensionVersion = record?.ExtensionVersion;
		SpicetifyRoot = record?.SpicetifyRoot;
		RememberedPort = record?.Port;
	}

		public string Token
	{
		get
		{
			lock (_gate)
			{
				return _token;
			}
		}
	}

		public string? Endpoint { get; private set; }

		public DateTimeOffset? InstalledAtUtc { get; private set; }

		public string? ExtensionVersion { get; private set; }

		public string? SpicetifyRoot { get; private set; }

		public bool HasStoredToken { get; private init; }

		public int? RememberedPort { get; private set; }

		public bool RememberPort(int port)
	{
		lock (_gate)
		{
			if (RememberedPort == port)
			{
				return false;
			}

			RememberedPort = port;
		}

		Persist();
		return true;
	}

		public static BridgeCredentials Load(string dataDirectory, ILogger logger)
	{
		var path = Path.Combine(dataDirectory, FileName);
		BridgeRecord? record = null;

		try
		{
			if (File.Exists(path))
			{
				record = JsonSerializer.Deserialize<BridgeRecord>(File.ReadAllText(path), Json);
			}
		}
		catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
		{
			logger.Warning(exception, "The bridge credentials file could not be read, so a new token was generated.");
		}

		var token = record?.Token;
		if (!IsUsable(token))
		{
			var credentials = new BridgeCredentials(path, Create(), null, logger) { HasStoredToken = false };
			credentials.Persist();
			return credentials;
		}

		return new BridgeCredentials(path, token!, record, logger) { HasStoredToken = true };
	}

		public void Reset()
	{
		lock (_gate)
		{
			_token = Create();
			Endpoint = null;
			InstalledAtUtc = null;
			ExtensionVersion = null;
		}

		Persist();
	}

		public void RecordInstall(string endpoint, string extensionVersion, string spicetifyRoot)
	{
		lock (_gate)
		{
			Endpoint = endpoint;
			InstalledAtUtc = DateTimeOffset.UtcNow;
			ExtensionVersion = extensionVersion;
			SpicetifyRoot = spicetifyRoot;
		}

		Persist();
	}

		public void RecordUninstall()
	{
		lock (_gate)
		{
			Endpoint = null;
			InstalledAtUtc = null;
			ExtensionVersion = null;
			SpicetifyRoot = null;
		}

		Persist();
	}

		public bool EndpointChanged(string currentEndpoint) =>
		!string.IsNullOrEmpty(Endpoint) &&
		!string.Equals(Endpoint, currentEndpoint, StringComparison.Ordinal);

		private void Persist()
	{
		try
		{
			Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
			var document = new BridgeRecord
			{
				Token = Token,
				Endpoint = Endpoint,
				InstalledAtUtc = InstalledAtUtc,
				ExtensionVersion = ExtensionVersion,
				SpicetifyRoot = SpicetifyRoot,
				Port = RememberedPort,
			};

			File.WriteAllText(_path, JsonSerializer.Serialize(document, Json));
		}
		catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException)
		{

			_logger.Warning(exception, "The bridge token could not be saved, so it will change on the next start.");
		}
	}

		private static string Create() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();

		private static bool IsUsable(string? token) =>
		token is { Length: 64 } && token.All(Uri.IsHexDigit);

	private sealed class BridgeRecord
	{
		public string? Token { get; set; }

		public string? Endpoint { get; set; }

		public DateTimeOffset? InstalledAtUtc { get; set; }

		public string? ExtensionVersion { get; set; }

		public string? SpicetifyRoot { get; set; }

		public int? Port { get; set; }
	}
}
