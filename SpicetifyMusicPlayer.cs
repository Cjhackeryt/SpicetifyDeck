using MacroDeck.Sdk.MusicPlayer;
using Serilog;
using SpicetifyDeck.Artwork;
using SpicetifyDeck.Bridge;
using SpicetifyDeck.State;

namespace SpicetifyDeck;

public sealed class SpicetifyMusicPlayer(
	SpotifyStateManager state,
	ArtworkFetcher artwork,
	BridgeConnectionManager bridge,
	ILogger? logger = null) : IMusicPlayer
{
	private readonly ILogger _log = (logger ?? Log.Logger).ForContext<SpicetifyMusicPlayer>();

	public Task<MusicPlayerState> GetStateAsync(CancellationToken cancellationToken = default)
	{
		var snapshot = state.Current;

		if (!state.IsConnected)
		{

			return Task.FromResult(new MusicPlayerState
			{
				IsConnected = false,
				IsUnavailable = true,
				StatusMessage = "The Spicetify bridge is not connected to Spotify.",
				PlaybackState = snapshot.PlaybackState,
				TrackName = snapshot.TrackName,
				Artists = snapshot.ArtistName is { } artist ? [artist] : [],
				AlbumName = snapshot.AlbumName,
				ArtworkId = snapshot.ArtworkUrl,
				Position = snapshot.CurrentPosition,
				Duration = snapshot.Duration,
				VolumePercent = snapshot.VolumePercent,
				ShuffleEnabled = snapshot.ShuffleEnabled,
				RepeatMode = snapshot.RepeatMode,
				DeviceName = snapshot.DeviceName,
				DeviceType = snapshot.ClientPlatform,
			});
		}

		return Task.FromResult(new MusicPlayerState
		{
			IsConnected = true,
			PlaybackState = snapshot.PlaybackState,
			TrackName = snapshot.TrackName,
			Artists = snapshot.ArtistName is { } name ? [name] : [],
			AlbumName = snapshot.AlbumName,
			ArtworkId = snapshot.ArtworkUrl,
			Position = snapshot.CurrentPosition,
			Duration = snapshot.Duration,
			VolumePercent = snapshot.VolumePercent,
			ShuffleEnabled = snapshot.ShuffleEnabled,
			RepeatMode = snapshot.RepeatMode,
			DeviceName = snapshot.DeviceName,
			DeviceType = snapshot.ClientPlatform,
		});
	}

	public Task<MusicPlayerArtwork?> GetArtworkAsync(string artworkId, CancellationToken cancellationToken = default) =>
		FetchArtworkAsync(artworkId, 300, cancellationToken);

	public Task PlayAsync(CancellationToken cancellationToken = default) => RunAsync(BridgeCommandKind.Play, null, cancellationToken);

	public Task PauseAsync(CancellationToken cancellationToken = default) => RunAsync(BridgeCommandKind.Pause, null, cancellationToken);

	public Task TogglePlayPauseAsync(CancellationToken cancellationToken = default) =>
		RunAsync(BridgeCommandKind.TogglePlay, null, cancellationToken);

	public Task NextAsync(CancellationToken cancellationToken = default) => RunAsync(BridgeCommandKind.Next, null, cancellationToken);

	public Task PreviousAsync(CancellationToken cancellationToken = default) => RunAsync(BridgeCommandKind.Previous, null, cancellationToken);

	public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default) =>
		RunAsync(BridgeCommandKind.SeekTo, Payload("positionMs", position.TotalMilliseconds), cancellationToken);

	public Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default) =>
		RunAsync(BridgeCommandKind.SetVolume, Payload("volumePercent", Math.Clamp(volumePercent, 0, 100)), cancellationToken);

	public Task SetShuffleAsync(bool enabled, CancellationToken cancellationToken = default) =>
		RunAsync(BridgeCommandKind.SetShuffle, Payload("enabled", enabled), cancellationToken);

	public Task SetRepeatModeAsync(RepeatMode mode, CancellationToken cancellationToken = default) =>
		RunAsync(BridgeCommandKind.SetRepeat, Payload("mode", RepeatValue(mode)), cancellationToken);

	private async Task<MusicPlayerArtwork?> FetchArtworkAsync(string artworkId, int size, CancellationToken cancellationToken)
	{

		var bytes = await artwork.FetchAsync(artworkId, size, cancellationToken).ConfigureAwait(false);
		return bytes is null ? null : new MusicPlayerArtwork(bytes, "image/jpeg");
	}

	private async Task RunAsync(string kind, IReadOnlyDictionary<string, object?>? payload, CancellationToken cancellationToken)
	{
		var result = await bridge.SendAsync(kind, payload, null, cancellationToken).ConfigureAwait(false);
		if (result.Ok)
		{
			return;
		}

		_log.Debug("Music player command {Kind} failed: {Reason}", kind, result.Error);
		throw new BridgeCommandException(result.Error ?? "The Spotify client could not run that command.");
	}

	private static Dictionary<string, object?> Payload(string key, object value) =>
		new(StringComparer.Ordinal) { [key] = value };

	private static string RepeatValue(RepeatMode mode) => mode switch
	{
		RepeatMode.Track => "track",
		RepeatMode.Context => "context",
		_ => "off",
	};
}

public sealed class BridgeCommandException(string message) : Exception(message);
