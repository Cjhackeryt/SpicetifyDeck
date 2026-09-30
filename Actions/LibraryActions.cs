using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using Serilog;
using SpicetifyDeck.Bridge;
using SpicetifyDeck.State;

namespace SpicetifyDeck.Actions;

public sealed class PlayUriAction(SpotifyStateManager state, BridgeConnectionManager bridge, ILogger logger)
	: SpicetifyActionBase(state, bridge, logger)
{
	public override string Id => "spicetify-play-uri";

	public override LocalizedText Name => Strings.Actions.PlayUri.Name();

	public override LocalizedText Description => Strings.Actions.PlayUri.Description();

	public override IReadOnlyList<ActionParameter> Parameters =>
	[
		ActionParameter.Text("uri", Strings.Actions.PlayUri.Uri.Label(), Strings.Actions.PlayUri.Uri.Description(),
			Strings.Actions.PlayUri.Uri.Placeholder(), required: true),
		ActionParameter.Choice("kind", KindOptions, Strings.Actions.PlayUri.Kind.Label(),
			Strings.Actions.PlayUri.Kind.Description(), "track", true),
	];

	private static IReadOnlyList<ActionParameterOption> KindOptions { get; } =
	[
		new() { Value = "track", Label = Strings.Actions.PlayUri.Kind.Track() },
		new() { Value = "album", Label = Strings.Actions.PlayUri.Kind.Album() },
		new() { Value = "playlist", Label = Strings.Actions.PlayUri.Kind.Playlist() },
		new() { Value = "artist", Label = Strings.Actions.PlayUri.Kind.Artist() },
	];

	protected override async Task<LocalizedText?> ExecuteAsync(ActionExecutionContext context)
	{
		var kind = ActionParameters.ReadString(context.Parameters, "kind") ?? "track";
		var uri = SpotifyUris.ForKind(ActionParameters.ReadString(context.Parameters, "uri"), kind);

		return uri is null
			? Strings.Actions.PlayUri.Errors.BadUri()
			: await SendAsync(BridgeCommandKind.PlayUri, Payload("uri", uri), context.CancellationToken)
				.ConfigureAwait(false);
	}
}

public sealed class PlayLikedSongsAction(SpotifyStateManager state, BridgeConnectionManager bridge, ILogger logger)
	: SpicetifyActionBase(state, bridge, logger)
{
		private const string SavedTracksUri = "spotify:collection:tracks";

	public override string Id => "spicetify-play-liked-songs";

	public override LocalizedText Name => Strings.Actions.PlayLikedSongs.Name();

	public override LocalizedText Description => Strings.Actions.PlayLikedSongs.Description();

	protected override Task<LocalizedText?> ExecuteAsync(ActionExecutionContext context) =>
		SendAsync(BridgeCommandKind.PlayUri, Payload("uri", SavedTracksUri), context.CancellationToken);
}

public sealed class OpenPageAction(SpotifyStateManager state, BridgeConnectionManager bridge, ILogger logger)
	: SpicetifyActionBase(state, bridge, logger)
{
	public override string Id => "spicetify-open-page";

	public override LocalizedText Name => Strings.Actions.OpenPage.Name();

	public override LocalizedText Description => Strings.Actions.OpenPage.Description();

	public override IReadOnlyList<ActionParameter> Parameters =>
	[
		ActionParameter.Choice("target", TargetOptions, Strings.Actions.OpenPage.Target.Label(),
			Strings.Actions.OpenPage.Target.Description(), "currentTrack", true),
		ActionParameter.Text("uri", Strings.Actions.OpenPage.Uri.Label(), Strings.Actions.OpenPage.Uri.Description(),
			Strings.Actions.OpenPage.Uri.Placeholder()).OnlyWhen("target", "custom"),
	];

	private static IReadOnlyList<ActionParameterOption> TargetOptions { get; } =
	[
		new() { Value = "currentTrack", Label = Strings.Actions.OpenPage.Target.CurrentTrack() },
		new() { Value = "currentArtist", Label = Strings.Actions.OpenPage.Target.CurrentArtist() },
		new() { Value = "currentAlbum", Label = Strings.Actions.OpenPage.Target.CurrentAlbum() },
		new() { Value = "likedSongs", Label = Strings.Actions.OpenPage.Target.LikedSongs() },
		new() { Value = "custom", Label = Strings.Actions.OpenPage.Target.Custom() },
	];

	protected override async Task<LocalizedText?> ExecuteAsync(ActionExecutionContext context)
	{
		var snapshot = State.Current;

		if (Guard(snapshot.Capabilities.History, Strings.Actions.OpenPage.NoSupport()) is { } missing)
		{
			return missing;
		}

		var target = ActionParameters.ReadString(context.Parameters, "target") ?? "currentTrack";
		var uri = target switch
		{
			"currentArtist" => snapshot.ArtistUri,
			"currentAlbum" => snapshot.AlbumUri,
			"likedSongs" => "spotify:collection:tracks",
			"custom" => SpotifyUris.Normalize(ActionParameters.ReadString(context.Parameters, "uri")),
			_ => snapshot.AlbumUri ?? snapshot.TrackUri,
		};

		return uri is null
			? Strings.Actions.Errors.NoCurrentTrack()
			: await SendAsync(BridgeCommandKind.OpenUri, Payload("uri", uri), context.CancellationToken)
				.ConfigureAwait(false);
	}
}

public sealed class OpenSpotifyAction(SpotifyStateManager state, BridgeConnectionManager bridge, ILogger logger)
	: SpicetifyActionBase(state, bridge, logger)
{
	public override string Id => "spicetify-open-spotify";

	public override LocalizedText Name => Strings.Actions.OpenSpotify.Name();

	public override LocalizedText Description => Strings.Actions.OpenSpotify.Description();

	public override IReadOnlyList<ActionParameter> Parameters =>
	[
		ActionParameter.Text("uri", Strings.Actions.OpenSpotify.Uri.Label(), Strings.Actions.OpenSpotify.Uri.Description(),
			Strings.Actions.OpenSpotify.Uri.Placeholder()),
	];

	protected override Task<LocalizedText?> ExecuteAsync(ActionExecutionContext context)
	{
		var typed = ActionParameters.ReadString(context.Parameters, "uri");
		var uri = typed is null ? State.Current.AlbumUri ?? State.Current.TrackUri : SpotifyUris.Normalize(typed);

		try
		{
			using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri ?? "spotify:")
			{
				UseShellExecute = true,
			});

			return Task.FromResult<LocalizedText?>(process is null
				? Strings.Actions.OpenSpotify.Errors.NotRunning()
				: null);
		}
		catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
		{
			Log.Debug(exception, "Could not start the Spotify client through its protocol handler.");
			return Task.FromResult<LocalizedText?>(Strings.Actions.OpenSpotify.Errors.NotInstalled());
		}
	}
}
