using System.Collections.Immutable;
using MacroDeck.Sdk.MusicPlayer;
using Serilog;
using SpicetifyDeck.Bridge;
using SpicetifyDeck.State;

namespace SpicetifyDeck.State;

public sealed class SpotifyStateManager : IDisposable
{
		private static readonly string[] TrackFields =
	[
		nameof(SpotifySnapshot.TrackUri),
		nameof(SpotifySnapshot.TrackId),
		nameof(SpotifySnapshot.TrackName),
		nameof(SpotifySnapshot.ArtistName),
		nameof(SpotifySnapshot.AlbumArtistName),
		nameof(SpotifySnapshot.AlbumName),
		nameof(SpotifySnapshot.AlbumUri),
		nameof(SpotifySnapshot.ArtistUri),
		nameof(SpotifySnapshot.ArtworkUrl),
		nameof(SpotifySnapshot.TrackNumber),
		nameof(SpotifySnapshot.DiscNumber),
		nameof(SpotifySnapshot.ReleaseDate),
		nameof(SpotifySnapshot.Explicit),
	];

		private static readonly string[] AllFields =
	[
		nameof(SpotifySnapshot.IsPlaying),
		nameof(SpotifySnapshot.IsPaused),
		nameof(SpotifySnapshot.IsBuffering),
		nameof(SpotifySnapshot.Position),
		nameof(SpotifySnapshot.Duration),
		nameof(SpotifySnapshot.ReportedFraction),
		nameof(SpotifySnapshot.VolumePercent),
		nameof(SpotifySnapshot.Muted),
		nameof(SpotifySnapshot.ShuffleEnabled),
		nameof(SpotifySnapshot.RepeatMode),
		nameof(SpotifySnapshot.TrackUri),
		nameof(SpotifySnapshot.TrackId),
		nameof(SpotifySnapshot.TrackName),
		nameof(SpotifySnapshot.ArtistName),
		nameof(SpotifySnapshot.AlbumArtistName),
		nameof(SpotifySnapshot.AlbumName),
		nameof(SpotifySnapshot.AlbumUri),
		nameof(SpotifySnapshot.ArtistUri),
		nameof(SpotifySnapshot.ArtworkUrl),
		nameof(SpotifySnapshot.TrackNumber),
		nameof(SpotifySnapshot.DiscNumber),
		nameof(SpotifySnapshot.ReleaseDate),
		nameof(SpotifySnapshot.Explicit),
		nameof(SpotifySnapshot.ContextUri),
		nameof(SpotifySnapshot.ContextName),
		nameof(SpotifySnapshot.ContextType),
		nameof(SpotifySnapshot.ClientPlatform),
		nameof(SpotifySnapshot.DeviceName),
		nameof(SpotifySnapshot.Capabilities),
	];

	private readonly BridgeConnectionManager _connections;
	private readonly PluginSettings _settings;
	private readonly ILogger _logger;
	private readonly Lock _sync = new();
	private readonly Dictionary<string, FieldValue> _fields = new(StringComparer.Ordinal);
	private readonly Dictionary<string, object?> _pendingTrackFields = new(StringComparer.Ordinal);
	private SpotifySnapshot _snapshot = new();
	private string? _lastTrackUri;
	private string? _pendingTrackUri;
	private int _disposed;

	public SpotifyStateManager(BridgeConnectionManager connections, PluginSettings settings, ILogger logger)
	{
		_connections = connections;
		_settings = settings;
		_logger = logger.ForContext<SpotifyStateManager>();
		_connections.StateReceived += OnStateAsync;
		_connections.ConnectionChanged += OnConnectionChanged;
	}

		public event Func<StateChange, CancellationToken, Task>? Changed;

		public SpotifySnapshot Current
	{
		get
		{
			lock (_sync)
			{
				return Stamp(Expire(_snapshot, DateTimeOffset.UtcNow));
			}
		}
	}

	private SpotifySnapshot Stamp(SpotifySnapshot snapshot)
	{
		var connectionState = _connections.ConnectionState;
		var connected = connectionState == BridgeConnectionState.Connected;

		return snapshot.BridgeConnected == connected && snapshot.ConnectionState == connectionState
			? snapshot
			: snapshot with { BridgeConnected = connected, ConnectionState = connectionState };
	}

		public bool IsConnected => _connections.IsReporting;

		public DateTimeOffset? LastUpdate
	{
		get
		{
			lock (_sync)
			{
				return _snapshot.CapturedAt == default ? null : _snapshot.CapturedAt;
			}
		}
	}

		public string? LastError { get; private set; }

		public ValueTask<SpotifySnapshot> GetAsync(CancellationToken cancellationToken = default) =>
		ValueTask.FromResult(Current);

		public void RecordError(string message)
	{
		LastError = message;
		_logger.Debug("Spicetify state: {Reason}", message);
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		_connections.StateReceived -= OnStateAsync;
		_connections.ConnectionChanged -= OnConnectionChanged;
	}

		private async Task OnStateAsync(BridgeState state, CancellationToken cancellationToken)
	{
		var change = Accept(state);
		if (change is null)
		{
			return;
		}

		var handler = Changed;
		if (handler is null)
		{
			return;
		}

		try
		{
			await handler(change, cancellationToken).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception)
		{

			_logger.Debug(exception, "A Spotify state listener failed.");
		}
	}

		private void NotifyWithoutWaiting(StateChange? change)
	{
		if (change is null || Changed is null)
		{
			return;
		}

		_ = NotifyAsync(change, CancellationToken.None);
	}

	private async Task NotifyAsync(StateChange change, CancellationToken cancellationToken)
	{
		var handler = Changed;
		if (handler is null)
		{
			return;
		}

		try
		{
			await handler(change, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception exception)
		{

			_logger.Debug(exception, "A Spotify state listener failed.");
		}
	}

		internal StateChange? Accept(BridgeState state)
	{
		lock (_sync)
		{
			var snapshot = Merge(state);
			var changedFields = Diff(_snapshot, snapshot);

			_snapshot = snapshot;
			return changedFields.IsEmpty ? null : new StateChange(snapshot, changedFields);
		}
	}

		internal void Connected(bool connected) => OnConnectionChanged(connected);

		internal void ForceStoredConnection(bool connected)
	{
		lock (_sync)
		{
			_snapshot = _snapshot with { BridgeConnected = connected };
		}
	}

		private void OnConnectionChanged(bool connected)
	{
		StateChange? change;

		lock (_sync)
		{
			if (connected)
			{
				LastError = null;
			}

			var next = _snapshot with
			{
				BridgeConnected = _connections.ConnectionState == BridgeConnectionState.Connected,
				ConnectionState = _connections.ConnectionState,
			};

			var changedFields = Diff(_snapshot, next);
			_snapshot = next;
			change = changedFields.IsEmpty ? null : new StateChange(next, changedFields);
		}

		NotifyWithoutWaiting(change);
	}

		private SpotifySnapshot Merge(BridgeState state)
	{
		var now = DateTimeOffset.UtcNow;
		var fresh = FromReport(state, now);
		var reportedUri = NormalizeText(fresh.GetValueOrDefault(nameof(SpotifySnapshot.TrackUri)) as string);
		var trackChanged = reportedUri is not null
			&& !string.Equals(reportedUri, _lastTrackUri, StringComparison.Ordinal);

		if (trackChanged)
		{
			_lastTrackUri = reportedUri;
			_pendingTrackUri = reportedUri;
			_pendingTrackFields.Clear();
		}

		if (_pendingTrackUri is not null && string.Equals(reportedUri, _pendingTrackUri, StringComparison.Ordinal))
		{
			foreach (var field in TrackFields)
			{
				var value = NormalizeValue(field, fresh.GetValueOrDefault(field));
				if (value is not null)
				{
					_pendingTrackFields[field] = value;
				}
			}
		}

		var trackCommitted = _pendingTrackUri is not null
			&& _pendingTrackFields.ContainsKey(nameof(SpotifySnapshot.TrackName));
		var committedTrackFields = trackCommitted
			? new Dictionary<string, object?>(_pendingTrackFields, StringComparer.Ordinal)
			: null;
		if (trackCommitted)
		{
			var nextName = _pendingTrackFields.GetValueOrDefault(nameof(SpotifySnapshot.TrackName));
			if (!Equals(_snapshot.TrackName, nextName))
			{
				_logger.Debug(
					"Variable {Variable} updated: {OldValue} -> {NewValue}",
					nameof(SpotifySnapshot.TrackName),
					_snapshot.TrackName ?? "Unavailable",
					nextName ?? "Unavailable");
			}

			foreach (var field in TrackFields)
			{
				_fields.Remove(field);
			}

			foreach (var field in TrackFields)
			{
				var value = _pendingTrackFields.GetValueOrDefault(field);
				var cache = new FieldValue();
				if (value is not null)
				{
					cache.SetValid(value, now);
				}

				_fields[field] = cache;
			}

			_pendingTrackUri = null;
			_pendingTrackFields.Clear();
		}

		var merged = new Dictionary<string, object?>(StringComparer.Ordinal);

		foreach (var (field, value) in fresh)
		{
			if (TrackFields.Contains(field, StringComparer.Ordinal))
			{
				if (trackCommitted)
				{
					merged[field] = committedTrackFields!.GetValueOrDefault(field);
				}
				else if (_pendingTrackUri is not null)
				{
					if (_fields.TryGetValue(field, out var retained))
					{
						retained.MarkInvalid();
					}

					merged[field] = LastValid(field, now);
				}
				else
				{
					merged[field] = MergeField(field, value, now);
				}

				continue;
			}

			merged[field] = MergeField(field, value, now);
		}

		foreach (var field in AllFields)
		{
			if (!merged.ContainsKey(field))
			{
				merged[field] = LastValid(field, now);
			}
		}

		return Apply(_snapshot, merged) with
		{
			BridgeConnected = _connections.ConnectionState == BridgeConnectionState.Connected,
			ConnectionState = _connections.ConnectionState,
		};
	}

	private object? MergeField(string field, object? value, DateTimeOffset now)
	{
		if (!_fields.TryGetValue(field, out var cache))
		{
			_fields[field] = cache = new FieldValue();
		}

		var valid = NormalizeValue(field, value);
		if (valid is not null)
		{
			if (cache.LastValidValue is not null && !Equals(cache.LastValidValue, valid))
			{
				_logger.Debug("Variable {Variable} updated: {OldValue} -> {NewValue}", field, cache.LastValidValue, valid);
			}

			cache.SetValid(valid, now);
			return valid;
		}

		if (!cache.InvalidLogged)
		{
			_logger.Debug(
				"Variable {Variable} ignored invalid value {Value}; keeping {LastValidValue}",
				field,
				value ?? "null",
				cache.LastValidValue ?? "Unavailable");
			cache.InvalidLogged = true;
		}

		cache.MarkInvalid();
		return LastValid(field, now);
	}

	private object? LastValid(string field, DateTimeOffset now)
	{
		if (!_fields.TryGetValue(field, out var cache) || cache.LastValidValue is null)
		{
			return null;
		}

		if (cache.IsCurrentlyValid && (!_connections.HasEverConnected || _connections.IsReporting))
		{
			return cache.CurrentValue;
		}

		if (cache.LastValidAt is not { } lastValidAt
			|| now - lastValidAt >= _settings.UnavailableTimeout)
		{
			return null;
		}

		return cache.LastValidValue;
	}

	private SpotifySnapshot Expire(SpotifySnapshot snapshot, DateTimeOffset now)
	{
		if (_fields.Count == 0)
		{
			return snapshot;
		}

		var current = _fields.Keys.ToDictionary(field => field, field => LastValid(field, now), StringComparer.Ordinal);
		return Apply(snapshot, current) with { CapturedAt = snapshot.CapturedAt };
	}

	private static object? NormalizeValue(string field, object? value)
	{
		if (value is string text)
		{
			return NormalizeText(text);
		}

		return field switch
		{
			nameof(SpotifySnapshot.Position) or nameof(SpotifySnapshot.Duration) =>
				value is TimeSpan duration && duration >= TimeSpan.Zero ? duration : null,
			nameof(SpotifySnapshot.ReportedFraction) =>
				value is double fraction && double.IsFinite(fraction) && fraction is >= 0 and <= 1 ? fraction : null,
			nameof(SpotifySnapshot.VolumePercent) =>
				value is int volume && volume is >= 0 and <= 100 ? volume : null,
			nameof(SpotifySnapshot.TrackNumber) or nameof(SpotifySnapshot.DiscNumber) =>
				value is int number && number > 0 ? number : null,
			_ => value,
		};
	}

	private static string? NormalizeText(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}

		var normalized = value.Trim();
		return normalized.Equals("Unavailable", StringComparison.OrdinalIgnoreCase)
			|| normalized.Equals("N/A", StringComparison.OrdinalIgnoreCase)
			|| normalized.Equals("undefined", StringComparison.OrdinalIgnoreCase)
			|| normalized.Equals("null", StringComparison.OrdinalIgnoreCase)
			? null
			: value;
	}

	private sealed class FieldValue
	{
		public object? CurrentValue { get; private set; }

		public object? LastValidValue { get; private set; }

		public DateTimeOffset? LastValidAt { get; private set; }

		public bool IsCurrentlyValid { get; private set; }

		public bool InvalidLogged { get; set; }

		public void SetValid(object value, DateTimeOffset timestamp)
		{
			CurrentValue = value;
			LastValidValue = value;
			LastValidAt = timestamp;
			IsCurrentlyValid = true;
			InvalidLogged = false;
		}

		public void MarkInvalid()
		{
			CurrentValue = null;
			IsCurrentlyValid = false;
		}
	}

		private static Dictionary<string, object?> FromReport(BridgeState state, DateTimeOffset now)
	{
		var fields = new Dictionary<string, object?>(StringComparer.Ordinal);

		if (state.Playback is { } playback)
		{
			fields[nameof(SpotifySnapshot.IsPlaying)] = playback.IsPlaying;
			fields[nameof(SpotifySnapshot.IsPaused)] = playback.IsPaused;
			fields[nameof(SpotifySnapshot.IsBuffering)] = playback.IsBuffering;
			fields[nameof(SpotifySnapshot.Position)] = playback.PositionMs is { } position && position >= 0
				? TimeSpan.FromMilliseconds(position)
				: null;
			fields[nameof(SpotifySnapshot.Duration)] = playback.DurationMs is { } duration && duration >= 0
				? TimeSpan.FromMilliseconds(duration)
				: null;

			fields[nameof(SpotifySnapshot.ReportedFraction)] = playback.ProgressFraction;
		}

		if (state.Controls is { } controls)
		{
			fields[nameof(SpotifySnapshot.VolumePercent)] = controls.VolumePercent;
			fields[nameof(SpotifySnapshot.Muted)] = controls.Muted;
			fields[nameof(SpotifySnapshot.ShuffleEnabled)] = controls.Shuffle;
			fields[nameof(SpotifySnapshot.RepeatMode)] = ParseRepeat(controls.Repeat);
		}

		if (state.Track is { } track)
		{
			fields[nameof(SpotifySnapshot.TrackUri)] = track.Uri;
			fields[nameof(SpotifySnapshot.TrackId)] = IdOf(track.Uri, "track");
			fields[nameof(SpotifySnapshot.TrackName)] = track.Name;
			fields[nameof(SpotifySnapshot.ArtistName)] = track.ArtistName;
			fields[nameof(SpotifySnapshot.AlbumArtistName)] = track.AlbumArtistName;
			fields[nameof(SpotifySnapshot.AlbumName)] = track.AlbumTitle;
			fields[nameof(SpotifySnapshot.AlbumUri)] = track.AlbumUri;
			fields[nameof(SpotifySnapshot.ArtistUri)] = track.ArtistUri;
			fields[nameof(SpotifySnapshot.ArtworkUrl)] = track.ImageUrl;
			fields[nameof(SpotifySnapshot.TrackNumber)] = track.TrackNumber;
			fields[nameof(SpotifySnapshot.DiscNumber)] = track.DiscNumber;
			fields[nameof(SpotifySnapshot.ReleaseDate)] = track.ReleaseDate;
			fields[nameof(SpotifySnapshot.Explicit)] = track.Explicit;
		}

		if (state.Context is { } context)
		{
			fields[nameof(SpotifySnapshot.ContextUri)] = context.Uri;
			fields[nameof(SpotifySnapshot.ContextName)] = context.Name;
			fields[nameof(SpotifySnapshot.ContextType)] = context.Type;
		}

		if (state.Client is { } client)
		{
			fields[nameof(SpotifySnapshot.ClientPlatform)] = client.Platform;
			fields[nameof(SpotifySnapshot.DeviceName)] = client.DeviceName;
		}

		fields[nameof(SpotifySnapshot.Capabilities)] = state.Capabilities;

		return fields;
	}

		private static SpotifySnapshot Apply(SpotifySnapshot previous, Dictionary<string, object?> merged)
	{
		object? Get(string field) => merged.GetValueOrDefault(field);

		var isPlaying = Get(nameof(SpotifySnapshot.IsPlaying));
		var isPaused = Get(nameof(SpotifySnapshot.IsPaused));
		var position = Get(nameof(SpotifySnapshot.Position)) as TimeSpan?;
		var shuffle = Get(nameof(SpotifySnapshot.ShuffleEnabled));
		var repeat = Get(nameof(SpotifySnapshot.RepeatMode)) as RepeatMode?;

		return new SpotifySnapshot
		{
			CapturedAt = DateTimeOffset.UtcNow,
			IsPlaying = isPlaying is true,
			IsPlayingKnown = isPlaying is not null,
			IsPaused = isPaused is true,
			IsPausedKnown = isPaused is not null,
			IsBuffering = AsBool(Get(nameof(SpotifySnapshot.IsBuffering))),
			Position = position ?? TimeSpan.Zero,
			PositionKnown = position is not null,
			Duration = Get(nameof(SpotifySnapshot.Duration)) as TimeSpan?,
			ReportedFraction = AsDouble(Get(nameof(SpotifySnapshot.ReportedFraction))),
			VolumePercent = AsInt(Get(nameof(SpotifySnapshot.VolumePercent))),
			Muted = AsBool(Get(nameof(SpotifySnapshot.Muted))),
			ShuffleEnabled = shuffle is true,
			ShuffleEnabledKnown = shuffle is not null,
			RepeatMode = repeat ?? RepeatMode.Off,
			RepeatModeKnown = repeat is not null,

			TrackUri = AsString(Get(nameof(SpotifySnapshot.TrackUri))),
			TrackId = AsString(Get(nameof(SpotifySnapshot.TrackId))),
			TrackName = AsString(Get(nameof(SpotifySnapshot.TrackName))),
			ArtistName = AsString(Get(nameof(SpotifySnapshot.ArtistName))),
			AlbumArtistName = AsString(Get(nameof(SpotifySnapshot.AlbumArtistName))),
			AlbumName = AsString(Get(nameof(SpotifySnapshot.AlbumName))),
			AlbumUri = AsString(Get(nameof(SpotifySnapshot.AlbumUri))),
			ArtistUri = AsString(Get(nameof(SpotifySnapshot.ArtistUri))),
			ArtworkUrl = AsString(Get(nameof(SpotifySnapshot.ArtworkUrl))),
			TrackNumber = AsInt(Get(nameof(SpotifySnapshot.TrackNumber))),
			DiscNumber = AsInt(Get(nameof(SpotifySnapshot.DiscNumber))),
			ReleaseDate = AsString(Get(nameof(SpotifySnapshot.ReleaseDate))),
			Explicit = AsBool(Get(nameof(SpotifySnapshot.Explicit))),

			ContextUri = AsString(Get(nameof(SpotifySnapshot.ContextUri))),
			ContextName = AsString(Get(nameof(SpotifySnapshot.ContextName))),
			ContextType = AsString(Get(nameof(SpotifySnapshot.ContextType))),

			ClientPlatform = AsString(Get(nameof(SpotifySnapshot.ClientPlatform))),
			DeviceName = AsString(Get(nameof(SpotifySnapshot.DeviceName))),

			Capabilities = Get(nameof(SpotifySnapshot.Capabilities)) as BridgeCapabilities ?? previous.Capabilities,
		};
	}

		private static ImmutableArray<string> Diff(SpotifySnapshot previous, SpotifySnapshot next)
	{
		var builder = ImmutableArray.CreateBuilder<string>();

		void Compare<T>(string name, T? before, T? after) where T : struct
		{
			if (!before.HasValue && !after.HasValue)
			{
				return;
			}

			if (before.HasValue != after.HasValue || !EqualityComparer<T>.Default.Equals(before!.Value, after!.Value))
			{
				builder.Add(name);
			}
		}

		void CompareText(string name, string? before, string? after)
		{
			if (!string.Equals(before, after, StringComparison.Ordinal))
			{
				builder.Add(name);
			}
		}

		Compare(nameof(SpotifySnapshot.IsPlaying), (bool?)previous.IsPlaying, next.IsPlaying);
		Compare(nameof(SpotifySnapshot.IsPaused), (bool?)previous.IsPaused, next.IsPaused);
		Compare(nameof(SpotifySnapshot.IsBuffering), previous.IsBuffering, next.IsBuffering);
		Compare(nameof(SpotifySnapshot.Duration), previous.Duration, next.Duration);
		Compare(nameof(SpotifySnapshot.VolumePercent), previous.VolumePercent, next.VolumePercent);
		Compare(nameof(SpotifySnapshot.Muted), previous.Muted, next.Muted);
		Compare(nameof(SpotifySnapshot.ShuffleEnabled), (bool?)previous.ShuffleEnabled, next.ShuffleEnabled);
		Compare(nameof(SpotifySnapshot.RepeatMode), (RepeatMode?)previous.RepeatMode, (RepeatMode?)next.RepeatMode);

		CompareText(nameof(SpotifySnapshot.TrackUri), previous.TrackUri, next.TrackUri);
		CompareText(nameof(SpotifySnapshot.TrackId), previous.TrackId, next.TrackId);
		CompareText(nameof(SpotifySnapshot.TrackName), previous.TrackName, next.TrackName);
		CompareText(nameof(SpotifySnapshot.ArtistName), previous.ArtistName, next.ArtistName);
		CompareText(nameof(SpotifySnapshot.AlbumArtistName), previous.AlbumArtistName, next.AlbumArtistName);
		CompareText(nameof(SpotifySnapshot.AlbumName), previous.AlbumName, next.AlbumName);
		CompareText(nameof(SpotifySnapshot.AlbumUri), previous.AlbumUri, next.AlbumUri);
		CompareText(nameof(SpotifySnapshot.ArtistUri), previous.ArtistUri, next.ArtistUri);
		CompareText(nameof(SpotifySnapshot.ArtworkUrl), previous.ArtworkUrl, next.ArtworkUrl);
		CompareText(nameof(SpotifySnapshot.ReleaseDate), previous.ReleaseDate, next.ReleaseDate);
		Compare(nameof(SpotifySnapshot.TrackNumber), previous.TrackNumber, next.TrackNumber);
		Compare(nameof(SpotifySnapshot.DiscNumber), previous.DiscNumber, next.DiscNumber);
		Compare(nameof(SpotifySnapshot.Explicit), previous.Explicit, next.Explicit);

		CompareText(nameof(SpotifySnapshot.ContextUri), previous.ContextUri, next.ContextUri);
		CompareText(nameof(SpotifySnapshot.ContextName), previous.ContextName, next.ContextName);
		CompareText(nameof(SpotifySnapshot.ContextType), previous.ContextType, next.ContextType);

		CompareText(nameof(SpotifySnapshot.ClientPlatform), previous.ClientPlatform, next.ClientPlatform);
		CompareText(nameof(SpotifySnapshot.DeviceName), previous.DeviceName, next.DeviceName);

		Compare(nameof(SpotifySnapshot.BridgeConnected), (bool?)previous.BridgeConnected, next.BridgeConnected);
		Compare(nameof(SpotifySnapshot.ConnectionState), (BridgeConnectionState?)previous.ConnectionState, next.ConnectionState);

		return builder.ToImmutable();
	}

	private static RepeatMode? ParseRepeat(string? value) => value switch
	{
		"track" => RepeatMode.Track,
		"context" => RepeatMode.Context,
		"off" => RepeatMode.Off,
		_ => null,
	};

		private static string? IdOf(string? uri, string kind)
	{
		if (string.IsNullOrWhiteSpace(uri))
		{
			return null;
		}

		var parts = uri.Split(':');
		return parts.Length >= 3 && parts[^2].Equals(kind, StringComparison.OrdinalIgnoreCase) ? parts[^1] : null;
	}

	private static string? AsString(object? value) => value as string;

	private static int? AsInt(object? value) => value switch
	{
		int number => number,
		long number => (int)number,
		double number => (int)number,
		_ => null,
	};

	private static double? AsDouble(object? value) => value switch
	{
		double number => number,
		float number => number,
		int number => number,
		long number => number,
		_ => null,
	};

	private static bool? AsBool(object? value) => value as bool?;
}

public sealed record StateChange(SpotifySnapshot Snapshot, ImmutableArray<string> Fields)
{
		public bool TrackChanged => Fields.Any(name =>
		name is nameof(SpotifySnapshot.TrackUri) or nameof(SpotifySnapshot.TrackName));
}
