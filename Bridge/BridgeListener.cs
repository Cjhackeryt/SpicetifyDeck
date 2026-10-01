using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using SpicetifyDeck.State;

namespace SpicetifyDeck.Bridge;

internal sealed class BridgeListener(
	BridgeEndpoint endpoint,
	BridgeConnectionManager connections,
	BridgeScriptGenerator generator,
	BridgeCredentials credentials,
	PluginSettings settings,
	Serilog.ILogger logger) : IHostedService, IAsyncDisposable
{
	private static readonly TimeSpan WebSocketKeepAliveInterval = TimeSpan.FromSeconds(15);
	private static readonly TimeSpan WebSocketKeepAliveTimeout = TimeSpan.FromSeconds(45);

	private readonly Serilog.ILogger _logger = logger.ForContext<BridgeListener>();
	private WebApplication? _app;
	private int _disposed;

	public async Task StartAsync(CancellationToken cancellationToken)
	{
		if (Volatile.Read(ref _disposed) != 0 || _app is not null)
		{
			return;
		}

		var first = settings.BridgePort;
		var port = 0;

		var remembered = credentials.RememberedPort;
		if (remembered is { } previous && previous != first)
		{
			if (await TryBindAsync(previous, cancellationToken).ConfigureAwait(false))
			{
				port = previous;
			}
			else
			{
				_logger.Warning(
					"Port {Port} is still in use, so the bridge is moving off the address the installed file holds.",
					previous);
			}
		}

		for (var attempt = 0; port == 0 && attempt < BridgeEndpoint.PortScanCount; attempt++)
		{
			var candidate = first + attempt;
			if (await TryBindAsync(candidate, cancellationToken).ConfigureAwait(false))
			{
				port = candidate;
			}
		}

		if (port == 0)
		{
			_logger.Error(
				"Could not bind any port from {First} to {Last}, so the Spicetify bridge cannot accept connections.",
				first,
				first + BridgeEndpoint.PortScanCount - 1);
			return;
		}

		if (port != first)
		{
			_logger.Warning(
				"Port {First} was already in use, so the bridge is on {Actual} instead. The bridge file will be written for that port.",
				first,
				port);
		}

		credentials.RememberPort(port);
		endpoint.Bind(port);
		connections.ListenerStarted();
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		if (_app is { } app)
		{
			_app = null;
			endpoint.Release();
			connections.ListenerStopped();

			try
			{
				await app.StopAsync(CancellationToken.None).ConfigureAwait(false);
			}
			catch (Exception exception) when (exception is ObjectDisposedException or OperationCanceledException)
			{

			}

			await app.DisposeAsync().ConfigureAwait(false);
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		await StopAsync(CancellationToken.None).ConfigureAwait(false);
	}

		private async Task<bool> TryBindAsync(int port, CancellationToken cancellationToken)
	{
		var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
		{
			ApplicationName = typeof(BridgeListener).Assembly.GetName().Name,
			ContentRootPath = AppContext.BaseDirectory,
		});

		builder.Logging.ClearProviders();

		builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, port));

		var app = builder.Build();
		app.UseWebSockets(new WebSocketOptions
		{
			KeepAliveInterval = WebSocketKeepAliveInterval,
			KeepAliveTimeout = WebSocketKeepAliveTimeout,
		});
		Map(app);

		try
		{
			await app.StartAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (Exception exception) when (IsAddressInUse(exception))
		{
			await app.DisposeAsync().ConfigureAwait(false);
			return false;
		}

		_app = app;
		return true;
	}

		private static bool IsAddressInUse(Exception exception) => exception switch
	{
		AddressInUseException => true,
		SocketException socket => socket.SocketErrorCode is SocketError.AddressAlreadyInUse or SocketError.AccessDenied,
		_ => exception.InnerException is not null && IsAddressInUse(exception.InnerException),
	};

		private void Map(WebApplication app)
	{
		app.MapGet(BridgeEndpoints.SocketPath, async (HttpContext http) =>
		{
			if (!http.WebSockets.IsWebSocketRequest)
			{
				http.Response.StatusCode = StatusCodes.Status400BadRequest;
				await http.Response.WriteAsync("This endpoint expects a WebSocket.").ConfigureAwait(false);
				return;
			}

			using var socket = await http.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
			await connections.ServeAsync(socket, http.RequestAborted).ConfigureAwait(false);
		});

		app.MapGet(BridgeEndpoints.ScriptPath, async (HttpContext http) =>
		{
			if (endpoint.SocketUrl is null)
			{
				http.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
				await http.Response.WriteAsync("The bridge listener is not running.").ConfigureAwait(false);
				return;
			}

			var script = generator.Render();
			http.Response.ContentType = "text/javascript; charset=utf-8";
			await http.Response.WriteAsync(script).ConfigureAwait(false);
		});

		app.MapGet(BridgeEndpoints.StatusPath, (HttpContext http) =>
		{
			http.Response.ContentType = "application/json";
			return http.Response.WriteAsJsonAsync(new
			{
				connected = connections.IsConnected,
				reporting = connections.IsReporting,
				socketUrl = endpoint.SocketUrl,
			});
		});
	}
}
