using Serilog;
using SpicetifyDeck.Bridge;
using Xunit;

namespace SpicetifyDeck.Tests;

public sealed class BridgeInstallationTests : IDisposable
{
	private readonly string _root = Path.Combine(
		Path.GetTempPath(),
		"spicetifydeck-tests-" + Guid.NewGuid().ToString("n"));

	public void Dispose()
	{
		try
		{
			if (Directory.Exists(_root))
			{
				Directory.Delete(_root, recursive: true);
			}
		}
		catch (IOException)
		{

		}
	}

	[Fact]
	public void TheTokenSurvivesAReload()
	{

		Directory.CreateDirectory(_root);

		var first = BridgeCredentials.Load(_root, Log.Logger);
		var second = BridgeCredentials.Load(_root, Log.Logger);

		Assert.Equal(first.Token, second.Token);
	}

	[Fact]
	public void AResetProducesADifferentToken()
	{
		Directory.CreateDirectory(_root);

		var credentials = BridgeCredentials.Load(_root, Log.Logger);
		var before = credentials.Token;
		credentials.Reset();

		Assert.NotEqual(before, credentials.Token);
		Assert.Equal(credentials.Token, BridgeCredentials.Load(_root, Log.Logger).Token);
	}

	[Fact]
	public void ACorruptCredentialsFileMintsAFreshTokenRatherThanFailing()
	{
		Directory.CreateDirectory(_root);
		File.WriteAllText(Path.Combine(_root, BridgeCredentials.FileName), "{ this is not json");

		var credentials = BridgeCredentials.Load(_root, Log.Logger);

		Assert.Equal(64, credentials.Token.Length);
		Assert.False(credentials.HasStoredToken);
	}

	[Fact]
	public void ATokenOfTheWrongShapeIsNotTrusted()
	{
		Directory.CreateDirectory(_root);
		File.WriteAllText(Path.Combine(_root, BridgeCredentials.FileName), """{"token":"short"}""");

		Assert.Equal(64, BridgeCredentials.Load(_root, Log.Logger).Token.Length);
	}

	[Fact]
	public void TheEndpointIsOnlyReportedAsChangedWhenItReallyMoved()
	{
		Directory.CreateDirectory(_root);
		var credentials = BridgeCredentials.Load(_root, Log.Logger);

		Assert.False(credentials.EndpointChanged("ws://127.0.0.1:5000/spicetify/bridge/socket"));

		credentials.RecordInstall("ws://127.0.0.1:5000/spicetify/bridge/socket", "2.0.0", _root);
		Assert.False(credentials.EndpointChanged("ws://127.0.0.1:5000/spicetify/bridge/socket"));
		Assert.True(credentials.EndpointChanged("ws://127.0.0.1:6000/spicetify/bridge/socket"));
	}

	[Fact]
	public void RemovingAnExtensionRewritesTheListAndLeavesTheOthersAlone()
	{

		Directory.CreateDirectory(_root);
		var path = Path.Combine(_root, SpicetifyLocator.ConfigFileName);
		File.WriteAllText(path,
			"spotify_path           = C:/Spotify\n" +
			"current_theme          = \n" +
			"extensions             = popupLyrics.js|spicetifydeck-bridge.js|loopyLoop.js\n" +
			"custom_apps            = \n" +
			"always_enable_devtools = 0\n");

		Assert.True(SpicetifyConfig.RemoveExtension(_root, "spicetifydeck-bridge.js"));

		var content = File.ReadAllText(path);
		Assert.Equal(["popupLyrics.js", "loopyLoop.js"], SpicetifyConfig.ReadExtensions(_root));

		Assert.Contains("spotify_path           = C:/Spotify", content, StringComparison.Ordinal);
		Assert.Contains("current_theme          = ", content, StringComparison.Ordinal);
		Assert.Contains("custom_apps            = ", content, StringComparison.Ordinal);
		Assert.Contains("always_enable_devtools = 0", content, StringComparison.Ordinal);
	}

	[Fact]
	public void RemovingTheLastExtensionLeavesAnEmptyValueRatherThanRemovingTheLine()
	{
		Directory.CreateDirectory(_root);
		var path = Path.Combine(_root, SpicetifyLocator.ConfigFileName);
		File.WriteAllText(path, "extensions = spicetifydeck-bridge.js\ncurrent_theme = \n");

		Assert.True(SpicetifyConfig.RemoveExtension(_root, "spicetifydeck-bridge.js"));

		Assert.Empty(SpicetifyConfig.ReadExtensions(_root));
		Assert.Contains("current_theme", File.ReadAllText(path), StringComparison.Ordinal);
	}

	[Fact]
	public void RemovingSomethingThatIsNotThereChangesNothing()
	{
		Directory.CreateDirectory(_root);
		var path = Path.Combine(_root, SpicetifyLocator.ConfigFileName);
		const string original = "extensions = popupLyrics.js\n";
		File.WriteAllText(path, original);

		Assert.True(SpicetifyConfig.RemoveExtension(_root, "spicetifydeck-bridge.js"));
		Assert.Equal(original, File.ReadAllText(path));
	}

	[Fact]
	public void AConfigWithNoExtensionsLineIsNotRewritten()
	{

		Directory.CreateDirectory(_root);
		var path = Path.Combine(_root, SpicetifyLocator.ConfigFileName);
		const string original = "current_theme = \n";
		File.WriteAllText(path, original);

		Assert.False(SpicetifyConfig.RemoveExtension(_root, "spicetifydeck-bridge.js"));
		Assert.Equal(original, File.ReadAllText(path));
	}

	[Fact]
	public void RemovingFromAMissingConfigIsNotAnError()
	{
		Assert.False(SpicetifyConfig.RemoveExtension(Path.Combine(_root, "nowhere"), "spicetifydeck-bridge.js"));
		Assert.False(SpicetifyConfig.RemoveExtension(null, "spicetifydeck-bridge.js"));
	}

	[Theory]
	[InlineData("a.js|b.js", 2)]
	[InlineData("  a.js | b.js  ", 2)]
	[InlineData("", 0)]
	[InlineData("|", 0)]
	[InlineData("a.js|a.js", 1)]
	public void TheConfiguredListIsSplitOnPipes(string value, int expected)
	{
		Assert.Equal(expected, SpicetifyConfig.ParseList(value).Count);
	}

	[Fact]
	public void TheConfiguredListIsReadFromTheConfigFileWithoutRunningTheCli()
	{

		Directory.CreateDirectory(_root);
		File.WriteAllText(
			Path.Combine(_root, SpicetifyLocator.ConfigFileName),
			"current_theme = \nextensions = popupLyrics.js|spicetifydeck-bridge.js\ncustom_apps = \n");

		Assert.True(SpicetifyConfig.IsExtensionEnabled(_root));
		Assert.Equal(["popupLyrics.js", "spicetifydeck-bridge.js"], SpicetifyConfig.ReadExtensions(_root));
	}

	[Fact]
	public void AConfigFileWithoutTheSettingMeansNotEnabled()
	{
		Directory.CreateDirectory(_root);
		File.WriteAllText(Path.Combine(_root, SpicetifyLocator.ConfigFileName), "current_theme = \n");

		Assert.False(SpicetifyConfig.IsExtensionEnabled(_root));
	}

	[Fact]
	public void AMissingConfigFileMeansNotEnabledRatherThanThrowing()
	{
		Assert.False(SpicetifyConfig.IsExtensionEnabled(Path.Combine(_root, "nowhere")));
		Assert.False(SpicetifyConfig.IsExtensionEnabled(null));
	}

	[Fact]
	public void TheLocatorPrefersTheDirectoryHoldingTheConfigFile()
	{

		var root = new SpicetifyLocation("C:/nowhere/spicetify.exe", _root);

		Assert.True(root.IsInstalled);
		Assert.Equal(Path.Combine(_root, "Extensions"), root.ExtensionsDirectory);
		Assert.Equal(Path.Combine(_root, SpicetifyLocator.ConfigFileName), root.ConfigPath);
	}

	[Fact]
	public void AnAbsentCliYieldsALocationWithNoRoot()
	{
		var root = new SpicetifyLocation(null, null);

		Assert.False(root.IsInstalled);
		Assert.Null(root.ExtensionsDirectory);
	}

	[Fact]
	public void AFailedCommandSummarisesWhatTheCliSaid()
	{
		var failure = new CliResult(false, "spicetify v2.45.1\nError: spotify_path is invalid\n", 1);

		Assert.Contains("spotify_path", failure.Summary, StringComparison.Ordinal);
		Assert.DoesNotContain("spicetify v2", failure.Summary, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void ASucceededCommandHasNoSummaryToShow()
	{
		Assert.Equal(string.Empty, new CliResult(true, "all good", 0).Summary);
	}

	[Fact]
	public void ATimedOutCommandSaysSoRatherThanReportingAnExitCode()
	{
		Assert.Equal("The command did not finish in time.", new CliResult(false, string.Empty, -1, TimedOut: true).Summary);
	}

	[Fact]
	public void EveryStatusHasAWordAndOnlyConnectedCountsAsInstalled()
	{

		Assert.All(
			Enum.GetValues<BridgeStatus>().Select(status => new BridgeDiagnostics(status, null, false, null, null, false)),
			diagnostics => Assert.False(string.IsNullOrWhiteSpace(diagnostics.StatusName)));

		Assert.False(new BridgeDiagnostics(BridgeStatus.SpicetifyNotInstalled, null, false, null, null, false).IsInstalled);
		Assert.False(new BridgeDiagnostics(BridgeStatus.BridgeNotInstalled, null, false, null, null, false).IsInstalled);
		Assert.True(new BridgeDiagnostics(BridgeStatus.Corrupted, null, false, null, null, false).IsInstalled);
		Assert.True(new BridgeDiagnostics(BridgeStatus.WaitingForSpotify, null, false, null, null, false).IsInstalled);
		Assert.True(new BridgeDiagnostics(BridgeStatus.Connected, null, false, null, null, false).IsInstalled);
	}
}
