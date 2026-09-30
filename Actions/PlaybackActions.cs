using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using Serilog;
using SpicetifyDeck.Bridge;
using SpicetifyDeck.State;

namespace SpicetifyDeck.Actions;

public abstract class FixedSpotifyTransportAction(
	SpotifyStateManager state,
	BridgeConnectionManager bridge,
	ILogger logger) : SpicetifyActionBase(state, bridge, logger)
{
	protected abstract string CommandKind { get; }

	protected override Task<LocalizedText?> ExecuteAsync(ActionExecutionContext context) =>
		SendAsync(CommandKind, null, context.CancellationToken);
}

public sealed class PlayAction(SpotifyStateManager state, BridgeConnectionManager bridge, ILogger logger)
	: FixedSpotifyTransportAction(state, bridge, logger)
{
	public override string Id => "spicetify-play";
	public override LocalizedText Name => Strings.Actions.Transport.Play.Name();
	public override LocalizedText Description => Strings.Actions.Transport.Play.Description();
	protected override string CommandKind => BridgeCommandKind.Play;
}

public sealed class PauseAction(SpotifyStateManager state, BridgeConnectionManager bridge, ILogger logger)
	: FixedSpotifyTransportAction(state, bridge, logger)
{
	public override string Id => "spicetify-pause";
	public override LocalizedText Name => Strings.Actions.Transport.Pause.Name();
	public override LocalizedText Description => Strings.Actions.Transport.Pause.Description();
	protected override string CommandKind => BridgeCommandKind.Pause;
}

public sealed class TogglePlayAction(SpotifyStateManager state, BridgeConnectionManager bridge, ILogger logger)
	: FixedSpotifyTransportAction(state, bridge, logger)
{
	public override string Id => "spicetify-toggle-play";
	public override LocalizedText Name => Strings.Actions.Transport.TogglePlay.Name();
	public override LocalizedText Description => Strings.Actions.Transport.TogglePlay.Description();
	protected override string CommandKind => BridgeCommandKind.TogglePlay;
}

public sealed class NextAction(SpotifyStateManager state, BridgeConnectionManager bridge, ILogger logger)
	: FixedSpotifyTransportAction(state, bridge, logger)
{
	public override string Id => "spicetify-next";
	public override LocalizedText Name => Strings.Actions.Transport.Next.Name();
	public override LocalizedText Description => Strings.Actions.Transport.Next.Description();
	protected override string CommandKind => BridgeCommandKind.Next;
}

public sealed class PreviousAction(SpotifyStateManager state, BridgeConnectionManager bridge, ILogger logger)
	: FixedSpotifyTransportAction(state, bridge, logger)
{
	public override string Id => "spicetify-previous";
	public override LocalizedText Name => Strings.Actions.Transport.Previous.Name();
	public override LocalizedText Description => Strings.Actions.Transport.Previous.Description();
	protected override string CommandKind => BridgeCommandKind.Previous;
}

public sealed class RestartTrackAction(SpotifyStateManager state, BridgeConnectionManager bridge, ILogger logger)
	: SpicetifyActionBase(state, bridge, logger)
{
	public override string Id => "spicetify-restart-track";

	public override LocalizedText Name => Strings.Actions.RestartTrack.Name();

	public override LocalizedText Description => Strings.Actions.RestartTrack.Description();

	protected override Task<LocalizedText?> ExecuteAsync(ActionExecutionContext context) =>
		SendAsync(BridgeCommandKind.RestartTrack, null, context.CancellationToken);
}

public sealed class SkipAction(SpotifyStateManager state, BridgeConnectionManager bridge, ILogger logger)
	: SpicetifyActionBase(state, bridge, logger)
{
	public override string Id => "spicetify-skip";

	public override LocalizedText Name => Strings.Actions.Skip.Name();

	public override LocalizedText Description => Strings.Actions.Skip.Description();

	public override IReadOnlyList<ActionParameter> Parameters =>
	[
		ActionParameter.Slider("seconds", 1, 120, Strings.Actions.Skip.Seconds.Label(),
			Strings.Actions.Skip.Seconds.Description(), 1, 30),
		ActionParameter.Choice("direction", DirectionOptions, Strings.Actions.Skip.Direction.Label(),
			Strings.Actions.Skip.Direction.Description(), "forward", true),
	];

	private static IReadOnlyList<ActionParameterOption> DirectionOptions { get; } =
	[
		new() { Value = "forward", Label = Strings.Actions.Skip.Direction.Forward() },
		new() { Value = "backward", Label = Strings.Actions.Skip.Direction.Backward() },
	];

	protected override Task<LocalizedText?> ExecuteAsync(ActionExecutionContext context)
	{
		var milliseconds = ActionParameters.ReadDouble(context.Parameters, "seconds", 30) * 1000d;
		var forward = !string.Equals(ActionParameters.ReadString(context.Parameters, "direction"), "backward",
			StringComparison.OrdinalIgnoreCase);

		return SendAsync(
			forward ? BridgeCommandKind.SkipForward : BridgeCommandKind.SkipBackward,
			Payload("deltaMs", forward ? milliseconds : -milliseconds),
			context.CancellationToken);
	}
}

public sealed class SeekToAction(SpotifyStateManager state, BridgeConnectionManager bridge, ILogger logger)
	: SpicetifyActionBase(state, bridge, logger)
{
	public override string Id => "spicetify-seek-to";
	public override LocalizedText Name => Strings.Actions.SeekTo.Name();
	public override LocalizedText Description => Strings.Actions.SeekTo.Description();
	public override IReadOnlyList<ActionParameter> Parameters =>
	[
		ActionParameter.Choice("mode", ModeOptions, Strings.Actions.SeekTo.Mode.Label(),
			Strings.Actions.SeekTo.Mode.Description(), "percent", true),
		ActionParameter.Slider("value", 0, 100, Strings.Actions.SeekTo.Value.Label(),
			Strings.Actions.SeekTo.Value.Description(), 1, 50).OnlyWhen("mode", "percent"),
		ActionParameter.Number("seconds", Strings.Actions.SeekTo.Seconds.Label(),
			Strings.Actions.SeekTo.Seconds.Description(), 0, null, 1, 60).OnlyWhen("mode", "seconds"),
	];

	private static IReadOnlyList<ActionParameterOption> ModeOptions { get; } =
	[
		new() { Value = "percent", Label = Strings.Actions.SeekTo.Mode.Percent() },
		new() { Value = "seconds", Label = Strings.Actions.SeekTo.Mode.Seconds() },
	];

	protected override async Task<LocalizedText?> ExecuteAsync(ActionExecutionContext context)
	{
		TimeSpan position;
		if (string.Equals(ActionParameters.ReadString(context.Parameters, "mode"), "seconds", StringComparison.OrdinalIgnoreCase))
		{
			position = TimeSpan.FromSeconds(Math.Max(0, ActionParameters.ReadDouble(context.Parameters, "seconds", 60)));
		}
		else
		{
			if (State.Current.Duration is not { } duration)
			{
				return Strings.Actions.SeekTo.Errors.NoDuration();
			}

			var percent = Math.Clamp(ActionParameters.ReadDouble(context.Parameters, "value", 50), 0, 100);
			position = TimeSpan.FromMilliseconds(duration.TotalMilliseconds * percent / 100d);
		}

		return await SendAsync(BridgeCommandKind.SeekTo,
			Payload("positionMs", position.TotalMilliseconds), context.CancellationToken).ConfigureAwait(false);
	}
}

public sealed class SetVolumeAction(SpotifyStateManager state, BridgeConnectionManager bridge, ILogger logger)
	: SpicetifyActionBase(state, bridge, logger)
{
	public override string Id => "spicetify-set-volume";
	public override LocalizedText Name => Strings.Actions.SetVolume.Name();
	public override LocalizedText Description => Strings.Actions.SetVolume.Description();
	public override IReadOnlyList<ActionParameter> Parameters =>
	[
		ActionParameter.Slider("volume", 0, 100, Strings.Actions.SetVolume.Volume.Label(),
			Strings.Actions.SetVolume.Volume.Description(), 1, 50),
	];

	protected override Task<LocalizedText?> ExecuteAsync(ActionExecutionContext context)
	{
		var volume = (int)Math.Round(Math.Clamp(ActionParameters.ReadDouble(context.Parameters, "volume", 50), 0, 100));
		return SendAsync(BridgeCommandKind.SetVolume, Payload("volumePercent", volume), context.CancellationToken);
	}
}

public sealed class VolumeStepAction(SpotifyStateManager state, BridgeConnectionManager bridge, ILogger logger)
	: SpicetifyActionBase(state, bridge, logger)
{
	public override string Id => "spicetify-volume-step";
	public override LocalizedText Name => Strings.Actions.VolumeStep.Name();
	public override LocalizedText Description => Strings.Actions.VolumeStep.Description();
	public override IReadOnlyList<ActionParameter> Parameters =>
	[
		ActionParameter.Slider("step", 1, 50, Strings.Actions.VolumeStep.Step.Label(),
			Strings.Actions.VolumeStep.Step.Description(), 1, 10),
		ActionParameter.Choice("direction", DirectionOptions, Strings.Actions.VolumeStep.Direction.Label(),
			Strings.Actions.VolumeStep.Direction.Description(), "up", true),
	];

	private static IReadOnlyList<ActionParameterOption> DirectionOptions { get; } =
	[
		new() { Value = "up", Label = Strings.Actions.VolumeStep.Direction.Up() },
		new() { Value = "down", Label = Strings.Actions.VolumeStep.Direction.Down() },
	];

	protected override Task<LocalizedText?> ExecuteAsync(ActionExecutionContext context)
	{
		var up = !string.Equals(ActionParameters.ReadString(context.Parameters, "direction"), "down",
			StringComparison.OrdinalIgnoreCase);
		var current = State.Current.VolumePercent ?? (up ? 0 : 100);
		var step = ActionParameters.ReadDouble(context.Parameters, "step", 10);
		var volume = Math.Clamp(up ? current + step : current - step, 0, 100);
		return SendAsync(BridgeCommandKind.SetVolume, Payload("volumePercent", (int)Math.Round(volume)), context.CancellationToken);
	}
}

public sealed class SetMuteAction(SpotifyStateManager state, BridgeConnectionManager bridge, ILogger logger)
	: SpicetifyActionBase(state, bridge, logger)
{
	public override string Id => "spicetify-set-mute";

	public override LocalizedText Name => Strings.Actions.SetMute.Name();

	public override LocalizedText Description => Strings.Actions.SetMute.Description();

	public override IReadOnlyList<ActionParameter> Parameters =>
	[
		ActionParameter.Choice("mode", ModeOptions, Strings.Actions.SetMute.Mode.Label(),
			Strings.Actions.SetMute.Mode.Description(), "toggle", true),
	];

	private static IReadOnlyList<ActionParameterOption> ModeOptions { get; } =
	[
		new() { Value = "toggle", Label = Strings.Actions.SetMute.Mode.Toggle() },
		new() { Value = "mute", Label = Strings.Actions.SetMute.Mode.Mute() },
		new() { Value = "unmute", Label = Strings.Actions.SetMute.Mode.Unmute() },
	];

	protected override Task<LocalizedText?> ExecuteAsync(ActionExecutionContext context)
	{
		var mode = ActionParameters.ReadString(context.Parameters, "mode") ?? "toggle";
		var current = State.Current;
		var muted = mode switch
		{
			"mute" => true,
			"unmute" => false,
			_ => !(current.Muted ?? current.VolumePercent == 0),
		};

		return SendAsync(BridgeCommandKind.SetMute, Payload("muted", muted), context.CancellationToken);
	}
}

public sealed class ToggleLikeAction(SpotifyStateManager state, BridgeConnectionManager bridge, ILogger logger)
	: SpicetifyActionBase(state, bridge, logger)
{
	public override string Id => "spicetify-toggle-like";

	public override LocalizedText Name => Strings.Actions.ToggleLike.Name();

	public override LocalizedText Description => Strings.Actions.ToggleLike.Description();

	public override IReadOnlyList<ActionParameter> Parameters =>
	[
		ActionParameter.Choice("mode", ModeOptions, Strings.Actions.ToggleLike.Mode.Label(),
			Strings.Actions.ToggleLike.Mode.Description(), "toggle", true),
	];

	private static IReadOnlyList<ActionParameterOption> ModeOptions { get; } =
	[
		new() { Value = "toggle", Label = Strings.Actions.ToggleLike.Mode.Toggle() },
		new() { Value = "like", Label = Strings.Actions.ToggleLike.Mode.Like() },
		new() { Value = "unlike", Label = Strings.Actions.ToggleLike.Mode.Unlike() },
	];

	protected override async Task<LocalizedText?> ExecuteAsync(ActionExecutionContext context)
	{
		if (State.Current.TrackUri is null)
		{
			return Strings.Actions.Errors.NoCurrentTrack();
		}

		return ActionParameters.ReadString(context.Parameters, "mode") switch
		{
			"like" => await SendAsync(BridgeCommandKind.SetLiked, Payload("liked", true), context.CancellationToken)
				.ConfigureAwait(false),
			"unlike" => await SendAsync(BridgeCommandKind.SetLiked, Payload("liked", false), context.CancellationToken)
				.ConfigureAwait(false),
			_ => await SendAsync(BridgeCommandKind.ToggleLiked, null, context.CancellationToken).ConfigureAwait(false),
		};
	}
}

public sealed class AddToQueueAction(SpotifyStateManager state, BridgeConnectionManager bridge, ILogger logger)
	: SpicetifyActionBase(state, bridge, logger)
{
	public override string Id => "spicetify-add-to-queue";

	public override LocalizedText Name => Strings.Actions.AddToQueue.Name();

	public override LocalizedText Description => Strings.Actions.AddToQueue.Description();

	public override IReadOnlyList<ActionParameter> Parameters =>
	[
		ActionParameter.Text("uri", Strings.Actions.AddToQueue.Uri.Label(), Strings.Actions.AddToQueue.Uri.Description(),
			Strings.Actions.AddToQueue.Uri.Placeholder()),
	];

	protected override async Task<LocalizedText?> ExecuteAsync(ActionExecutionContext context)
	{
		var uri = ActionParameters.ReadString(context.Parameters, "uri");
		var target = string.IsNullOrWhiteSpace(uri) ? State.Current.TrackUri : SpotifyUris.ForKind(uri, "track");

		return target is null
			? Strings.Actions.Errors.NoCurrentTrack()
			: await SendAsync(BridgeCommandKind.AddToQueue, Payload("uri", target), context.CancellationToken)
				.ConfigureAwait(false);
	}
}

public sealed class RemoveFromQueueAction(SpotifyStateManager state, BridgeConnectionManager bridge, ILogger logger)
	: SpicetifyActionBase(state, bridge, logger)
{
	public override string Id => "spicetify-remove-from-queue";

	public override LocalizedText Name => Strings.Actions.RemoveFromQueue.Name();

	public override LocalizedText Description => Strings.Actions.RemoveFromQueue.Description();

	public override IReadOnlyList<ActionParameter> Parameters =>
	[
		ActionParameter.Text("uri", Strings.Actions.RemoveFromQueue.Uri.Label(),
			Strings.Actions.RemoveFromQueue.Uri.Description(), Strings.Actions.RemoveFromQueue.Uri.Placeholder()),
	];

	protected override async Task<LocalizedText?> ExecuteAsync(ActionExecutionContext context)
	{
		if (Guard(State.Current.Capabilities.RemoveFromQueue, Strings.Actions.RemoveFromQueue.NoSupport()) is { } missing)
		{
			return missing;
		}

		var uri = ActionParameters.ReadString(context.Parameters, "uri");
		var target = string.IsNullOrWhiteSpace(uri) ? State.Current.TrackUri : SpotifyUris.ForKind(uri, "track");

		return target is null
			? Strings.Actions.Errors.NoCurrentTrack()
			: await SendAsync(BridgeCommandKind.RemoveFromQueue, Payload("uri", target), context.CancellationToken)
				.ConfigureAwait(false);
	}
}

public sealed class ClearQueueAction(SpotifyStateManager state, BridgeConnectionManager bridge, ILogger logger)
	: SpicetifyActionBase(state, bridge, logger)
{
	public override string Id => "spicetify-clear-queue";

	public override LocalizedText Name => Strings.Actions.ClearQueue.Name();

	public override LocalizedText Description => Strings.Actions.ClearQueue.Description();

	protected override async Task<LocalizedText?> ExecuteAsync(ActionExecutionContext context)
	{
		if (Guard(State.Current.Capabilities.ClearQueue, Strings.Actions.ClearQueue.NoSupport()) is { } missing)
		{
			return missing;
		}

		return await SendAsync(BridgeCommandKind.ClearQueue, null, context.CancellationToken).ConfigureAwait(false);
	}
}
