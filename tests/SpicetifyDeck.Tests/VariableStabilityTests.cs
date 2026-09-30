using MacroDeck.Sdk.MusicPlayer;
using Serilog;
using SpicetifyDeck.Bridge;
using SpicetifyDeck.State;
using SpicetifyDeck.Variables;
using Xunit;

namespace SpicetifyDeck.Tests;

public sealed class VariableStabilityTests
{
	private static readonly BridgeConnectionManager Connections =
		new(BridgeCredentials.Load(Path.GetTempPath(), Serilog.Log.Logger), Serilog.Log.Logger);

	private static SpotifyStateManager CreateManager(PluginSettings? settings = null) =>
		new(Connections, settings ?? new PluginSettings(), Serilog.Log.Logger);

	[Fact]
	public void AMomentaryMissingTrackDoesNotBlankTheTrack()
	{
		using var manager = CreateManager();

		manager.Accept(Report(trackName: "One", uri: "spotify:track:one", volume: 40));
		Assert.Equal("One", manager.Current.TrackName);

		manager.Accept(Report(trackName: null, uri: null, volume: 40));

		Assert.Equal("One", manager.Current.TrackName);
		Assert.Equal(40, manager.Current.VolumePercent);
	}

	[Fact]
	public void ARepeatedMomentaryMissingTrackKeepsTheValue()
	{

		using var manager = CreateManager();

		manager.Accept(Report(trackName: "One", uri: "spotify:track:one", volume: 40));

		for (var tick = 0; tick < 20; tick++)
		{
			manager.Accept(Report(trackName: null, uri: null, volume: 40));
			Assert.Equal("One", manager.Current.TrackName);
		}
	}

	[Fact]
	public async Task CurrentTrackVariableStaysStableAcrossThirtySecondsOfPartialReports()
	{
		using var manager = CreateManager();
		var currentTrack = SpicetifyVariableCatalog.Find("track-name")!;
		manager.Accept(Report(trackName: "One", uri: "spotify:track:one", volume: 40));

		for (var tick = 0; tick < 31; tick++)
		{
			await Task.Delay(TimeSpan.FromSeconds(1));
			manager.Accept(tick % 2 == 0
				? new BridgeState
				{
					Controls = new BridgeControls { VolumePercent = 40 },
					Track = new BridgeTrack { Uri = "spotify:track:one" },
				}
				: Report(trackName: "One", uri: "spotify:track:one", volume: 40));

			Assert.Equal("One", currentTrack.Read(manager.Current));
		}
	}

	[Fact]
	public void ARealTrackChangeWithValidMetadataCommitsImmediately()
	{

		using var manager = CreateManager();

		manager.Accept(Report(trackName: "One", uri: "spotify:track:one", volume: 40));
		manager.Accept(Report(trackName: "Two", uri: "spotify:track:two", volume: 40));

		Assert.Equal("Two", manager.Current.TrackName);
	}

	[Fact]
	public void ARealTrackChangeWithNoNameYetKeepsTheOldTrackUntilReplacement()
	{
		using var manager = CreateManager();

		manager.Accept(Report(trackName: "One", uri: "spotify:track:one", volume: 40));

		manager.Accept(Report(trackName: null, uri: "spotify:track:two", volume: 40));

		Assert.Equal("One", manager.Current.TrackName);

		manager.Accept(Report(trackName: "Two", uri: "spotify:track:two", volume: 40));
		Assert.Equal("Two", manager.Current.TrackName);
	}

	[Fact]
	public void TrackArtworkAndNumbersSurviveAMomentaryMissingTrackToo()
	{

		using var manager = CreateManager();

		manager.Accept(new BridgeState
		{
			Controls = new BridgeControls { VolumePercent = 40 },
			Track = new BridgeTrack
			{
				Name = "One",
				Uri = "spotify:track:one",
				ArtistName = "Someone",
				AlbumTitle = "An Album",
				ImageUrl = "https://example.invalid/a.jpg",
				TrackNumber = 3,
				DiscNumber = 1,
			},
		});

		manager.Accept(Report(trackName: null, uri: null, volume: 40));

		var snapshot = manager.Current;
		Assert.Equal("One", snapshot.TrackName);
		Assert.Equal("Someone", snapshot.ArtistName);
		Assert.Equal("An Album", snapshot.AlbumName);
		Assert.Equal("https://example.invalid/a.jpg", snapshot.ArtworkUrl);
		Assert.Equal(3, snapshot.TrackNumber);
		Assert.Equal(1, snapshot.DiscNumber);
	}

	[Fact]
	public void AStaleConnectionFlagIsNeverHandedToAReader()
	{

		using var manager = CreateManager();

		manager.Accept(Report(trackName: "One", uri: "spotify:track:one", volume: 40));

		manager.ForceStoredConnection(true);

		Assert.False(manager.Current.BridgeConnected);
		Assert.Equal("Disconnected", manager.Current.ConnectionStatus);
		Assert.Equal("Disconnected", manager.Current.ClientStateText);
	}

	[Fact]
	public void ADisconnectKeepsTheValuesButAnnouncesTheLinkGoingAway()
	{
		using var manager = CreateManager();
		var announcements = new List<bool>();
		manager.Changed += (change, _) =>
		{
			announcements.Add(change.Fields.Contains(nameof(SpotifySnapshot.BridgeConnected)));
			return Task.CompletedTask;
		};

		manager.Accept(Report(trackName: "One", uri: "spotify:track:one", volume: 40));
		manager.ForceStoredConnection(true);
		announcements.Clear();

		manager.Connected(false);

		Assert.Equal("Disconnected", manager.Current.ConnectionStatus);

		Assert.Equal("One", manager.Current.TrackName);

		manager.Connected(false);
		Assert.True(announcements.Count is 0 or 1);
	}

	[Fact]
	public void AReconnectKeepsTheLastValuesUntilTheNewClientReports()
	{

		using var manager = CreateManager();

		manager.Accept(Report(trackName: "One", uri: "spotify:track:one", volume: 40));
		manager.Connected(true);
		manager.Connected(true);

		manager.Accept(Report(trackName: null, uri: null, volume: null));

		Assert.Equal("One", manager.Current.TrackName);
		Assert.Equal(40, manager.Current.VolumePercent);

		manager.Accept(Report(trackName: "Two", uri: "spotify:track:two", volume: 55));
		Assert.Equal("Two", manager.Current.TrackName);
		Assert.Equal(55, manager.Current.VolumePercent);
	}

	[Fact]
	public void RepeatedIdenticalReportsRaiseNoChange()
	{

		using var manager = CreateManager();

		Assert.NotNull(manager.Accept(Report(trackName: "One", uri: "spotify:track:one", volume: 40)));
		Assert.Null(manager.Accept(Report(trackName: "One", uri: "spotify:track:one", volume: 40)));
		Assert.Null(manager.Accept(Report(trackName: null, uri: null, volume: 40)));
		Assert.Null(manager.Accept(Report(trackName: "One", uri: "spotify:track:one", volume: 40)));
	}

	[Fact]
	public async Task ProviderDoesNotResurrectAValueAfterTheManagerExpiresIt()
	{
		var connections = new BridgeConnectionManager(
			BridgeCredentials.Load(Path.GetTempPath(), Serilog.Log.Logger), Serilog.Log.Logger);
		using var manager = new SpotifyStateManager(
			connections,
			new PluginSettings { UnavailableTimeout = TimeSpan.Zero },
			Serilog.Log.Logger);
		var endpoint = new BridgeEndpoint(Serilog.Log.Logger);
		var credentials = BridgeCredentials.Load(Path.GetTempPath(), Serilog.Log.Logger);
		var generator = new BridgeScriptGenerator(endpoint, credentials, Serilog.Log.Logger);
		var service = new SpicetifyBridgeService(
			new SpicetifyLocator(Serilog.Log.Logger),
			new SpicetifyCli(Serilog.Log.Logger),
			generator,
			credentials,
			connections,
			endpoint,
			Serilog.Log.Logger);
		var provider = new SpicetifyVariableProvider(manager, connections, service, Serilog.Log.Logger);
		connections.ListenerStarted();

		Assert.Equal(SpicetifyValues.WaitingForSpicetify, (await provider.ReadAsync("track-name")).Value);

		manager.Accept(Report(trackName: "One", uri: "spotify:track:one", volume: 40));
		Assert.Equal("One", (await provider.ReadAsync("track-name")).Value);

		manager.Accept(Report(uri: "spotify:track:two", volume: 40));

		Assert.Null((await provider.ReadAsync("track-name")).Value);
	}

	[Fact]
	public void UnreportedBooleanAndRepeatValuesRemainUnknown()
	{

		var snapshot = new SpotifySnapshot();

		Assert.Null(SpicetifyVariableCatalog.Find("is-playing")!.Read(snapshot));
		Assert.Null(SpicetifyVariableCatalog.Find("playback-state")!.Read(snapshot));
		Assert.Null(SpicetifyVariableCatalog.Find("current-position")!.Read(snapshot));
		Assert.Null(SpicetifyVariableCatalog.Find("shuffle")!.Read(snapshot));
		Assert.Null(SpicetifyVariableCatalog.Find("repeat-mode")!.Read(snapshot));
	}

	[Fact]
	public void ReportedBooleanAndRepeatValuesBecomeKnown()
	{

		var snapshot = new SpotifySnapshot
		{
			IsPlaying = true,
			IsPlayingKnown = true,
			PositionKnown = true,
			Position = TimeSpan.FromSeconds(30),

			CapturedAt = DateTimeOffset.UtcNow,
			ShuffleEnabled = true,
			ShuffleEnabledKnown = true,
			RepeatMode = RepeatMode.Context,
			RepeatModeKnown = true,
		};

		Assert.Equal(true, SpicetifyVariableCatalog.Find("is-playing")!.Read(snapshot));
		Assert.Equal("Playing", SpicetifyVariableCatalog.Find("playback-state")!.Read(snapshot));
		Assert.InRange(
			Convert.ToDouble(
				SpicetifyVariableCatalog.Find("current-position")!.Read(snapshot),
				System.Globalization.CultureInfo.InvariantCulture),
			30000d,
			31000d);
		Assert.Equal(true, SpicetifyVariableCatalog.Find("shuffle")!.Read(snapshot));
		Assert.Equal("Context", SpicetifyVariableCatalog.Find("repeat-mode")!.Read(snapshot));
	}

	[Fact]
	public void ExplicitFalsePlaybackFlagsAreKnownAndReadAsStopped()
	{
		using var manager = CreateManager();
		manager.Accept(new BridgeState
		{
			Playback = new BridgePlayback { IsPlaying = false, IsPaused = false },
		});

		var snapshot = manager.Current;
		Assert.True(snapshot.IsPlayingKnown);
		Assert.True(snapshot.IsPausedKnown);
		Assert.False(snapshot.IsPlaying);
		Assert.Equal("Stopped", SpicetifyVariableCatalog.Find("playback-state")!.Read(snapshot));
		Assert.Equal(false, SpicetifyVariableCatalog.Find("is-playing")!.Read(snapshot));
	}

	[Fact]
	public void MissingPlaybackFlagsRemainUnknownInsteadOfBecomingFalse()
	{
		using var manager = CreateManager();
		manager.Accept(new BridgeState
		{
			Playback = new BridgePlayback(),
		});

		var snapshot = manager.Current;
		Assert.False(snapshot.IsPlayingKnown);
		Assert.False(snapshot.IsPausedKnown);
		Assert.Null(SpicetifyVariableCatalog.Find("playback-state")!.Read(snapshot));
		Assert.Null(SpicetifyVariableCatalog.Find("is-playing")!.Read(snapshot));
	}

	private static BridgeState Report(
		string? trackName = null,
		string? uri = null,
		int? volume = null,
		bool? isPlaying = null)
	{
		var report = new BridgeState
		{
			Controls = new BridgeControls { VolumePercent = volume },
			Playback = isPlaying is null ? null : new BridgePlayback { IsPlaying = isPlaying },
		};

		return trackName is null && uri is null
			? report
			: report with { Track = new BridgeTrack { Name = trackName, Uri = uri } };
	}
}
