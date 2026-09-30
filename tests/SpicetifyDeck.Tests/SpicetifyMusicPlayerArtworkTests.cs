using System.Net;
using SpicetifyDeck.Artwork;
using SpicetifyDeck.Bridge;
using SpicetifyDeck.State;
using Xunit;

namespace SpicetifyDeck.Tests;

public sealed class SpicetifyMusicPlayerArtworkTests
{
	[Fact]
	public async Task ArtworkIdResolvesItsImageEvenAfterPlaybackMovesToAnotherTrack()
	{
		const string firstImage = "https://i.scdn.co/image/ab67616d00000001300x300.jpg";
		const string secondImage = "https://i.scdn.co/image/ab67616d00000002300x300.jpg";
		var handler = new RecordingImageHandler();
		var logger = Serilog.Log.Logger;
		var factory = new SingleClientFactory(new HttpClient(handler));
		var artwork = new ArtworkFetcher(factory, logger);
		var credentials = BridgeCredentials.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("n")), logger);
		await using var bridge = new BridgeConnectionManager(credentials, logger);
		using var state = new SpotifyStateManager(bridge, new PluginSettings(), logger);
		var player = new SpicetifyMusicPlayer(state, artwork, bridge, logger);

		state.Accept(new BridgeState
		{
			Track = new BridgeTrack { Uri = "spotify:track:first", Name = "First", ImageUrl = firstImage },
		});
		var firstPlayerState = await player.GetStateAsync();

		state.Accept(new BridgeState
		{
			Track = new BridgeTrack { Uri = "spotify:track:second", Name = "Second", ImageUrl = secondImage },
		});

		var image = await player.GetArtworkAsync(firstPlayerState.ArtworkId!);

		Assert.NotNull(image);
		Assert.Equal(firstImage, handler.RequestUri?.AbsoluteUri);

		await artwork.FetchAsync(firstImage, 640, CancellationToken.None);
		Assert.Equal(
			"https://i.scdn.co/image/ab67616d00000001640x640.jpg",
			handler.RequestUri?.AbsoluteUri);
	}

	private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
	{
		public HttpClient CreateClient(string name) => client;
	}

	private sealed class RecordingImageHandler : HttpMessageHandler
	{
		public Uri? RequestUri { get; private set; }

		protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			RequestUri = request.RequestUri;
			var content = new ByteArrayContent([1, 2, 3]);
			content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
			return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
		}
	}
}
