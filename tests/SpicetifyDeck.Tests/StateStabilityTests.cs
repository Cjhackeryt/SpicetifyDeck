using MacroDeck.Sdk.MusicPlayer;
using Serilog;
using SpicetifyDeck.Bridge;
using SpicetifyDeck.State;
using Xunit;

namespace SpicetifyDeck.Tests;

public sealed class StateStabilityTests
{
	private static readonly BridgeConnectionManager Connections =
		new(BridgeCredentials.Load(Path.GetTempPath(), Log.Logger), Log.Logger);

	private static SpotifyStateManager CreateManager(PluginSettings? settings = null) =>
		new(Connections, settings ?? new PluginSettings(), Log.Logger);

	[Fact]
	public void AMissingFieldKeepsTheLastGoodValue()
	{
		using var manager = CreateManager();

		manager.Accept(Report(trackName: "One", uri: "spotify:track:one", volume: 40));
		Assert.Equal("One", manager.Current.TrackName);

		manager.Accept(Report(uri: "spotify:track:one", volume: 40));
		Assert.Equal("One", manager.Current.TrackName);
	}

	[Fact]
	public void AnIdenticalReportIsNotAChange()
	{
		using var manager = CreateManager();

		Assert.NotNull(manager.Accept(Report(trackName: "One", uri: "spotify:track:one", volume: 40)));
		Assert.Null(manager.Accept(Report(trackName: "One", uri: "spotify:track:one", volume: 40)));
		Assert.Null(manager.Accept(Report(trackName: "One", uri: "spotify:track:one", volume: 40)));
	}

	[Fact]
	public void ARealChangeNamesOnlyWhatMoved()
	{
		using var manager = CreateManager();

		manager.Accept(Report(trackName: "One", uri: "spotify:track:one", volume: 40));
		var change = manager.Accept(Report(trackName: "One", uri: "spotify:track:one", volume: 55));

		Assert.NotNull(change);
		Assert.Contains(nameof(SpotifySnapshot.VolumePercent), change!.Fields);
		Assert.DoesNotContain(nameof(SpotifySnapshot.TrackName), change.Fields);
		Assert.False(change.TrackChanged);
	}

	[Fact]
	public void APositionOnlyReportIsNotAChange()
	{

		using var manager = CreateManager();

		manager.Accept(Report(trackName: "One", uri: "spotify:track:one", volume: 40, positionMs: 0));
		Assert.Null(manager.Accept(Report(trackName: "One", uri: "spotify:track:one", volume: 40, positionMs: 1000)));
		Assert.Null(manager.Accept(Report(trackName: "One", uri: "spotify:track:one", volume: 40, positionMs: 2000)));
	}

	[Fact]
	public void ATrackChangeKeepsThePreviousFieldsUntilNewMetadataArrives()
	{

		using var manager = CreateManager();

		manager.Accept(Report(trackName: "One", uri: "spotify:track:one", volume: 40));
		Assert.Equal("One", manager.Current.TrackName);

		manager.Accept(Report(volume: 40, uri: "spotify:track:two"));
		Assert.Equal("One", manager.Current.TrackName);

		manager.Accept(new BridgeState
		{
			Track = new BridgeTrack
			{
				Uri = "spotify:track:two",
				Name = "Two",
				AlbumTitle = "New Album",
			},
		});
		Assert.Equal("Two", manager.Current.TrackName);
		Assert.Equal("New Album", manager.Current.AlbumName);
	}

	[Fact]
	public void AValueSurvivesUntilTheTimeoutThenGoesUnavailable()
	{
		using var manager = CreateManager();

		manager.Accept(Report(trackName: "One", uri: "spotify:track:one", volume: 40));

		manager.Accept(Report(uri: "spotify:track:one", volume: 40));
		Assert.Equal("One", manager.Current.TrackName);
		Assert.Equal(40, manager.Current.VolumePercent);

		using var expiring = CreateManager(new PluginSettings { UnavailableTimeout = TimeSpan.Zero });
		expiring.Accept(Report(trackName: "One", uri: "spotify:track:one", volume: 40));
		Assert.Equal("One", expiring.Current.TrackName);

		expiring.Accept(Report(uri: "spotify:track:one"));
		Assert.Null(expiring.Current.TrackName);
	}

	[Fact]
	public void InvalidSentinelsAndUnknownBooleansKeepTheirLastValidValues()
	{
		using var manager = CreateManager();

		manager.Accept(new BridgeState
		{
			Playback = new BridgePlayback { IsPlaying = true },
			Controls = new BridgeControls { Shuffle = true, Repeat = "track" },
			Track = new BridgeTrack
			{
				Uri = "spotify:track:one",
				Name = "Song",
				ArtistName = "Artist",
			},
		});

		manager.Accept(new BridgeState
		{
			Track = new BridgeTrack
			{
				Uri = "spotify:track:one",
				Name = "Unavailable",
				ArtistName = "N/A",
			},
		});

		var snapshot = manager.Current;
		Assert.Equal("Song", snapshot.TrackName);
		Assert.Equal("Artist", snapshot.ArtistName);
		Assert.True(snapshot.IsPlaying);
		Assert.True(snapshot.ShuffleEnabled);
		Assert.Equal(RepeatMode.Track, snapshot.RepeatMode);
	}

	[Fact]
	public void NewTrackFieldsStayTogetherUntilTheNewTrackNameArrives()
	{
		using var manager = CreateManager();

		manager.Accept(new BridgeState
		{
			Track = new BridgeTrack
			{
				Uri = "spotify:track:one",
				Name = "Old Song",
				ArtistName = "Old Artist",
				AlbumTitle = "Old Album",
			},
		});

		manager.Accept(new BridgeState
		{
			Track = new BridgeTrack
			{
				Uri = "spotify:track:two",
				ArtistName = "New Artist",
				AlbumTitle = "New Album",
			},
		});

		Assert.Equal("Old Song", manager.Current.TrackName);
		Assert.Equal("Old Artist", manager.Current.ArtistName);
		Assert.Equal("Old Album", manager.Current.AlbumName);

		manager.Accept(new BridgeState
		{
			Track = new BridgeTrack
			{
				Uri = "spotify:track:two",
				Name = "New Song",
				ArtistName = "New Artist",
				AlbumTitle = "New Album",
			},
		});

		Assert.Equal("New Song", manager.Current.TrackName);
		Assert.Equal("New Artist", manager.Current.ArtistName);
		Assert.Equal("New Album", manager.Current.AlbumName);
	}

	[Fact]
	public void PositionIsExtrapolatedWhilePlayingAndFrozenWhenPaused()
	{
		var playing = new SpotifySnapshot
		{
			IsPlaying = true,
			IsPlayingKnown = true,
			PositionKnown = true,
			Position = TimeSpan.FromSeconds(10),
			CapturedAt = DateTimeOffset.UtcNow,
			Duration = TimeSpan.FromMinutes(3),
		};

		Assert.InRange(playing.CurrentPosition, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(11));

		var paused = playing with { IsPlaying = false };
		Assert.Equal(TimeSpan.FromSeconds(10), paused.CurrentPosition);
	}

	[Fact]
	public void PositionIsExtrapolatedWhilePlaying()
	{
		var stale = new SpotifySnapshot
		{
			IsPlaying = true,
			Position = TimeSpan.FromSeconds(10),
			CapturedAt = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(5),
			Duration = TimeSpan.FromMinutes(10),
		};

		Assert.InRange(stale.CurrentPosition, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(16));
	}

	[Fact]
	public void PositionNeverPassesTheTrackLength()
	{
		var overruns = new SpotifySnapshot
		{
			IsPlaying = true,
			Position = TimeSpan.FromSeconds(178),
			CapturedAt = DateTimeOffset.UtcNow - TimeSpan.FromSeconds(30),
			Duration = TimeSpan.FromMinutes(3),
		};

		Assert.Equal(TimeSpan.FromMinutes(3), overruns.CurrentPosition);
	}

	[Fact]
	public void ProgressIsClampedToTheTrackLength()
	{
		var snapshot = new SpotifySnapshot
		{
			PositionKnown = true,
			Position = TimeSpan.FromMinutes(9),
			Duration = TimeSpan.FromMinutes(3),
		};

		Assert.Equal(1d, snapshot.ProgressFraction);
		Assert.Equal(TimeSpan.Zero, snapshot.Remaining);
		Assert.Null(new SpotifySnapshot().ProgressFraction);
	}

	[Fact]
	public void DerivedStateWordsAgreeWithTheFlagsTheySummarise()
	{
		Assert.Equal("Disconnected", new SpotifySnapshot().ConnectionStatus);
		Assert.Equal("Disconnected", new SpotifySnapshot().ClientStateText);
		Assert.Equal("AuthenticationFailed", new SpotifySnapshot { ConnectionState = BridgeConnectionState.AuthenticationFailed }.ConnectionStatus);
		Assert.Equal("Connected", new SpotifySnapshot
		{
			BridgeConnected = true,
			ConnectionState = BridgeConnectionState.Connected,
		}.ConnectionStatus);
		Assert.Equal("Playing", new SpotifySnapshot { BridgeConnected = true, IsPlaying = true, IsPlayingKnown = true }.ClientStateText);
		Assert.Equal("Buffering", new SpotifySnapshot { BridgeConnected = true, IsPlaying = true, IsPlayingKnown = true, IsBuffering = true }.ClientStateText);
	}

	[Fact]
	public void PlaybackStateFollowsThePlayingAndPausedFlags()
	{
		Assert.Equal(PlaybackState.Playing, new SpotifySnapshot { IsPlaying = true }.PlaybackState);
		Assert.Equal(PlaybackState.Paused, new SpotifySnapshot { IsPaused = true }.PlaybackState);
		Assert.Equal(PlaybackState.Stopped, new SpotifySnapshot().PlaybackState);
	}

		private static BridgeState Report(
		string? trackName = null,
		string? uri = null,
		int? volume = null,
		long? positionMs = null,
		long? durationMs = null,
		bool? isPlaying = null)
	{
		var report = new BridgeState
		{
			Playback = new BridgePlayback
			{
				PositionMs = positionMs,
				DurationMs = durationMs,
				IsPlaying = isPlaying,
				IsPaused = isPlaying == true ? false : null,
			},
			Controls = new BridgeControls { VolumePercent = volume },
		};

		return trackName is null && uri is null
			? report
			: report with
			{
				Track = new BridgeTrack { Name = trackName, Uri = uri, ArtistName = "Someone" },
			};
	}
}
