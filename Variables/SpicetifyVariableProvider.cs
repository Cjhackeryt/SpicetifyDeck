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
		private readonly System.Collections.Concurrent.ConcurrentDictionary<string, object?> _lastStatusValues =
		new(StringComparer.OrdinalIgnoreCase);

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

		return ValueTask.FromResult(VariableReading.Of(value, Minimum(variable), Maximum(variable), Step(variable)));
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
			value = null;
		}

		if (value is null)
		{
			return _lastStatusValues.TryGetValue(variable.LocalId, out var remembered)
				? VariableReading.Of(remembered)
				: VariableReading.Unavailable;
		}

		_lastStatusValues[variable.LocalId] = value;
		return VariableReading.Of(value);
	}

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
				if (!TryReadNumber(value, out var milliseconds))
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
		"%" or "ms" => 0,
		"x" => 0.25,
		_ => null,
	};

	private static double? Maximum(SpicetifyVariable variable) => variable.Unit switch
	{
		"%" => 100,
		"x" => 3,
		_ => null,
	};

	private static double? Step(SpicetifyVariable variable) => variable.Unit switch
	{
		"%" => 1,
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
