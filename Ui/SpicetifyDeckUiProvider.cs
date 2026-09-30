using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using Serilog;
using SpicetifyDeck.Bridge;
using SpicetifyDeck.Variables;

namespace SpicetifyDeck.Ui;

public sealed class SpicetifyDeckUiProvider : IUiProvider
{
	private readonly SpicetifyBridgeService _bridge;
	private readonly ILogger _logger;

	public SpicetifyDeckUiProvider(SpicetifyBridgeService bridge, ILogger logger)
	{
		_bridge = bridge;
		_logger = logger.ForContext<SpicetifyDeckUiProvider>();
	}

		public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
	[
		new() { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Shared },
	];

	public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken) =>
		Task.FromResult<IUiSession?>(

			string.Equals(request.Surface.Kind, UiSurfaceKinds.Config, StringComparison.Ordinal)
				? new SettingsSession(_bridge, _logger)
				: null);
}

internal sealed class SettingsSession : IUiSession
{
	private readonly SpicetifyBridgeService _bridge;
	private readonly ILogger _logger;
	private readonly UiState<int> _revision = new(0);
	private readonly UiView _view;
	private int _disposed;

	public SettingsSession(SpicetifyBridgeService bridge, ILogger logger)
	{
		_bridge = bridge;
		_logger = logger.ForContext<SettingsSession>();

		var surface = new UiSurface
		{
			Kind = UiSurfaceKinds.Config,
			SessionMode = UiSessionModes.Shared,
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
			{
				[UiConfigSurfaceAttributes.EntryPoint] = Json(UiConfigEntryPoints.IntegrationConfig),
				[UiConfigSurfaceAttributes.IntegrationId] = Json(SpicetifyValues.PluginName),
			},
		};

		_view = new UiView(surface, Build());
	}

	public event EventHandler? Changed;

	#pragma warning disable CS0067
	public event EventHandler<UiSessionFaultedEventArgs>? Faulted;
#pragma warning restore CS0067

	public UiTree BuildTree() => _view.Tree;

	public IReadOnlyList<UiPatch> DrainPatches() => _view.DrainPatches();

	public void Dispatch(UiEvent uiEvent) => _view.Dispatch(uiEvent);

	public ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 0)
		{
			_view.Dispose();
		}

		return ValueTask.CompletedTask;
	}

		private UiStack Build() => new()
	{
		Key = "root",
		Direction = UiValue.Of("vertical"),
		Gap = UiSize.Of(12),
		Padding = UiSize.Of(16),
		Children =
		[
			new UiHeading { Key = "title", Text = Strings.Ui.Settings.Title() },
			new UiProse { Key = "intro", Text = Strings.Ui.Settings.Intro() },

			Status("spicetify", () => Strings.Ui.Settings.SpicetifyCli.Label(),
				() => YesNo(Describe().SpicetifyVersion is not null)),
			Status("spicetify-version", () => Strings.Ui.Settings.SpicetifyVersion.Label(),
				() => Or(Describe().SpicetifyVersion, Strings.Ui.Settings.NotAvailable())),
			Status("bridge", () => Strings.Ui.Settings.Bridge.Label(),
				() => YesNo(Describe().IsInstalled)),

			Status("bridge-applied", () => Strings.Ui.Settings.BridgeApplied.Label(),
				() => YesNo(Describe().IsApplied)),
			Status("bridge-current", () => Strings.Ui.Settings.BridgeCurrent.Label(),
				() => YesNo(Describe().IsUpToDate)),
			Status("bridge-version", () => Strings.Ui.Settings.BridgeVersion.Label(),
				() => Or(Describe().BridgeVersion, Strings.Ui.Settings.NotAvailable())),
			Status("websocket", () => Strings.Ui.Settings.WebSocket.Label(),
				() => Strings.Ui.Settings.WebSocket.State(Describe().StatusName)),
			Status("spotify", () => Strings.Ui.Settings.Spotify.Label(),
				() => YesNo(SpicetifyBridgeService.IsSpotifyRunning)),
			Status("extension", () => Strings.Ui.Settings.Extension.Label(),
				() => YesNo(Describe().IsExtensionEnabled)),
			Status("path", () => Strings.Ui.Settings.Path.Label(),
				() => Or(_bridge.InstalledFilePath, Strings.Ui.Settings.NotAvailable())),

			Notice(),

			new UiStack
			{
				Key = "actions",
				Direction = UiValue.Of("row"),
				Gap = UiSize.Of(8),
				Children =
				[
					Button("install", Strings.Ui.Settings.InstallButton(), Operation.Install),
					Button("repair", Strings.Ui.Settings.RepairButton(), Operation.Repair),
					Button("reset", Strings.Ui.Settings.ResetButton(), Operation.Reset),
					Button("test", Strings.Ui.Settings.TestButton(), Operation.Test),
					Button("uninstall", Strings.Ui.Settings.UninstallButton(), Operation.Uninstall),
				],
			},
		],
	};

		private UiStatus Status(string key, Func<LocalizedText?> label, Func<LocalizedText?> value) => new()
	{
		Key = key,
		Label = Text(label),
		Value = Text(value),
	};

		private UiElement Notice()
	{
		if (Describe().RequiresApply)
		{
			return Banner(Strings.Ui.Settings.RequiresApply());
		}

		return Describe().LastError is { } error
			? Banner(LocalizedText.FromLiteral(error))
			: new UiFragment { Key = "notice" };
	}

	private static UiBanner Banner(LocalizedText text) => new()
	{
		Key = "notice",
		Severity = UiValue.Of("warning"),
		Text = text,
	};

	private UiButton Button(string key, LocalizedString label, Operation operation) => new()
	{
		Key = $"button-{key}",
		Corner = UiValue.Of("rounded"),
		Padding = UiSize.Of(10),
		Events = [UiEventHandler.OnAsync("press", token => RunAsync(operation, token))],
		Children = [new UiTextRun { Key = $"label-{key}", Text = label }],
	};

		private async Task RunAsync(Operation operation, CancellationToken cancellationToken)
	{
		try
		{
			switch (operation)
			{

				case Operation.Install:
				case Operation.Repair:
					await _bridge.InstallAsync(cancellationToken).ConfigureAwait(false);
					break;

				case Operation.Reset:
					await _bridge.ResetAuthenticationAsync(cancellationToken).ConfigureAwait(false);
					break;

				case Operation.Test:
					await _bridge.TestConnectionAsync(cancellationToken).ConfigureAwait(false);
					break;

				case Operation.Uninstall:
					await _bridge.UninstallAsync(cancellationToken).ConfigureAwait(false);
					break;
			}
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{

			_logger.Warning(exception, "The settings page action {Operation} failed.", operation);
		}

		Refresh();
	}

		private void Refresh()
	{
		if (Volatile.Read(ref _disposed) != 0)
		{
			return;
		}

		_revision.Value = _revision.Value + 1;
		Changed?.Invoke(this, EventArgs.Empty);
	}

		private UiText Text(Func<LocalizedText?> compute) =>
		UiText.From(() =>
		{
			_ = _revision.Value;
			return compute()?.ToString();
		});

	private BridgeDiagnostics Describe() => _bridge.Describe();

	private static LocalizedString YesNo(bool value) => value ? Strings.Ui.Settings.Yes() : Strings.Ui.Settings.No();

		private static LocalizedText? Or(string? value, LocalizedString fallback) =>
		string.IsNullOrWhiteSpace(value) ? fallback : LocalizedText.FromLiteral(value);

	private static JsonElement Json(string value) => JsonSerializer.SerializeToElement(value);

		private enum Operation
	{
		Install,
		Repair,
		Reset,
		Test,
		Uninstall,
	}
}
