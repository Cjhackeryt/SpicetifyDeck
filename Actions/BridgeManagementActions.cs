using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using Serilog;
using SpicetifyDeck.Bridge;
using SpicetifyDeck.State;

namespace SpicetifyDeck.Actions;

public abstract class BridgeActionBase(
	SpotifyStateManager state,
	BridgeConnectionManager bridge,
	SpicetifyBridgeService service,
	ILogger logger) : SpicetifyActionBase(state, bridge, logger)
{
	protected SpicetifyBridgeService Service { get; } = service;

	protected static async Task<LocalizedText?> RunAsync(
		Func<CancellationToken, Task<BridgeOperationResult>> operation,
		CancellationToken cancellationToken)
	{
		var result = await operation(cancellationToken).ConfigureAwait(false);
		if (result.Succeeded)
		{
			return null;
		}

		return LocalizedText.FromLiteral(result.Message ?? Strings.Actions.Bridge.Errors.Failed().ToString());
	}
}

public sealed class InstallBridgeAction(
	SpotifyStateManager state,
	BridgeConnectionManager bridge,
	SpicetifyBridgeService service,
	ILogger logger)
	: BridgeActionBase(state, bridge, service, logger)
{
	public override string Id => "spicetify-install-bridge";

	public override LocalizedText Name => Strings.Actions.Bridge.Install.Name();

	public override LocalizedText Description => Strings.Actions.Bridge.Install.Description();

	protected override Task<LocalizedText?> ExecuteAsync(ActionExecutionContext context) =>
		RunAsync(token => Service.InstallAsync(token), context.CancellationToken);
}

public sealed class ReinstallBridgeAction(
	SpotifyStateManager state,
	BridgeConnectionManager bridge,
	SpicetifyBridgeService service,
	ILogger logger)
	: BridgeActionBase(state, bridge, service, logger)
{
	public override string Id => "spicetify-reinstall-bridge";

	public override LocalizedText Name => Strings.Actions.Bridge.Reinstall.Name();

	public override LocalizedText Description => Strings.Actions.Bridge.Reinstall.Description();

	protected override Task<LocalizedText?> ExecuteAsync(ActionExecutionContext context) =>
		RunAsync(token => Service.InstallAsync(token), context.CancellationToken);
}

public sealed class RepairBridgeAction(
	SpotifyStateManager state,
	BridgeConnectionManager bridge,
	SpicetifyBridgeService service,
	ILogger logger)
	: BridgeActionBase(state, bridge, service, logger)
{
	public override string Id => "spicetify-repair-bridge";

	public override LocalizedText Name => Strings.Actions.Bridge.Repair.Name();

	public override LocalizedText Description => Strings.Actions.Bridge.Repair.Description();

	protected override async Task<LocalizedText?> ExecuteAsync(ActionExecutionContext context)
	{
		Bridge.ClearAuthenticationFailure();
		return await RunAsync(token => Service.RepairAsync(token), context.CancellationToken).ConfigureAwait(false);
	}
}

public sealed class UninstallBridgeAction(
	SpotifyStateManager state,
	BridgeConnectionManager bridge,
	SpicetifyBridgeService service,
	ILogger logger)
	: BridgeActionBase(state, bridge, service, logger)
{
	public override string Id => "spicetify-uninstall-bridge";

	public override LocalizedText Name => Strings.Actions.Bridge.Uninstall.Name();

	public override LocalizedText Description => Strings.Actions.Bridge.Uninstall.Description();

	protected override Task<LocalizedText?> ExecuteAsync(ActionExecutionContext context) =>
		RunAsync(Service.UninstallAsync, context.CancellationToken);
}

public sealed class RestartBridgeAction(
	SpotifyStateManager state,
	BridgeConnectionManager bridge,
	SpicetifyBridgeService service,
	ILogger logger)
	: BridgeActionBase(state, bridge, service, logger)
{
	public override string Id => "spicetify-restart-bridge";

	public override LocalizedText Name => Strings.Actions.Bridge.Restart.Name();

	public override LocalizedText Description => Strings.Actions.Bridge.Restart.Description();

	protected override async Task<LocalizedText?> ExecuteAsync(ActionExecutionContext context)
	{
		await Service.RestartAsync().ConfigureAwait(false);
		return null;
	}
}

public sealed class ResetBridgeAuthenticationAction(
	SpotifyStateManager state,
	BridgeConnectionManager bridge,
	SpicetifyBridgeService service,
	ILogger logger)
	: BridgeActionBase(state, bridge, service, logger)
{
	public override string Id => "spicetify-reset-bridge-auth";

	public override LocalizedText Name => Strings.Actions.Bridge.ResetAuth.Name();

	public override LocalizedText Description => Strings.Actions.Bridge.ResetAuth.Description();

	protected override async Task<LocalizedText?> ExecuteAsync(ActionExecutionContext context)
	{
		Bridge.ClearAuthenticationFailure();
		return await RunAsync(token => Service.ResetAuthenticationAsync(token), context.CancellationToken)
			.ConfigureAwait(false);
	}
}
