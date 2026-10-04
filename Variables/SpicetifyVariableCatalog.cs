using MacroDeck.Localization;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Variables;
using SpicetifyDeck.State;

namespace SpicetifyDeck.Variables;

public static class SpicetifyVariableCatalog
{
		public const string InstanceId = "spotify";

	public static IReadOnlyList<SpicetifyVariable> All { get; } = Build();

	private static readonly Dictionary<string, SpicetifyVariable> ByLocalId =
		All.ToDictionary(variable => variable.LocalId, StringComparer.OrdinalIgnoreCase);

	public static SpicetifyVariable? Find(string localId) => ByLocalId.GetValueOrDefault(localId);

	public static IReadOnlyList<VariableDefinition> Definitions { get; } =
		All.Select(variable => variable.ToDefinition()).ToArray();

	private static SpicetifyVariable Text(string name, LocalizedText display, LocalizedText description,
		Func<SpotifySnapshot, object?> read, VariableSource source = VariableSource.Stable, string? icon = null,
		bool writable = false) =>
		new(name, VariableType.Text, read, new SpicetifyVariableLabels(display, description, icon),
			Writable: writable, Source: source);

	private static SpicetifyVariable Number(string name, LocalizedText display, LocalizedText description,
		Func<SpotifySnapshot, object?> read, string? unit = null, string? semanticKind = null, int? decimals = null,
		VariableSource source = VariableSource.Stable, bool writable = false, bool commitOnRelease = false) =>
		new(name, VariableType.Numeric, read, new SpicetifyVariableLabels(display, description), Unit: unit,
			SemanticKind: semanticKind, DecimalPlaces: decimals, Writable: writable, CommitOnRelease: commitOnRelease,
			Source: source);

	private static SpicetifyVariable Flag(string name, LocalizedText display, LocalizedText description,
		Func<SpotifySnapshot, object?> read, VariableSource source = VariableSource.Client, bool writable = false) =>
		new(name, VariableType.Boolean, read, new SpicetifyVariableLabels(display, description),
			Writable: writable, Source: source);

	private static string FormatDuration(TimeSpan duration) =>
		$"{(long)duration.TotalMinutes}:{duration.Seconds:00}";

	private static IReadOnlyList<SpicetifyVariable> Build() =>
	[
		// Playback
		Flag("is_playing", Strings.Variables.IsPlaying.Display(), Strings.Variables.IsPlaying.Description(),
			s => s.IsPlayingKnown ? s.IsPlaying : null, VariableSource.Transport),
		Text("playback_state", Strings.Variables.PlaybackState.Display(), Strings.Variables.PlaybackState.Description(),
			s => !s.IsPlayingKnown && !s.IsPausedKnown ? null : s.PlaybackState switch
			{
				PlaybackState.Playing => "Playing",
				PlaybackState.Paused => "Paused",
				_ => "Stopped",
			}, VariableSource.Transport, "play"),
		Text("current_position", Strings.Variables.CurrentPosition.Display(), Strings.Variables.CurrentPosition.Description(),
			s => s.PositionKnown ? FormatDuration(s.CurrentPosition) : null, VariableSource.Transport,
			writable: true),
		Text("track_duration", Strings.Variables.TrackDuration.Display(), Strings.Variables.TrackDuration.Description(),
			s => s.Duration is { } duration ? FormatDuration(duration) : null, VariableSource.Transport),
		Number("progress_seconds", Strings.Variables.ProgressSeconds.Display(), Strings.Variables.ProgressSeconds.Description(),
			s => s.PositionKnown ? Math.Round(s.CurrentPosition.TotalSeconds) : null, "s", decimals: 0,
			source: VariableSource.Transport, writable: true),
		Number("progress_percentage", Strings.Variables.ProgressPercentage.Display(), Strings.Variables.ProgressPercentage.Description(),
			s => SpicetifyValues.Percent(s.ProgressFraction), "%", VariableSemanticKinds.Percentage, 1,
			VariableSource.Transport, writable: true),

		Text("track_name", Strings.Variables.TrackName.Display(), Strings.Variables.TrackName.Description(),
			s => s.TrackName, VariableSource.Transport),
		Text("artist_name", Strings.Variables.ArtistName.Display(), Strings.Variables.ArtistName.Description(),
			s => s.ArtistName, VariableSource.Transport),
		Text("album_name", Strings.Variables.AlbumName.Display(), Strings.Variables.AlbumName.Description(),
			s => s.AlbumName, VariableSource.Transport),
		Text("album_artist", Strings.Variables.AlbumArtist.Display(), Strings.Variables.AlbumArtist.Description(),
			s => s.AlbumArtistName, VariableSource.Transport),
		Text("playlist_name", Strings.Variables.PlaylistName.Display(), Strings.Variables.PlaylistName.Description(),
			s => s.ContextName, VariableSource.Transport),
		Text("track_uri", Strings.Variables.TrackUri.Display(), Strings.Variables.TrackUri.Description(),
			s => s.TrackUri),
		Text("album_uri", Strings.Variables.AlbumUri.Display(), Strings.Variables.AlbumUri.Description(),
			s => s.AlbumUri),
		Text("playlist_uri", Strings.Variables.PlaylistUri.Display(), Strings.Variables.PlaylistUri.Description(),
			s => s.ContextUri, VariableSource.Transport),
		Text("queue_next_track_name", Strings.Variables.QueueNextTrackName.Display(), Strings.Variables.QueueNextTrackName.Description(),
			s => s.QueueNextTracks is { Count: > 0 } tracks ? tracks[0].Name : null, VariableSource.Transport),
		Text("queue_next_track_uri", Strings.Variables.QueueNextTrackUri.Display(), Strings.Variables.QueueNextTrackUri.Description(),
			s => s.QueueNextTracks is { Count: > 0 } tracks ? tracks[0].Uri : null, VariableSource.Transport),
		Text("queue_previous_track_name", Strings.Variables.QueuePreviousTrackName.Display(), Strings.Variables.QueuePreviousTrackName.Description(),
			s => s.QueuePreviousTracks is { Count: > 0 } tracks ? tracks[^1].Name : null, VariableSource.Transport),
		Text("queue_previous_track_uri", Strings.Variables.QueuePreviousTrackUri.Display(), Strings.Variables.QueuePreviousTrackUri.Description(),
			s => s.QueuePreviousTracks is { Count: > 0 } tracks ? tracks[^1].Uri : null, VariableSource.Transport),
		Text("artist_uri", Strings.Variables.ArtistUri.Display(), Strings.Variables.ArtistUri.Description(),
			s => s.ArtistUri),
		Text("current_track_id", Strings.Variables.CurrentTrackId.Display(), Strings.Variables.CurrentTrackId.Description(),
			s => s.TrackId),

		// Playback settings
		Number("volume", Strings.Variables.Volume.Display(), Strings.Variables.Volume.Description(),
			s => s.VolumePercent, "%", VariableSemanticKinds.Percentage, 0, VariableSource.Transport, writable: true),
		Flag("shuffle", Strings.Variables.Shuffle.Display(), Strings.Variables.Shuffle.Description(),
			s => s.ShuffleEnabledKnown ? s.ShuffleEnabled : null, VariableSource.Transport, writable: true),
		Text("repeat_mode", Strings.Variables.RepeatMode.Display(), Strings.Variables.RepeatMode.Description(),
			s => !s.RepeatModeKnown ? null : s.RepeatMode switch
			{
				RepeatMode.Track => "Track",
				RepeatMode.Context => "Context",
				_ => "Off",
			}, VariableSource.Transport, writable: true),
		Flag("muted", Strings.Variables.Muted.Display(), Strings.Variables.Muted.Description(),
			s => s.Muted ?? (s.VolumePercent is { } volume ? volume == 0 : null), VariableSource.Transport, writable: true),

		Number("track_number", Strings.Variables.TrackNumber.Display(), Strings.Variables.TrackNumber.Description(),
			s => s.TrackNumber, source: VariableSource.Stable),
		Number("disc_number", Strings.Variables.DiscNumber.Display(), Strings.Variables.DiscNumber.Description(),
			s => s.DiscNumber, source: VariableSource.Stable),
		Flag("explicit", Strings.Variables.Explicit.Display(), Strings.Variables.Explicit.Description(),
			s => s.Explicit, VariableSource.Stable),
		Flag("track_liked", Strings.Variables.TrackLiked.Display(), Strings.Variables.TrackLiked.Description(),
			s => s.IsLiked, VariableSource.Transport),

		// Connection
		Text("connection_status", Strings.Variables.ConnectionStatus.Display(), Strings.Variables.ConnectionStatus.Description(),
			s => s.ConnectionStatus, source: VariableSource.Client),
	];
}
