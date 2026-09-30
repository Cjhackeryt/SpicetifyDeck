using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpicetifyDeck.Bridge;

public static class BridgeMessageKind
{
	public const string Hello = "hello";
	public const string State = "state";
	public const string Commands = "commands";
	public const string Welcome = "welcome";
	public const string AuthFailed = "auth-failed";
}

public sealed record BridgeHello
{
		[JsonPropertyName("type")]
	public string Type { get; init; } = BridgeMessageKind.Hello;

	[JsonPropertyName("token")]
	public string? Token { get; init; }

	[JsonPropertyName("bridgeVersion")]
	public string? BridgeVersion { get; init; }
}

public sealed record BridgeAuthFailed
{
	[JsonPropertyName("type")]
	public string Type { get; init; } = BridgeMessageKind.AuthFailed;

	[JsonPropertyName("reason")]
	public string? Reason { get; init; }
}

public sealed record BridgeWelcome
{
	[JsonPropertyName("type")]
	public string Type { get; init; } = BridgeMessageKind.Welcome;

	[JsonPropertyName("sessionId")]
	public string SessionId { get; init; } = string.Empty;

	[JsonPropertyName("unavailableTimeoutSeconds")]
	public int UnavailableTimeoutSeconds { get; init; }

	[JsonPropertyName("stateIntervalMs")]
	public int StateIntervalMs { get; init; }
}

public sealed record BridgeStateMessage
{
	[JsonPropertyName("type")]
	public string Type { get; init; } = BridgeMessageKind.State;

	[JsonPropertyName("state")]
	public BridgeState? State { get; init; }

		[JsonPropertyName("results")]
	public Dictionary<string, BridgeCommandOutcome>? Results { get; init; }
}

public sealed record BridgeCommandOutcome
{
	[JsonPropertyName("ok")]
	public bool Ok { get; init; }

	[JsonPropertyName("error")]
	public string? Error { get; init; }
}

public sealed record BridgeCommandsMessage
{
	[JsonPropertyName("type")]
	public string Type { get; init; } = BridgeMessageKind.Commands;

	[JsonPropertyName("commands")]
	public IReadOnlyList<BridgeCommand> Commands { get; init; } = Array.Empty<BridgeCommand>();
}

public sealed record BridgeCommand
{
	[JsonPropertyName("id")]
	public required string Id { get; init; }

	[JsonPropertyName("kind")]
	public required string Kind { get; init; }

	[JsonPropertyName("payload")]
	public IReadOnlyDictionary<string, object?>? Payload { get; init; }
}

public sealed record BridgeState
{
		[JsonPropertyName("playback")]
	public BridgePlayback? Playback { get; init; }

		[JsonPropertyName("track")]
	public BridgeTrack? Track { get; init; }

		[JsonPropertyName("context")]
	public BridgeContext? Context { get; init; }

		[JsonPropertyName("controls")]
	public BridgeControls? Controls { get; init; }

		[JsonPropertyName("capabilities")]
	public BridgeCapabilities? Capabilities { get; init; }

		[JsonPropertyName("client")]
	public BridgeClient? Client { get; init; }
}

public sealed record BridgePlayback
{
	[JsonPropertyName("isPlaying")]
	public bool? IsPlaying { get; init; }

		[JsonPropertyName("isPaused")]
	public bool? IsPaused { get; init; }

	[JsonPropertyName("positionMs")]
	public long? PositionMs { get; init; }

		[JsonPropertyName("progressFraction")]
	public double? ProgressFraction { get; init; }

	[JsonPropertyName("durationMs")]
	public long? DurationMs { get; init; }

	[JsonPropertyName("isBuffering")]
	public bool? IsBuffering { get; init; }
}

public sealed record BridgeControls
{
	[JsonPropertyName("volumePercent")]
	public int? VolumePercent { get; init; }

	[JsonPropertyName("muted")]
	public bool? Muted { get; init; }

	[JsonPropertyName("shuffle")]
	public bool? Shuffle { get; init; }

	[JsonPropertyName("repeat")]
	public string? Repeat { get; init; }
}

public sealed record BridgeTrack
{
	[JsonPropertyName("uri")]
	public string? Uri { get; init; }

	[JsonPropertyName("uid")]
	public string? Uid { get; init; }

	[JsonPropertyName("name")]
	public string? Name { get; init; }

	[JsonPropertyName("artistName")]
	public string? ArtistName { get; init; }

	[JsonPropertyName("albumArtistName")]
	public string? AlbumArtistName { get; init; }

	[JsonPropertyName("albumTitle")]
	public string? AlbumTitle { get; init; }

	[JsonPropertyName("albumUri")]
	public string? AlbumUri { get; init; }

	[JsonPropertyName("artistUri")]
	public string? ArtistUri { get; init; }

		[JsonPropertyName("imageUrl")]
	public string? ImageUrl { get; init; }

	[JsonPropertyName("durationMs")]
	public long? DurationMs { get; init; }

	[JsonPropertyName("trackNumber")]
	public int? TrackNumber { get; init; }

	[JsonPropertyName("discNumber")]
	public int? DiscNumber { get; init; }

	[JsonPropertyName("releaseDate")]
	public string? ReleaseDate { get; init; }

	[JsonPropertyName("explicit")]
	public bool? Explicit { get; init; }
}

public sealed record BridgeContext
{
	[JsonPropertyName("uri")]
	public string? Uri { get; init; }

	[JsonPropertyName("name")]
	public string? Name { get; init; }

	[JsonPropertyName("type")]
	public string? Type { get; init; }
}

public sealed record BridgeClient
{
	[JsonPropertyName("platform")]
	public string? Platform { get; init; }

		[JsonPropertyName("deviceName")]
	public string? DeviceName { get; init; }
}

public sealed record BridgeCapabilities
{
	[JsonPropertyName("player")]
	public bool Player { get; init; }

	[JsonPropertyName("playerApi")]
	public bool PlayerApi { get; init; }

	[JsonPropertyName("cosmos")]
	public bool Cosmos { get; init; }

	[JsonPropertyName("history")]
	public bool History { get; init; }

	[JsonPropertyName("queue")]
	public bool Queue { get; init; }

	[JsonPropertyName("removeFromQueue")]
	public bool RemoveFromQueue { get; init; }

	[JsonPropertyName("clearQueue")]
	public bool ClearQueue { get; init; }

}

internal static class BridgeJson
{
	public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
		PropertyNameCaseInsensitive = true,
	};
}
