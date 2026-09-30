using Serilog;
using SpicetifyDeck.Bridge;
using Xunit;

namespace SpicetifyDeck.Tests;

public sealed class BridgeReconcileTests
{
	[Fact]
	public void AFilePointingAtADifferentPortIsNotCurrent()
	{

		using var harness = new GeneratorHarness();
		harness.WriteFile(endpoint: "ws://127.0.0.1:8976/spicetify/bridge/socket");

		Assert.False(harness.Generator.IsCurrent(harness.FilePath));
	}

	[Fact]
	public void AFileWithADifferentTokenIsNotCurrent()
	{
		using var harness = new GeneratorHarness();
		harness.WriteFile(endpoint: harness.Endpoint, token: new string('a', 64));

		Assert.False(harness.Generator.IsCurrent(harness.FilePath));
	}

	[Fact]
	public void AFileFromAnEarlierExtensionVersionIsNotCurrent()
	{

		using var harness = new GeneratorHarness();
		harness.WriteFile(endpoint: harness.Endpoint);
		File.AppendAllText(harness.FilePath, "\n// a line an earlier version did not have\n");

		Assert.False(harness.Generator.IsCurrent(harness.FilePath));
	}

	[Fact]
	public void AFileThatStillHoldsAPlaceholderIsNotCurrent()
	{
		using var harness = new GeneratorHarness();
		Directory.CreateDirectory(harness.Root);
		File.WriteAllText(harness.FilePath, "// " + BridgeScriptGenerator.EndpointPlaceholder);

		Assert.False(harness.Generator.IsCurrent(harness.FilePath));
	}

	[Fact]
	public void AnAbsentFileIsNotCurrent()
	{

		using var harness = new GeneratorHarness();

		Assert.False(harness.Generator.IsCurrent(harness.FilePath));
		Assert.False(harness.Generator.IsCurrent(harness.FilePath + ".missing"));
	}

	[Fact]
	public void TheFileThisRunWouldWriteIsCurrent()
	{

		using var harness = new GeneratorHarness();
		harness.WriteFile(endpoint: harness.Endpoint);

		Assert.True(harness.Generator.IsCurrent(harness.FilePath));
	}

	[Fact]
	public void AMatchingFileIsNotRewritten()
	{
		using var harness = new GeneratorHarness();
		harness.WriteFile(endpoint: harness.Endpoint);
		var before = File.GetLastWriteTimeUtc(harness.FilePath);

		harness.Generator.Write(harness.Root);

		Assert.Equal(before, File.GetLastWriteTimeUtc(harness.FilePath));
	}

	[Fact]
	public void AStaleFileIsRewrittenWithTheLiveAddressAndToken()
	{
		using var harness = new GeneratorHarness();
		harness.WriteFile(endpoint: "ws://127.0.0.1:8976/spicetify/bridge/socket", token: new string('a', 64));

		var written = harness.Generator.Write(harness.Root);

		Assert.NotNull(written);
		Assert.True(harness.Generator.IsCurrent(written));

		var content = File.ReadAllText(written);
		Assert.Contains(harness.Endpoint, content, StringComparison.Ordinal);
		Assert.Contains(harness.Token, content, StringComparison.Ordinal);
		Assert.DoesNotContain(new string('a', 64), content, StringComparison.Ordinal);
	}

	[Fact]
	public void ARefusedClientDoesNotChangeTheToken()
	{

		using var harness = new GeneratorHarness();
		var before = harness.Credentials.Token;

		Assert.Equal(before, harness.Credentials.Token);
		Assert.Equal(before, BridgeCredentials.Load(harness.DataDirectory, Serilog.Log.Logger).Token);
	}

	[Fact]
	public void RepeatedReloadsReuseTheSameStoredToken()
	{

		using var harness = new GeneratorHarness();
		var first = harness.Credentials.Token;

		for (var run = 0; run < 5; run++)
		{
			var reloaded = BridgeCredentials.Load(harness.DataDirectory, Serilog.Log.Logger);
			Assert.Equal(first, reloaded.Token);
			Assert.True(reloaded.HasStoredToken);
		}
	}

	[Fact]
	public void OnlyAnExplicitResetChangesTheToken()
	{

		using var harness = new GeneratorHarness();
		var before = harness.Credentials.Token;

		harness.Credentials.Reset();
		var after = harness.Credentials.Token;

		Assert.NotEqual(before, after);
		Assert.Equal(after, BridgeCredentials.Load(harness.DataDirectory, Serilog.Log.Logger).Token);
	}

	[Fact]
	public void RewritingForANewEndpointKeepsTheToken()
	{

		using var harness = new GeneratorHarness();
		harness.WriteFile(endpoint: "ws://127.0.0.1:9999/spicetify/bridge/socket");

		var written = harness.Generator.Write(harness.Root);
		var content = File.ReadAllText(written!);

		Assert.Equal(harness.Endpoint, ReadDeclaration(content, "var ENDPOINT = "));
		Assert.Equal(harness.Token, ReadDeclaration(content, "var TOKEN = "));
	}

	[Fact]
	public void TheTokenIsExactlySixtyFourLowerCaseHexCharacters()
	{

		using var harness = new GeneratorHarness();

		Assert.Matches("^[0-9a-f]{64}$", harness.Token);

		var written = harness.Generator.Write(harness.Root);

		var inScript = ReadDeclaration(File.ReadAllText(written!), "var TOKEN = ");
		Assert.Matches("^[0-9a-f]{64}$", inScript);
		Assert.Equal(harness.Credentials.Token, inScript);
	}

	[Fact]
	public void WhatIsPersistedIsWhatIsWrittenAndWhatIsAccepted()
	{

		using var harness = new GeneratorHarness();
		var written = harness.Generator.Write(harness.Root);

		var persisted = harness.Credentials.Token;
		var inBridge = ReadDeclaration(File.ReadAllText(written!), "var TOKEN = ");
		var reloaded = BridgeCredentials.Load(harness.DataDirectory, Serilog.Log.Logger).Token;

		Assert.Equal(persisted, inBridge);
		Assert.Equal(persisted, reloaded);
	}

		private static string ReadDeclaration(string content, string marker) =>
		System.Text.RegularExpressions.Regex
			.Match(content, System.Text.RegularExpressions.Regex.Escape(marker) + "\"(?<v>[^\"]*)\"")
			.Groups["v"].Value;

	[Fact]
	public void AClientHoldingAnOlderCopyIsNamedAsTheProblem()
	{

		using var harness = new GeneratorHarness();
		var client = harness.NewClientFolder();

		harness.PointServiceAt(harness.Root);
		harness.WriteSourceFile();

		Directory.CreateDirectory(Path.Combine(client, "Apps", "xpui", "extensions"));
		File.WriteAllText(
			Path.Combine(client, "Apps", "xpui", "extensions", BridgeScriptGenerator.FileName),
			"// a bridge from an earlier install, with a token nobody accepts any more\n");

		harness.Service.UseClientFolders([client]);

		var problem = harness.Service.Verify();

		Assert.NotNull(problem);
		Assert.Contains("older copy", problem, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("Repair", problem, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void AClientHoldingTheCurrentCopyIsNotReported()
	{
		using var harness = new GeneratorHarness();
		var client = harness.NewClientFolder();

		harness.PointServiceAt(harness.Root);
		harness.WriteSourceFile();
		var current = Path.Combine(harness.Root, "Extensions", BridgeScriptGenerator.FileName);

		Directory.CreateDirectory(Path.Combine(client, "Apps", "xpui", "extensions"));
		File.Copy(current, Path.Combine(client, "Apps", "xpui", "extensions", BridgeScriptGenerator.FileName));

		harness.Service.UseClientFolders([client]);

		Assert.Null(harness.Service.Verify());
	}

	[Fact]
	public async Task AnInstallOfAnAlreadySynchronizedBridgeChangesNothing()
	{

		using var harness = new GeneratorHarness();
		harness.PointServiceAt(harness.Root);
		harness.WriteSourceFile();

		var path = Path.Combine(harness.Root, "Extensions", BridgeScriptGenerator.FileName);
		var before = File.GetLastWriteTimeUtc(path);
		var token = harness.Token;

		var result = await harness.Service.InstallAsync(CancellationToken.None);

		Assert.True(result.Succeeded, result.Message);
		Assert.Contains("synchronized", result.Message, StringComparison.OrdinalIgnoreCase);
		Assert.Equal(token, harness.Token);
		Assert.Equal(before, File.GetLastWriteTimeUtc(path));
	}

	[Fact]
	public async Task RepairOfAnAlreadySynchronizedBridgeStillAppliesSpicetify()
	{
		using var harness = new GeneratorHarness();
		var fakeCli = harness.CreateSuccessfulCli();
		harness.PointServiceAt(harness.Root, fakeCli);
		harness.WriteSourceFile();
		harness.Service.UseClientFolders([]);
		harness.Service.UseSpotifyRunningProbe(() => false);

		Assert.True(harness.Service.IsSynchronized());

		var result = await harness.Service.RepairAsync(CancellationToken.None);

		Assert.True(result.Succeeded, result.Message);
		Assert.Contains(result.Steps, step => step.Contains("Forced repair", StringComparison.Ordinal));
		Assert.Contains(result.Steps, step => step.Contains("Applied the Spicetify configuration", StringComparison.Ordinal));
	}

	[Fact]
	public async Task RepairRewritesABrokenBridgeFileAndAppliesIt()
	{
		using var harness = new GeneratorHarness();
		var fakeCli = harness.CreateSuccessfulCli();
		harness.PointServiceAt(harness.Root, fakeCli);
		harness.WriteSourceFile();
		harness.Service.UseClientFolders([]);
		harness.Service.UseSpotifyRunningProbe(() => false);
		var sourcePath = Path.Combine(harness.Root, "Extensions", BridgeScriptGenerator.FileName);
		File.WriteAllText(sourcePath, "// broken bridge file");

		var result = await harness.Service.RepairAsync(CancellationToken.None);

		Assert.True(result.Succeeded, result.Message);
		Assert.True(harness.Generator.IsCurrent(sourcePath));
		Assert.Contains(result.Steps, step => step.Contains("Applied the Spicetify configuration", StringComparison.Ordinal));
	}

	[Fact]
	public async Task InstallingNeverRotatesTheToken()
	{

		using var harness = new GeneratorHarness();
		harness.PointServiceAt(harness.Root);
		harness.WriteSourceFile();

		var token = harness.Token;

		await harness.Service.InstallAsync(CancellationToken.None);

		Assert.Equal(token, harness.Credentials.Token);
		Assert.Equal(
			token,
			BridgeCredentials.Load(harness.DataDirectory, Serilog.Log.Logger).Token);
	}

	[Fact]
	public async Task AnUnappliedUpdateIsReportedAsPendingRatherThanApplied()
	{

		using var harness = new GeneratorHarness();
		var client = harness.NewClientFolder();

		harness.PointServiceAt(harness.Root);
		harness.WriteSourceFile();

		Directory.CreateDirectory(Path.Combine(client, "Apps", "xpui", "extensions"));
		File.WriteAllText(
			Path.Combine(client, "Apps", "xpui", "extensions", BridgeScriptGenerator.FileName),
			"// a bridge from an earlier install\n");

		harness.Service.UseClientFolders([client]);
		harness.Service.UseSpotifyRunningProbe(() => true);

		Assert.Equal(BridgeStatus.UpdatePending, harness.Service.Describe().Status);
	}

	[Fact]
	public void ALiveClientOutranksAPendingUpdate()
	{

		using var harness = new GeneratorHarness();
		var client = harness.NewClientFolder();

		harness.PointServiceAt(harness.Root);
		harness.WriteSourceFile();

		Directory.CreateDirectory(Path.Combine(client, "Apps", "xpui", "extensions"));
		File.WriteAllText(
			Path.Combine(client, "Apps", "xpui", "extensions", BridgeScriptGenerator.FileName),
			"// a bridge from an earlier install\n");

		harness.Service.UseClientFolders([client]);
		harness.Service.UseSpotifyRunningProbe(() => true);
		harness.Connections.MarkReportingForTest();

		var diagnostics = harness.Service.Describe();

		Assert.Equal(BridgeStatus.Connected, diagnostics.Status);
		Assert.False(diagnostics.IsUpToDate);
		Assert.True(diagnostics.IsInstalled);
	}

	[Fact]
	public void AClientThatNeverAsksForTheBridgeIsNamedAsTheProblem()
	{

		using var harness = new GeneratorHarness();
		var client = harness.NewClientFolder();

		harness.PointServiceAt(harness.Root);
		harness.WriteSourceFile();

		Directory.CreateDirectory(Path.Combine(client, "Apps", "xpui", "extensions"));
		File.Copy(
			Path.Combine(harness.Root, "Extensions", BridgeScriptGenerator.FileName),
			Path.Combine(client, "Apps", "xpui", "extensions", BridgeScriptGenerator.FileName));

		GeneratorHarness.WriteClientPage(client, referencesBridge: false);
		harness.Service.UseClientFolders([client]);

		var problem = harness.Service.Verify();

		Assert.NotNull(problem);
		Assert.Contains("not loading the bridge", problem, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("Repair", problem, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void AClientAskingForTheBridgeIsNotReported()
	{
		using var harness = new GeneratorHarness();
		harness.PointServiceAt(harness.Root);
		harness.WriteSourceFile();

		var client = harness.NewLoadedClient();
		harness.Service.UseClientFolders([client]);

		Assert.Null(harness.Service.Verify());
		Assert.True(harness.Service.IsSynchronized());
	}

	[Fact]
	public void AClientWithNoPageIsNotReported()
	{

		using var harness = new GeneratorHarness();
		var client = harness.NewClientFolder();

		harness.PointServiceAt(harness.Root);
		harness.WriteSourceFile();

		Directory.CreateDirectory(Path.Combine(client, "Apps", "xpui", "extensions"));
		File.Copy(
			Path.Combine(harness.Root, "Extensions", BridgeScriptGenerator.FileName),
			Path.Combine(client, "Apps", "xpui", "extensions", BridgeScriptGenerator.FileName));

		harness.Service.UseClientFolders([client]);

		Assert.Null(harness.Service.Verify());
	}

	[Fact]
	public void AClientNotAskingForTheBridgeIsNotSynchronized()
	{

		using var harness = new GeneratorHarness();
		harness.PointServiceAt(harness.Root);
		harness.WriteSourceFile();

		var client = harness.NewClientFolder();
		Directory.CreateDirectory(Path.Combine(client, "Apps", "xpui", "extensions"));
		File.Copy(
			Path.Combine(harness.Root, "Extensions", BridgeScriptGenerator.FileName),
			Path.Combine(client, "Apps", "xpui", "extensions", BridgeScriptGenerator.FileName));
		GeneratorHarness.WriteClientPage(client, referencesBridge: false);

		harness.Service.UseClientFolders([client]);

		Assert.False(harness.Service.IsSynchronized());
		Assert.False(harness.Service.Describe().IsUpToDate);
	}

	private sealed class GeneratorHarness : IDisposable
	{
		private static int NextPort = 31000 + Random.Shared.Next(0, 4000);

		public GeneratorHarness()
		{
			Root = Path.Combine(Path.GetTempPath(), "sd-recon-" + Guid.NewGuid().ToString("n"));
			Directory.CreateDirectory(Root);

			DataDirectory = Path.Combine(Root, "data");
			Directory.CreateDirectory(DataDirectory);

			Credentials = BridgeCredentials.Load(DataDirectory, Serilog.Log.Logger);
			BridgeEndpoint = new BridgeEndpoint(Serilog.Log.Logger);
			BridgeEndpoint.Bind(Interlocked.Increment(ref NextPort));
			Generator = new BridgeScriptGenerator(BridgeEndpoint, Credentials, Serilog.Log.Logger);
			Connections = new BridgeConnectionManager(Credentials, Serilog.Log.Logger);
			Service = new SpicetifyBridgeService(
				new SpicetifyLocator(Serilog.Log.Logger),
				new SpicetifyCli(Serilog.Log.Logger),
				Generator, Credentials, Connections, BridgeEndpoint, Serilog.Log.Logger);
		}

				public void WriteSourceFile(string? endpoint = null, string? token = null)
		{
			Directory.CreateDirectory(Path.Combine(Root, "Extensions"));
			var path = Path.Combine(Root, "Extensions", BridgeScriptGenerator.FileName);

			File.WriteAllText(
				path,
				Generator.Render()
					.Replace(BridgeScriptGenerator.EndpointPlaceholder, endpoint ?? Endpoint, StringComparison.Ordinal)
					.Replace(BridgeScriptGenerator.TokenPlaceholder, token ?? Token, StringComparison.Ordinal));
		}

				public string NewClientFolder()
		{
			var folder = Path.Combine(Root, "client-" + Guid.NewGuid().ToString("n")[..8]);
			Directory.CreateDirectory(folder);
			return folder;
		}

				public static void WriteClientPage(string client, bool referencesBridge)
		{
			var page = Path.Combine(client, "Apps", "xpui", "index.html");
			Directory.CreateDirectory(Path.GetDirectoryName(page)!);

			var script = referencesBridge
				? "<script defer src='extensions/spicetifydeck-bridge.js'></script>"
				: "<script defer src='extensions/marketplace/extension.js'></script>";

			File.WriteAllText(page, "<html><body><div id=\"app\"></div>" + script + "</body></html>");
		}

				public string NewLoadedClient()
		{
			var client = NewClientFolder();

			Directory.CreateDirectory(Path.Combine(client, "Apps", "xpui", "extensions"));
			File.Copy(
				Path.Combine(Root, "Extensions", BridgeScriptGenerator.FileName),
				Path.Combine(client, "Apps", "xpui", "extensions", BridgeScriptGenerator.FileName));
			WriteClientPage(client, referencesBridge: true);

			return client;
		}

				public void PointServiceAt(string spicetifyRoot, string? executablePath = null)
		{
			Directory.CreateDirectory(Path.Combine(spicetifyRoot, "Extensions"));

			var executable = executablePath ?? Path.Combine(spicetifyRoot, SpicetifyLocator.ExecutableName + ".exe");
			if (executablePath is null)
			{
				File.WriteAllText(executable, string.Empty);
			}
			File.WriteAllText(
				Path.Combine(spicetifyRoot, SpicetifyLocator.ConfigFileName),
				"extensions = " + BridgeScriptGenerator.FileName + "\n");

			Service.UseSpicetifyLocation(new SpicetifyLocation(executable, spicetifyRoot));
		}

		public string CreateSuccessfulCli()
		{
			var executable = Path.Combine(Root, OperatingSystem.IsWindows() ? "spicetify.cmd" : "spicetify");
			File.WriteAllText(executable, OperatingSystem.IsWindows()
				? "@echo off\r\nexit /b 0\r\n"
				: "#!/bin/sh\nexit 0\n");

			if (!OperatingSystem.IsWindows())
			{
				File.SetUnixFileMode(executable,
					UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
			}

			return executable;
		}

		public SpicetifyBridgeService Service { get; }

		public BridgeConnectionManager Connections { get; }

		public string Root { get; }

		public string DataDirectory { get; }

		public string FilePath => Path.Combine(Root, BridgeScriptGenerator.FileName);

		public string Endpoint => BridgeEndpoint.SocketUrl!;

		public string Token => Credentials.Token;

		public BridgeCredentials Credentials { get; }

		public BridgeEndpoint BridgeEndpoint { get; }

		public BridgeScriptGenerator Generator { get; }

				public void WriteFile(string endpoint, string? token = null)
		{
			Directory.CreateDirectory(Root);
			File.WriteAllText(
				FilePath,
				Generator.Render()
					.Replace(Endpoint, endpoint, StringComparison.Ordinal)
					.Replace(Token, token ?? Token, StringComparison.Ordinal));
		}

		public void Dispose()
		{
			try
			{
				Directory.Delete(Root, recursive: true);
			}
			catch (IOException)
			{

			}
		}
	}
}
