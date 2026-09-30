namespace SpicetifyDeck.Bridge;

public static class BridgeCommandKind
{
	// Transport
	public const string Play = "play";
	public const string Pause = "pause";
	public const string TogglePlay = "togglePlay";
	public const string Next = "next";
	public const string Previous = "previous";
	public const string RestartTrack = "restartTrack";

	// Seeking
	public const string SeekTo = "seekTo";
	public const string SkipForward = "skipForward";
	public const string SkipBackward = "skipBackward";

	// Volume and modes
	public const string SetVolume = "setVolume";
	public const string SetMute = "setMute";
	public const string ToggleMute = "toggleMute";
	public const string SetShuffle = "setShuffle";
	public const string SetRepeat = "setRepeat";

	// Queue
	public const string AddToQueue = "addToQueue";
	public const string RemoveFromQueue = "removeFromQueue";
	public const string ClearQueue = "clearQueue";

	// Library
	public const string SetLiked = "setLiked";
	public const string ToggleLiked = "toggleLiked";
	public const string PlayUri = "playUri";
	public const string OpenUri = "openUri";

	// Diagnostics
	public const string Ping = "ping";
}

public sealed record BridgeCommandResult
{
	public bool Ok { get; init; }

	public string? Value { get; init; }

	public string? Error { get; init; }

	public static BridgeCommandResult Success(string? value = null) => new() { Ok = true, Value = value };

	public static BridgeCommandResult Failure(string error) => new() { Ok = false, Error = error };

		public static BridgeCommandResult TimedOut() =>
		new() { Ok = false, Error = "The Spotify client did not acknowledge the command in time." };

		public static BridgeCommandResult Cancelled() => new() { Ok = false, Error = "The command was cancelled." };

		public static BridgeCommandResult Offline() =>
		new() { Ok = false, Error = "The Spicetify bridge is not connected to a running Spotify client." };

		public static BridgeCommandResult Unsupported(string what) =>
		new() { Ok = false, Error = $"This Spotify version does not expose {what} to the bridge." };
}
