using System.Diagnostics;
using Serilog;

namespace SpicetifyDeck.Bridge;

public sealed record BridgeOperationResult(
	bool Succeeded,
	string? Message,
	BridgeDiagnostics Diagnostics,
	IReadOnlyList<string> Steps)
{
	public static BridgeOperationResult Fail(string message, BridgeDiagnostics diagnostics, IReadOnlyList<string> steps) =>
		new(false, message, diagnostics, steps);
}

public sealed class SpicetifyBridgeService(
	SpicetifyLocator locator,
	SpicetifyCli cli,
	BridgeScriptGenerator generator,
	BridgeCredentials credentials,
	BridgeConnectionManager connections,
	BridgeEndpoint endpoint,
	ILogger logger)
{
		public const string SpicetifyMissingMessage = "Spicetify CLI was not found. Please install Spicetify first.";

		public const string InstalledMessage = "Spicetify Bridge installed successfully. Restart Spotify to load it.";

	private readonly ILogger _logger = logger.ForContext<SpicetifyBridgeService>();
	private readonly BridgeConnectionManager _connections = connections;
	private readonly Lock _gate = new();
	private SpicetifyLocation? _location;
	private DateTimeOffset? _locatedAt;
	private string? _version;
	private string? _lastError;
	private bool _requiresApply;
	private string? _inspection;
	private bool _extensionEnabled;
	private DateTimeOffset? _cacheExpiresAt;

		private static readonly TimeSpan CacheWindow = TimeSpan.FromSeconds(2);

		private static readonly TimeSpan MissingRetry = TimeSpan.FromSeconds(30);

		public SpicetifyLocation Location
	{
		get
		{
			lock (_gate)
			{
				if (_location is { } cached)
				{
					var stillThere = cached.ExecutablePath is { } executable
						? File.Exists(executable)
						: _locatedAt is { } at && DateTimeOffset.UtcNow - at < MissingRetry;

					if (stillThere)
					{
						return cached;
					}
				}

				_location = locator.Locate();
				_locatedAt = DateTimeOffset.UtcNow;
				return _location;
			}
		}
	}

		public string? InstalledFilePath
	{
		get
		{
			var directory = Location.ExtensionsDirectory;
			return directory is null ? null : Path.Combine(directory, BridgeScriptGenerator.FileName);
		}
	}

		public string? Endpoint => endpoint.SocketUrl;

		public string? LastError
	{
		get
		{
			lock (_gate)
			{
				return _lastError;
			}
		}
	}

		public static bool IsSpotifyRunning
	{
		get
		{
			try
			{
				var processes = Process.GetProcessesByName("Spotify");
				try
				{
					return processes.Length > 0;
				}
				finally
				{

					foreach (var process in processes)
					{
						process.Dispose();
					}
				}
			}
			catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception)
			{

				return false;
			}
		}
	}

		private Func<bool> _spotifyRunning = static () => IsSpotifyRunning;

		internal void UseSpotifyRunningProbe(Func<bool> probe) => _spotifyRunning = probe;

		public bool IsExtensionEnabled() => IsExtensionEnabled(Location);

		public string? BridgeFilePath => InstalledFilePath;

		public BridgeDiagnostics Describe()
	{
		var location = Location;
		var now = DateTimeOffset.UtcNow;
		string? version;
		bool requiresApply;
		bool requiresRefresh;

		var applied = InspectAppliedCopy() is null && IsLoadedByClient();

		lock (_gate)
		{
			version = _version;
			requiresApply = _requiresApply;
			requiresRefresh = _cacheExpiresAt is null || now >= _cacheExpiresAt;
		}

		if (requiresRefresh)
		{
			var problem = generator.Inspect(InstalledFilePath);
			var enabled = SpicetifyConfig.IsExtensionEnabled(location.Root);

			lock (_gate)
			{
				_inspection = problem;
				_extensionEnabled = enabled;
				_cacheExpiresAt = now + CacheWindow;
			}
		}

		string? inspection;
		bool extensionEnabled;

		lock (_gate)
		{
			inspection = _inspection;
			extensionEnabled = _extensionEnabled;
		}

		return new BridgeDiagnostics(
			ResolveStatus(inspection, applied),
			version,
			extensionEnabled,
			credentials.ExtensionVersion,
			LastError,
			requiresApply,
			applied);
	}

		public Task<BridgeOperationResult> InstallAsync(CancellationToken cancellationToken) =>
		InstallAsync(rotateToken: false, forceRepair: false, cancellationToken);

		public Task<BridgeOperationResult> RepairAsync(CancellationToken cancellationToken) =>
		InstallAsync(rotateToken: false, forceRepair: true, cancellationToken);

		public Task<BridgeOperationResult> ResetAuthenticationAsync(CancellationToken cancellationToken) =>
		InstallAsync(rotateToken: true, forceRepair: false, cancellationToken);

	private async Task<BridgeOperationResult> InstallAsync(
		bool rotateToken,
		bool forceRepair,
		CancellationToken cancellationToken)
	{
		var steps = new List<string>();
		var location = Location;

		if (!location.IsInstalled)
		{
			return Fail(SpicetifyMissingMessage, steps);
		}

		if (!BridgeScriptGenerator.HasTemplate())
		{
			steps.Add("The bundled bridge script is missing from the plugin.");
			return Fail("The bundled bridge script is missing from the plugin, so the bridge cannot be installed.", steps);
		}

		if (endpoint.SocketUrl is null)
		{
			steps.Add("The bridge listener is not running.");
			return Fail("The bridge listener is not running yet. Try again in a moment.", steps);
		}

		if (!rotateToken && !forceRepair && IsSynchronized(location))
		{
			steps.Add("The bridge file, the Spicetify configuration and the copy in the client all already match this run.");
			_logger.Information("The Spicetify bridge is already installed and synchronized, so nothing was changed.");

			return new BridgeOperationResult(
				true,
				"The Spicetify bridge is already installed and synchronized. Nothing was changed.",
				Describe(),
				steps);
		}
		if (forceRepair)
		{
			steps.Add("Forced repair requested; checking and applying the bridge regardless of synchronization status.");
		}

		if (rotateToken)
		{
			credentials.Reset();
			steps.Add("Generated a new bridge token.");
		}

		var path = generator.Write(location.ExtensionsDirectory);
		if (path is null)
		{
			steps.Add($"Could not write the bridge to {location.ExtensionsDirectory}.");
			return Fail($"The bridge file could not be written to {location.ExtensionsDirectory}.", steps);
		}

		steps.Add($"Wrote {path}.");
		credentials.RecordInstall(endpoint.SocketUrl, BridgeScriptGenerator.TemplateVersion ?? "unknown", location.Root ?? string.Empty);

		var version = await cli.GetVersionAsync(location, cancellationToken).ConfigureAwait(false);
		lock (_gate)
		{
			_version = version;
		}

		var configure = await cli.AddExtensionAsync(location, BridgeScriptGenerator.FileName, cancellationToken).ConfigureAwait(false);
		if (!configure.Succeeded)
		{
			steps.Add($"Configuring the extension failed: {configure.Summary}");
			return Fail(
				$"The bridge file was written, but Spicetify could not be told to load it. {configure.Summary}",
								steps);
		}

		steps.Add("Added the extension to the Spicetify configuration, keeping the existing ones.");

		if (_spotifyRunning())
		{
			steps.Add("Spotify is running, so the change is written but not applied.");
			MarkApplyRequired();

			_logger.Information(
				"The bridge on disk is up to date for {SocketUrl}, but Spotify is running so the update is pending. The bridge Spotify is using is unaffected.",
				endpoint.SocketUrl);

			return Fail(
				"Spotify is running, so the bridge update is pending. The bridge Spotify is using is unaffected and keeps working. "
				+ "Quit Spotify completely and run this action again to apply it.",
				steps);
		}

		var apply = await cli.ApplyAsync(location, cancellationToken).ConfigureAwait(false);
		if (!apply.Succeeded)
		{
			steps.Add($"spicetify apply failed: {apply.Summary}");
			return Fail(
				$"The bridge file was written and configured, but spicetify apply failed. {apply.Summary}",
								steps);
		}

		steps.Add("Applied the Spicetify configuration.");

		var problem = Verify(location);
		if (problem is not null)
		{
			steps.Add($"Verification failed: {problem}");
			return Fail($"The bridge could not be verified. {problem}", steps);
		}

		steps.Add("Verified the file, the placeholders and the Spicetify configuration.");
		MarkApplied();

		var diagnostics = Describe();
		_logger.Information("Spicetify bridge installed and verified. {Steps}", string.Join(" ", steps));

		_logger.Information(
			"The bridge is listening on {SocketUrl}. Spotify must be quit and started again for this to take effect.",
			endpoint.SocketUrl);

		return new BridgeOperationResult(true, InstalledMessage, diagnostics, steps);
	}

		public async Task<BridgeOperationResult> UninstallAsync(CancellationToken cancellationToken)
	{
		var steps = new List<string>();
		var location = Location;

		if (!location.IsInstalled)
		{
			return Fail(SpicetifyMissingMessage, steps);
		}

		var remove = await cli.RemoveExtensionAsync(location, BridgeScriptGenerator.FileName, cancellationToken)
			.ConfigureAwait(false);
		if (!remove.Succeeded)
		{
			steps.Add($"Removing the extension from the configuration failed: {remove.Summary}");
			return Fail($"The extension could not be removed from the Spicetify configuration. {remove.Summary}", steps);
		}

		steps.Add("Removed the extension from the Spicetify configuration, keeping the existing ones.");

		if (!generator.Delete(location.ExtensionsDirectory))
		{
			steps.Add("The bridge file could not be deleted.");
			return Fail("The extension is no longer configured, but the bridge file could not be deleted.", steps);
		}

		steps.Add($"Deleted {InstalledFilePath}.");

		credentials.RecordUninstall();
		credentials.Reset();

		var apply = await cli.ApplyAsync(location, cancellationToken).ConfigureAwait(false);
		if (!apply.Succeeded)
		{
			steps.Add($"spicetify apply failed: {apply.Summary}");
			return Fail(
				$"The bridge was removed, but spicetify apply failed so Spotify may still load it until it restarts. {apply.Summary}",
								steps);
		}

		steps.Add("Applied the Spicetify configuration.");
		MarkApplied();

		return new BridgeOperationResult(true, "Spicetify Bridge uninstalled.", Describe(), steps);
	}

		public string? Verify(SpicetifyLocation? known = null)
	{
		var location = known ?? Location;
		if (!location.IsInstalled)
		{
			return SpicetifyMissingMessage;
		}

		var problem = generator.Inspect(InstalledFilePath);
		if (problem is not null)
		{
			return problem;
		}

		var stale = InspectAppliedCopy();
		if (stale is not null)
		{
			return stale;
		}

		if (!IsLoadedByClient())
		{
			return "The Spotify client is not loading the bridge. The extension is installed and "
				+ "configured, but the client's own page never asks for it, so nothing in Spotify "
				+ "is running the bridge and no connection can be made. Quit Spotify completely and "
				+ "run Repair Spicetify Bridge to apply it.";
		}

		if (!IsExtensionEnabled(location))
		{
			return "Spicetify is not configured to load the bridge extension. Run Install Spicetify Bridge.";
		}

		return null;
	}

		private string? InspectAppliedCopy()
	{
		var applied = AppliedCopyPath;
		if (applied is null || !File.Exists(applied))
		{
			return null;
		}

		if (generator.IsCurrent(applied))
		{
			return null;
		}

		return "Spotify is running an older copy of the bridge, so it presents a token this plugin no longer accepts. "
			+ "Quit Spotify completely and run Repair Spicetify Bridge.";
	}

		public bool IsSynchronized(SpicetifyLocation? known = null)
	{
		var location = known ?? Location;

		if (!location.IsInstalled || InstalledFilePath is not { } path || !File.Exists(path))
		{
			return false;
		}

		if (!generator.IsCurrent(path) || !IsExtensionEnabled(location))
		{
			return false;
		}

		var applied = AppliedCopyPath;
		return (applied is null || !File.Exists(applied) || generator.IsCurrent(applied))
			&& IsLoadedByClient();
	}

		private string? AppliedCopyPath
	{
		get
		{
			foreach (var candidate in _clientFolders ?? SpotifyClientFolders())
			{
				var path = Path.Combine(candidate, "Apps", "xpui", "extensions", BridgeScriptGenerator.FileName);
				if (File.Exists(path))
				{
					return path;
				}
			}

			return null;
		}
	}

		private string? ClientIndexPath
	{
		get
		{
			foreach (var candidate in _clientFolders ?? SpotifyClientFolders())
			{
				var path = Path.Combine(candidate, "Apps", "xpui", "index.html");
				if (File.Exists(path))
				{
					return path;
				}
			}

			return null;
		}
	}

		private bool IsLoadedByClient()
	{
		var page = ClientIndexPath;
		if (page is null)
		{
			return true;
		}

		try
		{

			return File.ReadAllText(page).Contains(
				"extensions/" + BridgeScriptGenerator.FileName,
				StringComparison.OrdinalIgnoreCase);
		}
		catch (IOException)
		{

			return true;
		}
		catch (UnauthorizedAccessException)
		{
			return true;
		}
	}

	private IReadOnlyList<string>? _clientFolders;

		internal void UseClientFolders(IReadOnlyList<string> folders) => _clientFolders = folders;

		internal void UseSpicetifyLocation(SpicetifyLocation location)
	{
		lock (_gate)
		{
			_location = location;
			_locatedAt = DateTimeOffset.UtcNow;
			_inspection = null;
			_cacheExpiresAt = null;
		}
	}

	private static IEnumerable<string> SpotifyClientFolders()
	{
		var roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
		var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

		yield return roaming;
		yield return local;

		var packages = Path.Combine(local, "Packages");
		if (Directory.Exists(packages))
		{
			foreach (var package in Directory.EnumerateDirectories(packages, "*Spotify*"))
			{
				yield return Path.Combine(package, "LocalCache", "Roaming");
			}
		}
	}

		public async Task<(bool Connected, string Message)> TestConnectionAsync(CancellationToken cancellationToken)
	{
		if (!IsExtensionEnabled())
		{
			return (false, "Spicetify is not configured to load the bridge extension.");
		}

		if (!_connections.IsConnected)
		{
			return (false, _connections.HasAuthenticationFailure
				? _connections.AuthenticationFailureReason ?? "Authentication failed."
				: "The bridge is not connected. Start Spotify, and run Repair Spicetify Bridge if it stays that way.");
		}

		var result = await _connections.SendAsync(BridgeCommandKind.Ping, null, TimeSpan.FromSeconds(5), cancellationToken)
			.ConfigureAwait(false);

		return result.Ok
			? (true, "The bridge answered a test request.")
			: (false, result.Error ?? "The bridge did not answer a test request.");
	}

		public Task RestartAsync() => _connections.DropSessionAsync();

		public async Task<BridgeOperationResult?> ReconcileAsync(CancellationToken cancellationToken)
	{
		var location = Location;

		if (!location.IsInstalled
			|| !BridgeScriptGenerator.HasTemplate()
			|| endpoint.SocketUrl is not { } live
			|| InstalledFilePath is not { } path
			|| !File.Exists(path))
		{
			return null;
		}

		if (generator.IsCurrent(path) && IsLoadedByClient())
		{
			return null;
		}

		var fileIsCurrent = generator.IsCurrent(path);

		var steps = new List<string>();
		_logger.Warning(fileIsCurrent
			? "The Spotify client is not loading the bridge, so Spicetify will be re-applied. The bridge is only loaded into the client by an apply."
			: "The installed bridge does not match this run, so it is being rewritten. Spotify is holding an older copy until it is restarted.");

		if (!fileIsCurrent)
		{
			if (generator.Write(location.ExtensionsDirectory) is not { } written)
			{
				steps.Add($"Could not write the bridge to {location.ExtensionsDirectory}.");
				return Fail($"The installed bridge was out of date and could not be rewritten. {SpicetifyMissingMessage}", steps);
			}

			steps.Add($"Rewrote {written} for {live}.");
			credentials.RecordInstall(live, BridgeScriptGenerator.TemplateVersion ?? "unknown", location.Root ?? string.Empty);
		}

		if (_spotifyRunning())
		{
			steps.Add("Spotify is running, so the change was not applied. Applying now would rewrite the client while it is using it.");
			MarkApplyRequired();

			_logger.Information(
				"The bridge is ready for {SocketUrl} but was not applied, because Spotify is running. Quit Spotify and start Macro Deck again, or run Repair Spicetify Bridge with Spotify closed.",
				live);

			return new BridgeOperationResult(
				true,
				fileIsCurrent
					? "The Spotify client is not loading the bridge. Quit Spotify completely, then run Repair Spicetify Bridge to apply it."
					: "The installed bridge was out of date and has been corrected. Quit Spotify completely, then run Repair Spicetify Bridge to apply it.",
				Describe(),
				steps);
		}

		if (!IsExtensionEnabled())
		{
			var configure = await cli.AddExtensionAsync(location, BridgeScriptGenerator.FileName, cancellationToken).ConfigureAwait(false);
			if (!configure.Succeeded)
			{
				steps.Add($"Configuring the extension failed: {configure.Summary}");
				MarkApplyRequired();
				return Fail($"The bridge file was corrected, but Spicetify could not be told to load it. {configure.Summary}", steps);
			}

			steps.Add("Added the extension to the Spicetify configuration.");
		}

		var apply = await cli.ApplyAsync(location, cancellationToken).ConfigureAwait(false);
		if (!apply.Succeeded)
		{
			steps.Add($"spicetify apply failed: {apply.Summary}");
			MarkApplyRequired();
			return Fail($"The bridge file was corrected, but spicetify apply failed. {apply.Summary}", steps);
		}

		steps.Add("Applied the Spicetify configuration.");

		var problem = Verify(location);
		if (problem is not null)
		{
			steps.Add($"Verification failed: {problem}");
			return Fail($"The bridge was corrected but could not be verified. {problem}", steps);
		}

		steps.Add("Verified the file, the placeholders and the Spicetify configuration.");
		MarkApplyRequired();

		_logger.Information(
			"The installed bridge was corrected to {SocketUrl}. Spotify must be quit and started again for it to take effect.",
			live);

		return new BridgeOperationResult(
			true,
			"The installed bridge was out of date and has been corrected. Quit Spotify completely and start it again.",
			Describe(),
			steps);
	}

		public void MarkApplyRequired()
	{
		lock (_gate)
		{
			_requiresApply = true;
		}
	}

		public void Forget()
	{
		lock (_gate)
		{
			_location = null;
			_locatedAt = null;
			_version = null;
			_requiresApply = false;
		}
	}

	private BridgeStatus ResolveStatus(string? inspectionProblem, bool isApplied)
	{
		if (!Location.IsInstalled)
		{
			return BridgeStatus.SpicetifyNotInstalled;
		}

		if (inspectionProblem is not null)
		{

			return File.Exists(InstalledFilePath) ? BridgeStatus.Corrupted : BridgeStatus.BridgeNotInstalled;
		}

		if (_connections.HasAuthenticationFailure)
		{
			return BridgeStatus.AuthenticationFailed;
		}

		if (_connections.IsReporting)
		{

			return BridgeStatus.Connected;
		}

		if (_connections.IsConnected || _connections.HasEverConnected)
		{
			return BridgeStatus.ConnectionLost;
		}

		if (!isApplied || _requiresApply)
		{
			return _spotifyRunning() ? BridgeStatus.UpdatePending : BridgeStatus.WaitingForSpotify;
		}

		return _spotifyRunning() ? BridgeStatus.NotConnected : BridgeStatus.WaitingForSpotify;
	}

		private static bool IsExtensionEnabled(SpicetifyLocation location) =>
		SpicetifyConfig.IsExtensionEnabled(location.Root);

	private void MarkApplied()
	{
		lock (_gate)
		{
			_requiresApply = false;
			_lastError = null;
		}
			_cacheExpiresAt = null;
	}

	private BridgeOperationResult Fail(string message, List<string> steps) => Fail(message, steps, Describe());

	private BridgeOperationResult Fail(string message, IReadOnlyList<string> steps, BridgeDiagnostics diagnostics)
	{
		lock (_gate)
		{
			_lastError = message;
		}

		_logger.Warning("The bridge operation failed: {Message} {Steps}", message, string.Join(" ", steps));
		return new BridgeOperationResult(false, message, diagnostics, steps);
	}
}
