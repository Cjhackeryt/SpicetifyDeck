using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Serilog;

namespace SpicetifyDeck.Bridge;

public sealed class PluginEndpoint(ILogger logger)
{
	private readonly ILogger _logger = logger.ForContext<PluginEndpoint>();
	private string? _baseAddress;

		public string? BaseAddress => Volatile.Read(ref _baseAddress);

		public string? SocketUrl =>
		BaseAddress is { } address ? string.Create(CultureInfo.InvariantCulture, $"{address.Replace("http", "ws", StringComparison.Ordinal)}{BridgeEndpoints.SocketPath}") : null;

		public void Resolve(IServer? server)
	{
		var addresses = server?.Features.Get<IServerAddressesFeature>()?.Addresses;
		var chosen = addresses?.FirstOrDefault(IsLoopback);

		if (chosen is null)
		{
			_logger.Warning(
				"No loopback address was found, so the Spicetify bridge cannot be reached. " +
				"Playback and variables will report no connection.");
			return;
		}

		Volatile.Write(ref _baseAddress, chosen.TrimEnd('/'));
		_logger.Information("The Spicetify bridge listens on {SocketUrl}.", SocketUrl);
	}

		private static bool IsLoopback(string address)
	{
		if (!Uri.TryCreate(address, UriKind.Absolute, out var uri))
		{
			return false;
		}

		if (uri.Host is "0.0.0.0" or "+" or "::" or "[::]")
		{
			return false;
		}

		return IPAddress.TryParse(uri.Host, out var parsed)
			? IPAddress.IsLoopback(parsed)
			: uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
	}

		public const int SocketPort = 0;
}
