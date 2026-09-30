using Serilog;

namespace SpicetifyDeck.Bridge;

public sealed class BridgeEndpoint(ILogger logger)
{
		public const int DefaultPort = 8975;

		public const int PortScanCount = 10;

	private readonly ILogger _logger = logger.ForContext<BridgeEndpoint>();
	private readonly Lock _gate = new();
	private int? _port;

		public int? Port
	{
		get
		{
			lock (_gate)
			{
				return _port;
			}
		}
	}

		public string? SocketUrl
	{
		get
		{
			lock (_gate)
			{
				return _port is { } port
					? $"ws://127.0.0.1:{port}{BridgeEndpoints.SocketPath}"
					: null;
			}
		}
	}

		public bool IsBound => Port is not null;

		public void Bind(int port)
	{
		lock (_gate)
		{
			_port = port;
		}

		_logger.Information("The Spicetify bridge listens on {SocketUrl}.", SocketUrl);
	}

		public void Release()
	{
		lock (_gate)
		{
			_port = null;
		}
	}
}
