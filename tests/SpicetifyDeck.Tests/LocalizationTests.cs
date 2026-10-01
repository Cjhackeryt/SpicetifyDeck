using System.Collections;
using System.Reflection;
using System.Resources;
using Xunit;

namespace SpicetifyDeck.Tests;

public sealed class LocalizationTests
{
	private static Dictionary<string, string> ReadShippedStrings()
	{

		var assembly = typeof(SpicetifyIntegration).Assembly;
		var name = assembly.GetManifestResourceNames()
			.FirstOrDefault(candidate => candidate.EndsWith("Strings.resources", StringComparison.Ordinal));

		Assert.NotNull(name);

		using var stream = assembly.GetManifestResourceStream(name!)!;
		using var reader = new ResourceReader(stream);

		return reader.Cast<DictionaryEntry>()
			.ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!, StringComparer.Ordinal);
	}

		[Fact]
	public void EveryKeyGroupThePluginReliesOnIsPresent()
	{
		var strings = ReadShippedStrings();

		string[] groups =
		[
			"Actions.Transport", "Actions.RestartTrack", "Actions.Skip", "Actions.SeekTo",
			"Actions.SetVolume", "Actions.VolumeStep", "Actions.SetMute",
			"Actions.ToggleLike", "Actions.AddToQueue", "Actions.RemoveFromQueue", "Actions.ClearQueue",
			"Actions.PlayUri", "Actions.PlayLikedSongs", "Actions.OpenPage",
			"Actions.OpenSpotify",
			"Actions.Errors",
			"Actions.Bridge.Install", "Actions.Bridge.Reinstall", "Actions.Bridge.Repair",
			"Actions.Bridge.Uninstall", "Actions.Bridge.Restart",
			"Actions.Bridge.ResetAuth", "Actions.Bridge.Errors",
			"Variables.IsPlaying", "Variables.PlaybackState", "Variables.CurrentPosition",
			"Variables.TrackDuration", "Variables.ProgressPercentage",
			"Variables.TrackName", "Variables.ArtistName", "Variables.AlbumName", "Variables.AlbumArtist",
			"Variables.PlaylistName", "Variables.PlaylistUri",
			"Variables.QueueNextTrackName", "Variables.QueueNextTrackUri",
			"Variables.QueuePreviousTrackName", "Variables.QueuePreviousTrackUri",
			"Variables.TrackUri", "Variables.AlbumUri", "Variables.ArtistUri", "Variables.CurrentTrackId",
			"Variables.TrackNumber", "Variables.DiscNumber", "Variables.Explicit", "Variables.TrackLiked",
			"Variables.Volume", "Variables.Shuffle", "Variables.RepeatMode", "Variables.Muted",
			"Variables.ConnectionStatus",
			"Variables.DeckConnectionStatus",
			"Player", "Ui.Settings", "Ui.Settings.WebSocket",
			"Issues.NoSpicetify", "Issues.NoBridge", "Issues.NotConnected",
			"Issues.Corrupted", "Issues.AuthFailed", "Issues.Unknown",
		];

		var present = strings.Keys.ToHashSet(StringComparer.Ordinal);

		var missing = groups
			.Where(group => !present.Any(key => key.StartsWith(group + ".", StringComparison.Ordinal)))
			.ToArray();

		Assert.True(missing.Length == 0, $"These key groups are missing from the shipped strings: {string.Join(", ", missing)}");
	}

		[Fact]
	public void NoShippedValueIsEmptyOrLooksLikeAnUnresolvedKey()
	{
		foreach (var (key, value) in ReadShippedStrings())
		{
			Assert.False(string.IsNullOrWhiteSpace(value), $"{key} has no value.");
			Assert.DoesNotContain("{{", value, StringComparison.Ordinal);
			Assert.False(value.StartsWith("__SPICETIFYDECK", StringComparison.Ordinal), $"{key} still holds a placeholder.");
		}
	}

		[Fact]
	public void EveryIssueGroupIsComplete()
	{
		var strings = ReadShippedStrings();

		string[] issues = ["NoSpicetify", "NoBridge", "NotConnected", "Corrupted", "AuthFailed"];

		foreach (var issue in issues)
		{
			foreach (var suffix in new[] { "Title", "Description", "Resolve", "Action" })
			{
				var key = $"Issues.{issue}.{suffix}";
				Assert.True(strings.ContainsKey(key), $"{key} is missing.");
				Assert.False(string.IsNullOrWhiteSpace(strings[key]), $"{key} has no value.");
			}
		}

		Assert.True(strings.ContainsKey("Issues.Unknown.Resolve"));
	}
}
