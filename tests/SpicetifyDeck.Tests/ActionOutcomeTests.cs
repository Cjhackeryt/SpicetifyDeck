using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using SpicetifyDeck.Actions;
using SpicetifyDeck.Bridge;
using SpicetifyDeck.State;
using Xunit;

namespace SpicetifyDeck.Tests;

public sealed class ActionOutcomeTests
{
	private static BridgeConnectionManager NewBridge() =>
		new(BridgeCredentials.Load(Path.GetTempPath(), Serilog.Log.Logger), Serilog.Log.Logger);

	private static SpotifyStateManager NewState(BridgeConnectionManager bridge) =>
		new(bridge, new PluginSettings(), Serilog.Log.Logger);

	private static ActionExecutionContext Context() => new()
	{
		Parameters = new Dictionary<string, object>(StringComparer.Ordinal),
		CancellationToken = CancellationToken.None,
	};

	[Fact]
	public void GuardYieldsNothingAtAllWhenTheCapabilityIsThere()
	{

		Assert.Null(ProbeAction.GuardForTest(supported: true, "message"));
	}

	[Fact]
	public void GuardYieldsTheMessageWhenTheCapabilityIsMissing()
	{
		Assert.NotNull(ProbeAction.GuardForTest(supported: false, "message"));
	}

	[Fact]
	public async Task AGuardedActionSucceedsWhenTheCapabilityIsThere()
	{
		var bridge = NewBridge();
		var action = new ProbeAction(NewState(bridge), bridge, capabilityPresent: true);

		var result = await action.CreateExecutor().ExecuteAsync(Context());

		Assert.Equal(ActionResultStatus.Succeeded, result.Status);
	}

	[Fact]
	public async Task AGuardedActionFailsWithAReasonWhenTheCapabilityIsMissing()
	{
		var bridge = NewBridge();
		var action = new ProbeAction(NewState(bridge), bridge, capabilityPresent: false);

		var result = await action.CreateExecutor().ExecuteAsync(Context());

		Assert.Equal(ActionResultStatus.Failed, result.Status);
		Assert.False(
			result.ErrorMessage.IsEmpty,
			"A failure must carry a message, or the host shows 'The action failed without saying why'.");
	}

	[Fact]
	public async Task ARefusedBridgeCommandFailsWithAReason()
	{

		var bridge = NewBridge();
		var action = new SendProbeAction(NewState(bridge), bridge);

		var result = await action.CreateExecutor().ExecuteAsync(Context());

		Assert.Equal(ActionResultStatus.Failed, result.Status);
		Assert.False(
			result.ErrorMessage.IsEmpty,
			"A refused command must say why, not just that it failed.");
	}

	private sealed class ProbeAction(SpotifyStateManager state, BridgeConnectionManager bridge, bool capabilityPresent)
		: SpicetifyActionBase(state, bridge, Serilog.Log.Logger)
	{
		public override string Id => "probe-guarded";

		public override LocalizedText Name => "Probe";

		public override LocalizedText Description => "Probe";

		internal static LocalizedText? GuardForTest(bool supported, string what) =>
			Guard(supported, LocalizedText.FromLiteral(what));

		protected override Task<LocalizedText?> ExecuteAsync(ActionExecutionContext context) =>
			Task.FromResult(Guard(capabilityPresent, LocalizedText.FromLiteral("not supported")));
	}

	private sealed class SendProbeAction(SpotifyStateManager state, BridgeConnectionManager bridge)
		: SpicetifyActionBase(state, bridge, Serilog.Log.Logger)
	{
		public override string Id => "probe-send";

		public override LocalizedText Name => "Probe";

		public override LocalizedText Description => "Probe";

		protected override Task<LocalizedText?> ExecuteAsync(ActionExecutionContext context) =>
			SendAsync(BridgeCommandKind.Ping, null, context.CancellationToken);
	}
}
