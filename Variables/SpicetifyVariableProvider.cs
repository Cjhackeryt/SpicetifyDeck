using System.Text.Json;
using MacroDeck.Sdk;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Variables;
using Serilog;
using SpicetifyDeck.Bridge;
using SpicetifyDeck.State;

namespace SpicetifyDeck.Variables;

public sealed class SpicetifyVariableProvider : IVariableProvider
{
	private readonly SpotifyStateManager _state;
	private readonly BridgeConnectionManager _bridge;
	private readonly SpicetifyBridgeService _bridgeService;
	private readonly ILogger _logger;

	public SpicetifyVariableProvider(
		SpotifyStateManager state,
		BridgeConnectionManager bridge,
		SpicetifyBridgeService bridgeService,
		ILogger logger)
	{
		_state = state;
		_bridge = bridge;
		_bridgeService = bridgeService;
		_logger = logger.ForContext<SpicetifyVariableProvider>();
	}

	public IReadOnlyList<VariableDefinition> Variables =>
		[.. SpicetifyVariableCatalog.Definitions, .. DeckStatusVariableCatalog.Definitions];

	public string CatalogName => SpicetifyValues.PluginName;

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default)
	{

		if (DeckStatusVariableCatalog.Find(localId) is { } status)
		{
			return ValueTask.FromResult(ReadStatus(status));
		}

		var variable = SpicetifyVariableCatalog.Find(localId);
		if (variable is null)
		{
			return ValueTask.FromResult(VariableReading.Unavailable);
		}

		object? value;
		var snapshot = _state.Current;
		try
		{
			value = variable.Read(snapshot);
		}
		catch (Exception exception)
		{
			_logger.Debug(exception, "Reading the variable {Variable} failed.", variable.Name);
			return ValueTask.FromResult(VariableReading.Unavailable);
		}

		if (value is null)
		{
			if (variable.Type == VariableType.Text
				&& snapshot.ConnectionState == BridgeConnectionState.Connecting
				&& _state.LastUpdate is null)
			{
				return ValueTask.FromResult(VariableReading.Of(SpicetifyValues.WaitingForSpicetify));
			}

			return ValueTask.FromResult(VariableReading.Unavailable);
		}

		return ValueTask.FromResult(VariableReading.Of(value, Minimum(variable), Maximum(variable, snapshot), Step(variable)));
	}

		private VariableReading ReadStatus(DeckStatusVariable variable)
	{
		object? value;
		try
		{
			value = variable.Read(_bridgeService);
		}
		catch (Exception exception)
		{
			_logger.Debug(exception, "Reading the bridge variable {Variable} failed.", variable.Name);
			return VariableReading.Of(ConnectionFallback(_bridge.ConnectionState));
		}

		if (value is null)
		{
			return VariableReading.Of(ConnectionFallback(_bridge.ConnectionState));
		}

		return VariableReading.Of(value);
	}

	internal static string ConnectionFallback(BridgeConnectionState state) => state switch
	{
		BridgeConnectionState.Connected => "Bridge Connected",
		BridgeConnectionState.Connecting or BridgeConnectionState.Authenticating => "Waiting for Spotify",
		BridgeConnectionState.AuthenticationFailed => "Authentication Failed",
		_ => "Bridge Disconnected",
	};

		public async ValueTask<VariableWriteResult> SetValueAsync(
		string localId, object? value, CancellationToken cancellationToken = default)
	{
		var variable = SpicetifyVariableCatalog.Find(localId);
		if (variable is null || !variable.Writable)
		{
			return VariableWriteResult.NotWritable();
		}

		if (!_state.IsConnected)
		{
			return VariableWriteResult.Unavailable();
		}

		switch (variable.Name)
		{
			case "progress_seconds":
			{
				if (!TryReadNumber(value, out var seconds))
				{
					return VariableWriteResult.InvalidValue();
				}

				if (_state.Current.Duration is not { } duration || duration <= TimeSpan.Zero)
				{
					return VariableWriteResult.Unavailable();
				}

				var positionMs = Math.Clamp(seconds, 0, duration.TotalSeconds) * 1000d;
				return await SendAsync(
					BridgeCommandKind.SeekTo,
					new Dictionary<string, object?> { ["positionMs"] = positionMs },
					cancellationToken).ConfigureAwait(false);
			}

			case "progress_percentage":
			{
				if (!TryReadNumber(value, out var percent))
				{
					return VariableWriteResult.InvalidValue();
				}

				if (_state.Current.Duration is not { } duration || duration <= TimeSpan.Zero)
				{
					return VariableWriteResult.Unavailable();
				}

				var positionMs = duration.TotalMilliseconds * Math.Clamp(percent, 0, 100) / 100d;
				return await SendAsync(
					BridgeCommandKind.SeekTo,
					new Dictionary<string, object?> { ["positionMs"] = positionMs },
					cancellationToken).ConfigureAwait(false);
			}

			case "volume":
			{
				if (!TryReadNumber(value, out var percent))
				{
					return VariableWriteResult.InvalidValue();
				}

				return await SendAsync(
					BridgeCommandKind.SetVolume,
					new Dictionary<string, object?> { ["volumePercent"] = (int)Math.Round(Math.Clamp(percent, 0, 100)) },
					cancellationToken).ConfigureAwait(false);
			}

			case "current_position":
			{
				if (!TryReadPosition(value, out var milliseconds))
				{
					return VariableWriteResult.InvalidValue();
				}

				return await SendAsync(
					BridgeCommandKind.SeekTo,
					new Dictionary<string, object?> { ["positionMs"] = Math.Max(0, milliseconds) },
					cancellationToken).ConfigureAwait(false);
			}

			case "shuffle":
			{
				if (!TryReadBoolean(value, out var shuffle))
				{
					return VariableWriteResult.InvalidValue();
				}

				return await SendAsync(
					BridgeCommandKind.SetShuffle,
					new Dictionary<string, object?> { ["enabled"] = shuffle },
					cancellationToken).ConfigureAwait(false);
			}

			case "repeat_mode":
			{
				var mode = RepeatValue(value?.ToString());
				return await SendAsync(
					BridgeCommandKind.SetRepeat,
					new Dictionary<string, object?> { ["mode"] = mode },
					cancellationToken).ConfigureAwait(false);
			}

			case "muted":
			{
				if (!TryReadBoolean(value, out var muted))
				{
					return VariableWriteResult.InvalidValue();
				}

				return await SendAsync(
					BridgeCommandKind.SetMute,
					new Dictionary<string, object?> { ["muted"] = muted },
					cancellationToken).ConfigureAwait(false);
			}

			default:
				return VariableWriteResult.NotWritable();
		}
	}

	private async ValueTask<VariableWriteResult> SendAsync(
		string kind, IReadOnlyDictionary<string, object?>? payload, CancellationToken cancellationToken)
	{
		var result = await _bridge.SendAsync(kind, payload, null, cancellationToken).ConfigureAwait(false);
		return result.Ok ? VariableWriteResult.Applied() : VariableWriteResult.Failed(result.Error);
	}

		private static string RepeatValue(string? value) => value switch
	{
		"Track" or "track" => "track",
		"Context" or "context" => "context",
		_ => "off",
	};

	private static double? Minimum(SpicetifyVariable variable) => variable.Unit switch
	{
		"%" or "s" or "ms" => 0,
		"x" => 0.25,
		_ => null,
	};

	private static double? Maximum(SpicetifyVariable variable, SpotifySnapshot snapshot) => variable.Unit switch
	{
		"%" => 100,
		"s" => snapshot.Duration?.TotalSeconds,
		"x" => 3,
		_ => null,
	};

	private static double? Step(SpicetifyVariable variable) => variable.Unit switch
	{
		"%" => 1,
		"s" => 1,
		"ms" => 1000,
		"x" => 0.05,
		_ => null,
	};

	private static bool TryReadNumber(object? value, out double number)
	{
		switch (value)
		{
			case double d:
				number = d;
				return double.IsFinite(d);
			case float f:
				number = f;
				return float.IsFinite(f);
			case int i:
				number = i;
				return true;
			case long l:
				number = l;
				return true;
			case decimal m:
				number = (double)m;
				return true;
			case string text:
				return double.TryParse(text, System.Globalization.NumberStyles.Any,
					System.Globalization.CultureInfo.InvariantCulture, out number);
			case JsonElement { ValueKind: JsonValueKind.Number } element:
				number = element.GetDouble();
				return double.IsFinite(number);
			default:
				number = double.NaN;
				return false;
		}
	}

	private static bool TryReadPosition(object? value, out double milliseconds)
	{
		var text = value switch
		{
			string stringValue => stringValue,
			JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
			_ => null,
		};

		if (text is not null)
		{
			var parts = text.Split(':');
			if (parts.Length == 2
				&& long.TryParse(parts[0], System.Globalization.NumberStyles.None,
					System.Globalization.CultureInfo.InvariantCulture, out var minutes)
				&& int.TryParse(parts[1], System.Globalization.NumberStyles.None,
					System.Globalization.CultureInfo.InvariantCulture, out var seconds)
				&& minutes >= 0 && seconds is >= 0 and < 60)
			{
				milliseconds = (minutes * 60d + seconds) * 1000d;
				return true;
			}
		}

		return TryReadNumber(value, out milliseconds);
	}

	private static bool TryReadBoolean(object? value, out bool state)
	{
		switch (value)
		{
			case bool b:
				state = b;
				return true;
			case string text when bool.TryParse(text, out var parsed):
				state = parsed;
				return true;
			case JsonElement { ValueKind: JsonValueKind.True }:
				state = true;
				return true;
			case JsonElement { ValueKind: JsonValueKind.False }:
				state = false;
				return true;
			default:
				state = false;
				return false;
		}
	}
}
