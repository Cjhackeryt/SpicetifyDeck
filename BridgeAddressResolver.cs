using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.Hosting;
using Serilog;
using SpicetifyDeck.Bridge;

namespace SpicetifyDeck;

internal sealed class BridgeAddressResolver(
	PluginEndpoint endpoint,
	SpicetifyBridgeService bridge,
	BridgeScriptGenerator generator,
	BridgeCredentials credentials,
	IServer server,
	IHostApplicationLifetime lifetime,
	ILogger logger) : IHostedService
{
	public Task StartAsync(CancellationToken cancellationToken)
	{
		lifetime.ApplicationStarted.Register(Resolve);
		return Task.CompletedTask;
	}

	public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

		private void Resolve()
	{
		try
		{
			endpoint.Resolve(server);

			var socketUrl = endpoint.SocketUrl;
			if (socketUrl is null)
			{
				return;
			}

			if (!credentials.EndpointChanged(socketUrl))
			{
				return;
			}

			var previous = credentials.Endpoint;
			var path = generator.Write(bridge.Location.ExtensionsDirectory);
			if (path is null)
			{
				logger.Warning(
					"The bridge endpoint moved from {Previous} to {Current} and the bridge file could not be updated.",
					previous,
					socketUrl);
				return;
			}

			credentials.RecordInstall(socketUrl, BridgeScriptGenerator.TemplateVersion ?? "unknown", bridge.Location.Root ?? string.Empty);
			bridge.MarkApplyRequired();

			logger.Information(
				"The bridge endpoint moved from {Previous} to {Current}. The bridge file was updated, " +
				"so run spicetify apply (or the Reinstall Spicetify Bridge action) and restart Spotify.",
				previous,
				socketUrl);
		}
		catch (Exception exception)
		{
			logger.Warning(exception, "Could not determine the Spicetify bridge address.");
		}
	}
}
