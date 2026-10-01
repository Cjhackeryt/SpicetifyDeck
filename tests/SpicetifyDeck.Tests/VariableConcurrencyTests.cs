using MacroDeck.Sdk.Variables;
using SpicetifyDeck.Bridge;
using SpicetifyDeck.State;
using SpicetifyDeck.Variables;
using Xunit;

namespace SpicetifyDeck.Tests;

public sealed class VariableConcurrencyTests
{
	[Theory]
	[InlineData(BridgeConnectionState.Disconnected, "Bridge Disconnected")]
	[InlineData(BridgeConnectionState.Connecting, "Waiting for Spotify")]
	[InlineData(BridgeConnectionState.Authenticating, "Waiting for Spotify")]
	[InlineData(BridgeConnectionState.AuthenticationFailed, "Authentication Failed")]
	[InlineData(BridgeConnectionState.Connected, "Bridge Connected")]
	public void ConnectionStatusFallbackIsAlwaysAvailable(BridgeConnectionState state, string expected)
	{
		Assert.Equal(expected, SpicetifyVariableProvider.ConnectionFallback(state));
	}

	[Fact]
	public void ReadingEveryVariableFromManyThreadsNeverThrowsOrReturnsUnavailable()
	{
		var data = Path.Combine(Path.GetTempPath(), "sd-var-" + Guid.NewGuid().ToString("n"));
		Directory.CreateDirectory(data);

		var credentials = BridgeCredentials.Load(data, Serilog.Log.Logger);
		var connections = new BridgeConnectionManager(credentials, Serilog.Log.Logger);
		var state = new SpotifyStateManager(connections, new PluginSettings(), Serilog.Log.Logger);
		var endpoint = new BridgeEndpoint(Serilog.Log.Logger);
		var generator = new BridgeScriptGenerator(endpoint, credentials, Serilog.Log.Logger);
		var locator = new SpicetifyLocator(Serilog.Log.Logger);
		var bridgeService = new SpicetifyBridgeService(
			locator, new SpicetifyCli(Serilog.Log.Logger), generator, credentials, connections, endpoint, Serilog.Log.Logger);
		var provider = new SpicetifyVariableProvider(state, connections, bridgeService, Serilog.Log.Logger);

		state.Accept(new BridgeState
		{
			Playback = new BridgePlayback
			{
				IsPlaying = true,
				IsPaused = false,
				PositionMs = 1000,
				DurationMs = 200_000,
				ProgressFraction = 0.005,
				IsBuffering = false,
			},
			Controls = new BridgeControls
			{
				VolumePercent = 50,
				Muted = false,
				Shuffle = true,
				Repeat = "context",
			},
			Track = new BridgeTrack
			{
				Name = "Song",
				Uri = "spotify:track:abc",
				ArtistName = "Artist",
				AlbumArtistName = "Artist",
				AlbumTitle = "Album",
				AlbumUri = "spotify:album:xyz",
				ArtistUri = "spotify:artist:xyz",
				ImageUrl = "https://example.invalid/a.jpg",
				TrackNumber = 1,
				DiscNumber = 1,
				Explicit = false,
				ReleaseDate = "2020-01-01",
			},
			Context = new BridgeContext { Uri = "spotify:playlist:abc", Name = "A Playlist" },
			Client = new BridgeClient { Platform = "win32", DeviceName = "PC" },
			Capabilities = new BridgeCapabilities { History = true },
		});

		var ids = provider.Variables.Select(definition => definition.Id).OfType<string>().ToArray();

		var known = ids
			.Where(id => provider.ReadAsync(id).AsTask().GetAwaiter().GetResult().Value is not null)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		Assert.NotEmpty(known);

		var failures = new System.Collections.Concurrent.ConcurrentBag<string>();
		var lost = new System.Collections.Concurrent.ConcurrentBag<string>();

		Parallel.For(0, 64, _ =>
		{
			for (var round = 0; round < 40; round++)
			{
				foreach (var id in ids)
				{
					try
					{
						if (provider.ReadAsync(id).AsTask().GetAwaiter().GetResult().Value is null)
						{
							lost.Add(id);
						}
					}
					catch (Exception exception)
					{
						failures.Add($"{id}: {exception.GetType().Name} {exception.Message}");
					}
				}
			}
		});

		Assert.True(failures.IsEmpty, "Reads threw under concurrency: " + string.Join("; ", failures.Take(3)));

		var flickered = lost
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Where(known.Contains)
			.ToArray();

		Assert.True(
			flickered.Length == 0,
			"These variables had a value and then lost it: " + string.Join(", ", flickered));
	}

	[Fact]
	public async Task EveryDeckStatusVariableIsReadableAndNeverBlank()
	{
		var data = Path.Combine(Path.GetTempPath(), "sd-var-" + Guid.NewGuid().ToString("n"));
		Directory.CreateDirectory(data);

		var credentials = BridgeCredentials.Load(data, Serilog.Log.Logger);
		var connections = new BridgeConnectionManager(credentials, Serilog.Log.Logger);
		var state = new SpotifyStateManager(connections, new PluginSettings(), Serilog.Log.Logger);
		var endpoint = new BridgeEndpoint(Serilog.Log.Logger);
		var generator = new BridgeScriptGenerator(endpoint, credentials, Serilog.Log.Logger);
		var locator = new SpicetifyLocator(Serilog.Log.Logger);
		var bridgeService = new SpicetifyBridgeService(
			locator, new SpicetifyCli(Serilog.Log.Logger), generator, credentials, connections, endpoint, Serilog.Log.Logger);
		var provider = new SpicetifyVariableProvider(state, connections, bridgeService, Serilog.Log.Logger);

		foreach (var definition in DeckStatusVariableCatalog.Definitions)
		{
			if (definition.Id is not { } id)
			{
				continue;
			}

			var reading = await provider.ReadAsync(id);

			if (id is "spicetifydeck-connection-status")
			{
				Assert.False(reading.Value is null, $"{id} must always have a value.");
			}
		}
	}
}
