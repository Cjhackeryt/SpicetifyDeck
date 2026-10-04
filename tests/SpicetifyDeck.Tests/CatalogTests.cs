using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Variables;
using Serilog;
using SpicetifyDeck.Actions;
using SpicetifyDeck.Bridge;
using SpicetifyDeck.State;
using SpicetifyDeck.Variables;
using Xunit;

namespace SpicetifyDeck.Tests;

public sealed class CatalogTests
{
	[Fact]
	public void EveryVariableHasAUniqueLocalId()
	{
		var ids = SpicetifyVariableCatalog.All.Select(variable => variable.LocalId).ToArray();

		Assert.Equal(ids.Length, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
	}

	[Fact]
	public void LocalIdsUseTheHyphenatedFormTheHostExpects()
	{
		foreach (var variable in SpicetifyVariableCatalog.All)
		{
			Assert.Matches("^[a-z][a-z0-9]*(?:-[a-z0-9]+)*$", variable.LocalId);
		}
	}

	[Fact]
	public void EveryVariableDeclaresDisplayTextAndADescription()
	{
		foreach (var variable in SpicetifyVariableCatalog.All)
		{
			Assert.False(variable.Labels.Display.IsEmpty, $"{variable.Name} has no display name.");
			Assert.False(variable.Labels.Description.IsEmpty, $"{variable.Name} has no description.");
		}
	}

	[Fact]
	public void EveryVariableProducesItsDeclaredDefinition()
	{
		foreach (var variable in SpicetifyVariableCatalog.All)
		{
			var definition = variable.ToDefinition();

			Assert.Equal(variable.LocalId, definition.Id);
			Assert.Equal(variable.Type, definition.Type);
			Assert.Equal(variable.Writable, definition.CanWrite);
		}
	}

		[Fact]
	public void EveryVariableCanBeReadFromAnEmptySnapshot()
	{
		var snapshot = new SpotifySnapshot();

		foreach (var variable in SpicetifyVariableCatalog.All)
		{
			var value = variable.Read(snapshot);
			Assert.True(value is null or string or double or bool,
				$"{variable.Name} produced {value?.GetType().Name ?? "null"}, which is not a supported variable type.");
		}
	}

	[Fact]
	public void NoReaderInventsTheUnavailableMarker()
	{

		var snapshot = new SpotifySnapshot();

		foreach (var variable in SpicetifyVariableCatalog.All)
		{
			Assert.NotEqual(SpicetifyValues.Unavailable, variable.Read(snapshot));
		}
	}

	[Fact]
	public void TrackPositionAndDurationUseMinutesAndSeconds()
	{
		var snapshot = new SpotifySnapshot
		{
			Position = TimeSpan.FromSeconds(83),
			PositionKnown = true,
			Duration = TimeSpan.FromSeconds(125),
		};

		Assert.Equal("1:23", SpicetifyVariableCatalog.Find("current-position")!.Read(snapshot));
		Assert.Equal("2:05", SpicetifyVariableCatalog.Find("track-duration")!.Read(snapshot));
		Assert.Equal(VariableType.Text, SpicetifyVariableCatalog.Find("current-position")!.Type);
		Assert.Equal(VariableType.Text, SpicetifyVariableCatalog.Find("track-duration")!.Type);
	}

	[Fact]
	public void AMissingValueIsNullRatherThanAnEmptyStringOrTheMarker()
	{
		var snapshot = new SpotifySnapshot();

		foreach (var variable in SpicetifyVariableCatalog.All.Where(v => v.Type == VariableType.Text))
		{
			var value = variable.Read(snapshot);
			Assert.True(value is null || value is string, $"{variable.Name} produced {value?.GetType().Name ?? "null"}.");
		}
	}

	[Fact]
	public void TheVariableSetIsExactlyWhatWasApproved()
	{

		string[] approved =
		[
			// Playback
			"is_playing", "playback_state", "current_position", "track_duration", "progress_seconds", "progress_percentage",

			// Current track
			"track_name", "artist_name", "album_name", "album_artist", "track_uri", "album_uri",
			"artist_uri", "current_track_id", "playlist_name", "playlist_uri",
			"queue_next_track_name", "queue_next_track_uri", "queue_previous_track_name", "queue_previous_track_uri",

			"volume", "shuffle", "repeat_mode", "muted",

			// Track metadata
			"track_number", "disc_number", "explicit", "track_liked",

			// Connection
			"connection_status",
		];

		var names = SpicetifyVariableCatalog.All.Select(variable => variable.Name)
			.ToHashSet(StringComparer.Ordinal);

		Assert.Equal(approved.Length, SpicetifyVariableCatalog.All.Count);

		foreach (var name in approved)
		{
			Assert.Contains(name, names);
		}

		foreach (var name in names.Except(approved, StringComparer.Ordinal))
		{
			Assert.Fail($"{name} is not on the approved list.");
		}
	}

	[Fact]
	public void OnlyTheServiceConnectionStatusVariableIsRegistered()
	{

		string[] approved = ["spicetifydeck_connection_status"];

		var names = DeckStatusVariableCatalog.All.Select(variable => variable.Name)
			.ToHashSet(StringComparer.Ordinal);

		Assert.Equal(approved.Length, DeckStatusVariableCatalog.All.Count);

		foreach (var name in approved)
		{
			Assert.Contains(name, names);
		}

		foreach (var name in names.Except(approved, StringComparer.Ordinal))
		{
			Assert.Fail($"{name} is not on the approved list.");
		}
	}

	[Fact]
	public void NoTwoVariablesReportTheSameFact()
	{

		var snapshot = new SpotifySnapshot
		{
			TrackName = "Song",
			ArtistName = "Track Artist",
			AlbumName = "Album",
			AlbumArtistName = "Album Artist",
			AlbumUri = "spotify:album:1",
			ArtistUri = "spotify:artist:1",
			ContextName = "Playlist",
			ContextUri = "spotify:playlist:1",
			ReleaseDate = "2020-01-01",
		};

		var byValue = new Dictionary<string, string>(StringComparer.Ordinal);

		foreach (var variable in SpicetifyVariableCatalog.All)
		{
			var value = variable.Read(snapshot);
			if (value is null)
			{
				continue;
			}

			var key = Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

			if (byValue.TryGetValue(key, out var first))
			{
				Assert.Fail($"{first} and {variable.Name} both report {value}.");
			}

			byValue[key] = variable.Name;
		}
	}

	[Fact]
	public void WritableVariablesAreTheOnesWithAWritePath()
	{
		var writable = SpicetifyVariableCatalog.All
			.Where(variable => variable.Writable)
			.Select(variable => variable.Name)
			.ToArray();

		foreach (var name in new[] { "volume", "current_position", "progress_seconds", "progress_percentage", "shuffle", "repeat_mode", "muted" })
		{
			Assert.Contains(name, writable);
		}
	}

	[Fact]
	public void ProgressSecondsIsANumericSliderBoundedByTrackDuration()
	{
		var variable = SpicetifyVariableCatalog.Find("progress-seconds")!;
		var definition = variable.ToDefinition();

		Assert.Equal(VariableType.Numeric, variable.Type);
		Assert.True(variable.Writable);
		Assert.Equal("s", variable.Unit);
		Assert.Equal(0, variable.DecimalPlaces);
		Assert.Equal(10d, variable.Read(new SpotifySnapshot
		{
			PositionKnown = true,
			Position = TimeSpan.FromSeconds(10.4),
		}));
		Assert.NotNull(definition.Write);
	}

	[Fact]
	public void ActionIdsAreUniqueAcrossThePlugin()
	{

		string[] host =
		[
		];

		string[] custom =
		[
			"spicetify-play", "spicetify-pause", "spicetify-toggle-play", "spicetify-next",
			"spicetify-previous", "spicetify-restart-track", "spicetify-skip", "spicetify-seek-to",
			"spicetify-set-volume", "spicetify-volume-step", "spicetify-set-mute",
			"spicetify-toggle-like", "spicetify-add-to-queue", "spicetify-remove-from-queue",
			"spicetify-clear-queue", "spicetify-play-uri", "spicetify-play-liked-songs",
			"spicetify-open-page", "spicetify-open-spotify",
			"spicetify-install-bridge", "spicetify-reinstall-bridge", "spicetify-repair-bridge",
			"spicetify-uninstall-bridge", "spicetify-restart-bridge", "spicetify-reset-bridge-auth",
		];

		var all = host.Concat(custom).ToArray();
		Assert.Equal(all.Length, all.Distinct(StringComparer.Ordinal).Count());
	}

	[Fact]
	public void ParameterHelpersFallBackRatherThanThrowing()
	{
		var empty = new Dictionary<string, object>();

		Assert.Equal(7, ActionParameters.ReadInt(empty, "missing", 7));
		Assert.Equal(7d, ActionParameters.ReadDouble(empty, "missing", 7));
		Assert.True(ActionParameters.ReadBoolean(empty, "missing", true));
		Assert.Null(ActionParameters.ReadString(empty, "missing"));
	}

	[Theory]
	[InlineData("4iV5W9uYEdYUVa79Axb7Rh", "spotify:track:4iV5W9uYEdYUVa79Axb7Rh")]
	[InlineData("spotify:track:4iV5W9uYEdYUVa79Axb7Rh", "spotify:track:4iV5W9uYEdYUVa79Axb7Rh")]
	public void SpotifyUrisAcceptAnIdOrAFullUri(string input, string expected)
	{
		Assert.Equal(expected, SpotifyUris.ForKind(input, "track"));
	}

	[Fact]
	public void SpotifyUrisExtractAnIdFromAShareLink()
	{
		const string link = "https://open.spotify.com/track/4iV5W9uYEdYUVa79Axb7Rh?si=abc123";

		Assert.Equal("spotify:track:4iV5W9uYEdYUVa79Axb7Rh", SpotifyUris.ForKind(link, "track"));
	}

	[Fact]
	public void SpotifyUrisAcceptTheOlderIdQueryForm()
	{
		Assert.Equal("spotify:album:4iV5W9uYEdYUVa79Axb7Rh",
			SpotifyUris.ForKind("https://open.spotify.com/album?id=4iV5W9uYEdYUVa79Axb7Rh", "album"));
	}

	[Fact]
	public void SpotifyUrisRejectEmptyAndNonsenseInput()
	{
		Assert.Null(SpotifyUris.ForKind(null, "track"));
		Assert.Null(SpotifyUris.ForKind("   ", "track"));
		Assert.Null(SpotifyUris.ForKind("not an id", "track"));
	}

	[Fact]
	public void SpotifyUrisReportsTheKindFromAUri()
	{
		Assert.Equal("album", SpotifyUris.KindOf("spotify:album:abc"));
		Assert.Equal("playlist", SpotifyUris.KindOf("spotify:playlist:abc"));
		Assert.Null(SpotifyUris.KindOf(null));
	}

	[Fact]
	public void ArtworkUrlsAreResizedWhenTheyCarryASizeAndLeftAloneWhenTheyDoNot()
	{
		Assert.Equal("https://i.scdn.co/image/a640x640.jpg",
			Artwork.ArtworkUrl.Resize("https://i.scdn.co/image/a300x300.jpg", 640));

		Assert.Equal("https://i.scdn.co/image/abc123", Artwork.ArtworkUrl.Resize("https://i.scdn.co/image/abc123", 300));
	}

	[Fact]
	public void ArtworkSizesAreClampedToASensibleRange()
	{
		Assert.Equal("https://i.scdn.co/image/a64x64.jpg",
			Artwork.ArtworkUrl.Resize("https://i.scdn.co/image/a300x300.jpg", 1));
		Assert.Equal("https://i.scdn.co/image/a640x640.jpg",
			Artwork.ArtworkUrl.Resize("https://i.scdn.co/image/a300x300.jpg", 5000));
	}

}
