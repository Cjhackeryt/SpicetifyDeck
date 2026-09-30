using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Serilog;
using SpicetifyDeck.Bridge;
using SpicetifyDeck.State;
using Xunit;

namespace SpicetifyDeck.Tests;

public sealed class BridgeConnectionTests : IAsyncDisposable
{
		private static int NextPort = 20000 + Random.Shared.Next(0, 8000);

	private readonly BridgeListener _listener;
	private readonly BridgeConnectionManager _connections;
	private readonly BridgeCredentials _credentials;
	private readonly BridgeEndpoint _endpoint;
	private readonly BridgeScriptGenerator _generator;
	private readonly string _data = Path.Combine(Path.GetTempPath(), "sd-bridge-" + Guid.NewGuid().ToString("n"));

	public BridgeConnectionTests()
	{
		Directory.CreateDirectory(_data);

		_credentials = BridgeCredentials.Load(_data, Serilog.Log.Logger);
		_endpoint = new BridgeEndpoint(Serilog.Log.Logger);
		_connections = new BridgeConnectionManager(_credentials, Serilog.Log.Logger);
		_generator = new BridgeScriptGenerator(_endpoint, _credentials, Serilog.Log.Logger);
		_listener = new BridgeListener(
			_endpoint,
			_connections,
			_generator,
			_credentials,
			new State.PluginSettings { BridgePort = Interlocked.Increment(ref NextPort) },
			Serilog.Log.Logger);
	}

	public async ValueTask DisposeAsync()
	{
		await _listener.DisposeAsync();

		try
		{
			if (Directory.Exists(_data))
			{
				Directory.Delete(_data, recursive: true);
			}
		}
		catch (IOException)
		{

		}
	}

	[Fact]
	public async Task TheListenerBindsLoopbackOnlyAndReportsTheAddress()
	{
		await _listener.StartAsync(CancellationToken.None);

		Assert.True(_endpoint.IsBound);
		Assert.Equal(BridgeConnectionState.Connecting, _connections.ConnectionState);

		Assert.Contains("127.0.0.1", _endpoint.SocketUrl!, StringComparison.Ordinal);
		Assert.EndsWith(BridgeEndpoints.SocketPath, _endpoint.SocketUrl!, StringComparison.Ordinal);

		using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
		var body = await http.GetStringAsync(
			$"http://127.0.0.1:{_endpoint.Port!.Value}{BridgeEndpoints.StatusPath}");

		Assert.Contains("\"connected\":false", body, StringComparison.Ordinal);
	}

	[Fact]
	public async Task ABusyPortIsSkippedRatherThanFatal()
	{

		using var occupied = new TcpListener(IPAddress.Loopback, 0);
		occupied.Start();
		var taken = ((IPEndPoint)occupied.LocalEndpoint).Port;

		var settings = new State.PluginSettings { BridgePort = taken };
		var endpoint = new BridgeEndpoint(Serilog.Log.Logger);
		var credentials = BridgeCredentials.Load(_data, Serilog.Log.Logger);
		var connections = new BridgeConnectionManager(credentials, Serilog.Log.Logger);
		var generator = new BridgeScriptGenerator(endpoint, credentials, Serilog.Log.Logger);

		await using var listener = new BridgeListener(
			endpoint, connections, generator, credentials, settings, Serilog.Log.Logger);
		await listener.StartAsync(CancellationToken.None);

		Assert.True(endpoint.IsBound);
		Assert.NotEqual(taken, endpoint.Port);
	}

	[Fact]
	public async Task ATransientCollisionDoesNotMoveTheAddressOnTheNextStart()
	{

		var data = Path.Combine(Path.GetTempPath(), "sd-stick-" + Guid.NewGuid().ToString("n"));
		Directory.CreateDirectory(data);

		var preferred = Interlocked.Increment(ref NextPort);
		var credentials = BridgeCredentials.Load(data, Serilog.Log.Logger);
		int fallback;

		{
			using var blocker = new TcpListener(IPAddress.Loopback, preferred);
			blocker.Start();

			var endpoint = new BridgeEndpoint(Serilog.Log.Logger);
			var connections = new BridgeConnectionManager(credentials, Serilog.Log.Logger);
			var generator = new BridgeScriptGenerator(endpoint, credentials, Serilog.Log.Logger);

			await using var listener = new BridgeListener(
				endpoint,
				connections,
				generator,
				credentials,
				new State.PluginSettings { BridgePort = preferred },
				Serilog.Log.Logger);

			await listener.StartAsync(CancellationToken.None);
			Assert.NotEqual(preferred, endpoint.Port);

			fallback = endpoint.Port!.Value;
		}

		{
			var second = new BridgeEndpoint(Serilog.Log.Logger);
			var secondConnections = new BridgeConnectionManager(credentials, Serilog.Log.Logger);
			var secondGenerator = new BridgeScriptGenerator(second, credentials, Serilog.Log.Logger);

			await using var restarted = new BridgeListener(
				second,
				secondConnections,
				secondGenerator,
				credentials,
				new State.PluginSettings { BridgePort = preferred },
				Serilog.Log.Logger);

			await restarted.StartAsync(CancellationToken.None);

			Assert.Equal(fallback, second.Port);
		}

		Directory.Delete(data, recursive: true);
	}

	[Fact]
	public async Task TheAddressDoesNotChangeBetweenBinds()
	{

		await _listener.StartAsync(CancellationToken.None);
		var first = _endpoint.SocketUrl;

		await _listener.StopAsync(CancellationToken.None);
		Assert.Equal(BridgeConnectionState.Disconnected, _connections.ConnectionState);
		await _listener.StartAsync(CancellationToken.None);
		Assert.Equal(BridgeConnectionState.Connecting, _connections.ConnectionState);

		Assert.Equal(first, _endpoint.SocketUrl);
	}

	[Fact]
	public async Task AClientPresentingTheTokenIsAcceptedAndItsStateIsRead()
	{
		await _listener.StartAsync(CancellationToken.None);
		var state = new TaskCompletionSource<BridgeState>(TaskCreationOptions.RunContinuationsAsynchronously);
		_connections.StateReceived += (report, _) =>
		{
			state.TrySetResult(report);
			return Task.CompletedTask;
		};

		using var client = await ConnectAsync();
		await SendAsync(client, new BridgeHello { Token = _credentials.Token, BridgeVersion = "2.1.0" });
		await ReadAsync(client, "welcome");
		Assert.Equal(BridgeConnectionState.Connecting, _connections.ConnectionState);

		await SendAsync(client, new BridgeStateMessage
		{
			State = new BridgeState
			{
				Playback = new BridgePlayback { IsPlaying = true },
				Track = new BridgeTrack { Name = "Test", Uri = "spotify:track:x" },
			},
		});

		var report = await state.Task.WaitAsync(TimeSpan.FromSeconds(10));
		Assert.True(report.Playback?.IsPlaying);
		Assert.Equal("Test", report.Track?.Name);
		Assert.Equal(BridgeConnectionState.Connected, _connections.ConnectionState);
	}

	[Fact]
	public async Task AClientPresentingTheWrongTokenIsRefusedAndNothingIsRead()
	{
		await _listener.StartAsync(CancellationToken.None);
		var anyState = false;
		_connections.StateReceived += (_, _) =>
		{
			anyState = true;
			return Task.CompletedTask;
		};

		using var client = await ConnectAsync();
		await SendAsync(client, new BridgeHello { Token = new string('0', 64) });

		var reply = await ReadAsync(client, "auth-failed");
		Assert.Contains("auth-failed", reply, StringComparison.Ordinal);
		var close = await client.ReceiveAsync(new ArraySegment<byte>(new byte[256]), CancellationToken.None)
			.WaitAsync(TimeSpan.FromSeconds(10));
		Assert.Equal(WebSocketMessageType.Close, close.MessageType);
		Assert.Equal(WebSocketCloseStatus.PolicyViolation, client.CloseStatus);

		await Task.Delay(300);
		Assert.False(anyState);
		Assert.True(_connections.HasAuthenticationFailure);
		Assert.Equal(BridgeConnectionState.AuthenticationFailed, _connections.ConnectionState);
	}

	[Fact]
	public async Task StateBeforeAnyHelloIsRefused()
	{
		await _listener.StartAsync(CancellationToken.None);
		var anyState = false;
		_connections.StateReceived += (_, _) =>
		{
			anyState = true;
			return Task.CompletedTask;
		};

		using var client = await ConnectAsync();
		await SendAsync(client, new BridgeStateMessage
		{
			State = new BridgeState { Playback = new BridgePlayback { IsPlaying = true } },
		});

		var reply = await ReadAsync(client, "auth-failed");
		Assert.Contains("auth-failed", reply, StringComparison.Ordinal);
		await Task.Delay(300);
		Assert.False(anyState);
	}

	[Fact]
	public async Task ACommandRoundTripsThroughTheClientAndItsResultComesBack()
	{
		await _listener.StartAsync(CancellationToken.None);
		_connections.StateReceived += (_, _) => Task.CompletedTask;

		using var client = await ConnectAsync();
		await SendAsync(client, new BridgeHello { Token = _credentials.Token });
		await ReadAsync(client, "welcome");

		var command = _connections.SendAsync(
			BridgeCommandKind.Ping,
			null,
			TimeSpan.FromSeconds(10),
			CancellationToken.None);

		var commandText = await ReadAsync(client, "commands");
		Assert.Contains("commands", commandText, StringComparison.Ordinal);

		var id = JsonDocument.Parse(commandText).RootElement
			.GetProperty("commands")[0].GetProperty("id").GetString()!;

		await SendAsync(client, new BridgeStateMessage
		{
			State = new BridgeState { Playback = new BridgePlayback { IsPlaying = true } },
			Results = new Dictionary<string, BridgeCommandOutcome>(StringComparer.Ordinal)
			{
				[id] = new() { Ok = true },
			},
		});

		var result = await command.WaitAsync(TimeSpan.FromSeconds(10));
		Assert.True(result.Ok);
	}

	[Fact]
	public async Task TheRenderedScriptCarriesTheLiveAddressAndTokenAndNoPlaceholders()
	{
		await _listener.StartAsync(CancellationToken.None);
		var script = _generator.Render();

		Assert.Contains(_endpoint.SocketUrl!, script, StringComparison.Ordinal);
		Assert.Contains(_credentials.Token, script, StringComparison.Ordinal);
		Assert.DoesNotContain(BridgeScriptGenerator.EndpointPlaceholder, script, StringComparison.Ordinal);
		Assert.DoesNotContain(BridgeScriptGenerator.TokenPlaceholder, script, StringComparison.Ordinal);
	}

	[Fact]
	public async Task TheStatusPageAnswersOnTheBridgesOwnPort()
	{
		await _listener.StartAsync(CancellationToken.None);
		var port = _endpoint.Port!.Value;

		using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
		var body = await http.GetStringAsync($"http://127.0.0.1:{port}{BridgeEndpoints.StatusPath}");

		Assert.Contains("\"connected\":false", body, StringComparison.Ordinal);
		Assert.Contains(_endpoint.SocketUrl!, body, StringComparison.Ordinal);
	}

		private static async Task<string> ReadAsync(ClientWebSocket client, string marker)
	{
		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		var buffer = new byte[16 * 1024];

		while (true)
		{
			var result = await client.ReceiveAsync(buffer, timeout.Token);
			if (result.MessageType == WebSocketMessageType.Close)
			{
				Assert.Fail($"The server closed the connection while waiting for {marker}.");
			}

			var text = Encoding.UTF8.GetString(buffer, 0, result.Count);
			if (result.EndOfMessage)
			{
				return text;
			}

			Assert.Fail($"Expected {marker} but the first frame did not contain it.");
		}
	}

	private async Task<ClientWebSocket> ConnectAsync()
	{
		var client = new ClientWebSocket();
		await client.ConnectAsync(new Uri(_endpoint.SocketUrl!), CancellationToken.None);
		return client;
	}

	private static async Task SendAsync(ClientWebSocket client, object payload)
	{
		var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, BridgeJson.Options);
		await client.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None);
	}
}
