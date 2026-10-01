using MacroDeck.Sdk.MusicPlayer;
using SpicetifyDeck.Bridge;

namespace SpicetifyDeck.State;

public sealed record SpotifySnapshot
{
	public DateTimeOffset CapturedAt { get; init; }

	// Transport
	public bool IsPlaying { get; init; }
	public bool IsPaused { get; init; }
	public bool IsPlayingKnown { get; init; }
	public bool IsPausedKnown { get; init; }
	public bool? IsBuffering { get; init; }
	public TimeSpan Position { get; init; }
	public bool PositionKnown { get; init; }
	public TimeSpan? Duration { get; init; }
	public int? VolumePercent { get; init; }
	public bool? Muted { get; init; }
	public bool ShuffleEnabled { get; init; }
	public bool ShuffleEnabledKnown { get; init; }
	public RepeatMode RepeatMode { get; init; } = RepeatMode.Off;
	public bool RepeatModeKnown { get; init; }

	public string? TrackUri { get; init; }
	public string? TrackId { get; init; }
	public string? TrackName { get; init; }
	public string? ArtistName { get; init; }
	public string? AlbumArtistName { get; init; }
	public string? AlbumName { get; init; }
	public string? AlbumUri { get; init; }
	public string? ArtistUri { get; init; }
	public string? ArtworkUrl { get; init; }
	public int? TrackNumber { get; init; }
	public int? DiscNumber { get; init; }
	public string? ReleaseDate { get; init; }
	public bool? Explicit { get; init; }
	public bool? IsLiked { get; init; }

	// Context
	public string? ContextUri { get; init; }
	public string? ContextName { get; init; }
	public string? ContextType { get; init; }

	public string? ClientPlatform { get; init; }
	public string? DeviceName { get; init; }

		public BridgeCapabilities Capabilities { get; init; } = new();

		public bool BridgeConnected { get; init; }

	public BridgeConnectionState ConnectionState { get; init; } = BridgeConnectionState.Disconnected;

		public string ConnectionStatus => ConnectionState.ToString();

		public string ClientStateText
	{
		get
		{
			if (!BridgeConnected)
			{
				return ConnectionState == BridgeConnectionState.Connecting
					? "Waiting for Spicetify..."
					: ConnectionState.ToString();
			}

			if (!IsPlayingKnown && !IsPausedKnown)
			{
				return "Waiting for Spicetify...";
			}

			if (IsBuffering == true)
			{
				return "Buffering";
			}

			return PlaybackState switch
			{
				PlaybackState.Playing => "Playing",
				PlaybackState.Paused => "Paused",
				_ => "Stopped",
			};
		}
	}

	public PlaybackState PlaybackState =>
		IsPlaying ? PlaybackState.Playing : IsPaused ? PlaybackState.Paused : PlaybackState.Stopped;

		public double? ProgressFraction
	{
		get
		{

			if (ReportedFraction is { } reported)
			{
				return Math.Clamp(reported, 0d, 1d);
			}

			if (!PositionKnown || Duration is not { } duration || duration <= TimeSpan.Zero)
			{
				return null;
			}

			if (Position <= TimeSpan.Zero)
			{
				return 0d;
			}

			return Position >= duration ? 1d : Position.TotalSeconds / duration.TotalSeconds;
		}
	}

		public double? ReportedFraction { get; init; }

		public TimeSpan? Remaining
	{
		get
		{
			if (!PositionKnown || Duration is not { } duration)
			{
				return null;
			}

			var remaining = duration - Position;
			return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
		}
	}

		public TimeSpan CurrentPosition
	{
		get
		{
			if (!IsPlaying)
			{
				return Position < TimeSpan.Zero ? TimeSpan.Zero : Position;
			}

			var elapsed = DateTimeOffset.UtcNow - CapturedAt;
			if (elapsed <= TimeSpan.Zero)
			{
				return Position;
			}

			var advanced = Position + elapsed;
			return Duration is { } duration && advanced > duration ? duration : advanced;
		}
	}
}
