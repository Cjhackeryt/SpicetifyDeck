using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Issues;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Ui;
using Serilog;
using SpicetifyDeck.Actions;
using SpicetifyDeck.Artwork;
using SpicetifyDeck.Bridge;
using SpicetifyDeck.State;
using SpicetifyDeck.Ui;
using SpicetifyDeck.Variables;

namespace SpicetifyDeck;

#pragma warning disable CA1001
public sealed class SpicetifyIntegration : IPluginIntegration, IVariableProvider, IMusicPlayerProvider, IIntegrationIssueProvider, IUiProvider
#pragma warning restore CA1001
{
	private const string IssueNoSpicetify = "spicetify-cli-missing";
	private const string IssueNoBridge = "spicetify-bridge-missing";
	private const string IssueBridgeCorrupted = "spicetify-bridge-corrupted";
	private const string IssueNotConnected = "spicetify-bridge-disconnected";
	private const string IssueAuthFailed = "spicetify-bridge-auth-failed";
	private const string IssueUpdatePending = "spicetify-bridge-update-pending";

	private readonly SpotifyStateManager _state;
	private readonly BridgeConnectionManager _bridge;
	private readonly SpicetifyBridgeService _bridgeService;
	private readonly SpicetifyVariableProvider _variables;
	private readonly ILogger _logger;
	private readonly SpicetifyMusicPlayer _player;
	private readonly SpicetifyDeckUiProvider _ui;

	public SpicetifyIntegration(
		SpotifyStateManager state,
		BridgeConnectionManager bridge,
		SpicetifyBridgeService bridgeService,
		ArtworkFetcher artwork,
		SpicetifyVariableProvider variables,
		SpicetifyDeckUiProvider ui,
		ILogger logger)
	{
		_state = state;
		_bridge = bridge;
		_bridgeService = bridgeService;
		_variables = variables;
		_ui = ui;
		_logger = logger.ForContext<SpicetifyIntegration>();
		_player = new SpicetifyMusicPlayer(state, artwork, bridge, _logger);

		Actions = BuildActions();
	}

	public IReadOnlyList<IActionDefinition> Actions { get; }

	// ── Variables ─────────────────────────────────────────────────────────────

	public IReadOnlyList<VariableDefinition> Variables => _variables.Variables;

	public string CatalogName => SpicetifyValues.PluginName;

	public ValueTask<VariableReading> ReadAsync(string localId, CancellationToken cancellationToken = default) =>
		_variables.ReadAsync(localId, cancellationToken);

	public ValueTask<VariableWriteResult> SetValueAsync(string localId, object? value, CancellationToken cancellationToken = default) =>
		_variables.SetValueAsync(localId, value, cancellationToken);

	// ── Music player ──────────────────────────────────────────────────────────

	public string ProviderName => SpicetifyValues.PluginName;

	public IReadOnlyList<MusicPlayerInstance> GetInstances() =>
		[new MusicPlayerInstance(SpicetifyVariableCatalog.InstanceId, Strings.Player.Name().ToString())];

	public IMusicPlayer? GetPlayer(string instanceId) =>
		string.Equals(instanceId, SpicetifyVariableCatalog.InstanceId, StringComparison.Ordinal) ? _player : null;

	// ── Settings page ────────────────────────────────────────────────────────

		public IReadOnlyList<UiSurfaceDeclaration> Surfaces => _ui.Surfaces;

	public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken) =>
		_ui.CreateSessionAsync(request, cancellationToken);

	// ── Lifecycle ─────────────────────────────────────────────────────────────

		public async Task InitializeAsync(IIntegrationContext context)
	{
		_logger.Information("SpicetifyDeck is initializing.");

		await ReconcileBridgeAsync(CancellationToken.None).ConfigureAwait(false);
	}

		private async Task ReconcileBridgeAsync(CancellationToken cancellationToken)
	{
		try
		{
			var result = await _bridgeService.ReconcileAsync(cancellationToken).ConfigureAwait(false);
			if (result is null)
			{
				return;
			}

			_logger.Information("Bridge reconciliation: {Message}", result.Message);
			if (!result.Succeeded)
			{
				_logger.Warning("Bridge reconciliation failed: {Message}", result.Message);
			}
		}
		catch (Exception exception) when (exception is not OperationCanceledException)
		{
			_logger.Warning(exception, "The installed bridge could not be checked.");
		}
	}

		public Task ShutdownAsync()
	{
		_logger.Information("SpicetifyDeck is shutting down.");
			return Task.CompletedTask;
	}

	// ── Issues ────────────────────────────────────────────────────────────────

		public Task<IReadOnlyList<IntegrationIssue>> GetIssuesAsync(CancellationToken cancellationToken) =>
		Task.FromResult<IReadOnlyList<IntegrationIssue>>(BuildIssues());

		public Task<IssueResolution> ResolveIssueAsync(string issueId, CancellationToken cancellationToken) =>
		Task.FromResult(issueId switch
		{
			IssueNoSpicetify => IssueResolution.Failed(Strings.Issues.NoSpicetify.Resolve()),
			IssueNoBridge => IssueResolution.Ok(Strings.Issues.NoBridge.Resolve()),
			IssueBridgeCorrupted => IssueResolution.Ok(Strings.Issues.Corrupted.Resolve()),
			IssueNotConnected => IssueResolution.Ok(Strings.Issues.NotConnected.Resolve()),
			IssueAuthFailed => IssueResolution.Ok(Strings.Issues.AuthFailed.Resolve()),
			IssueUpdatePending => IssueResolution.Ok(Strings.Issues.UpdatePending.Resolve()),
			_ => IssueResolution.Failed(Strings.Issues.Unknown.Resolve()),
		});

		private List<IntegrationIssue> BuildIssues()
	{
		var issues = new List<IntegrationIssue>();
		var diagnostics = _bridgeService.Describe();

		IntegrationIssue? issue = diagnostics.Status switch
		{
			BridgeStatus.SpicetifyNotInstalled => new()
			{
				Id = IssueNoSpicetify,
				Title = Strings.Issues.NoSpicetify.Title(),
				Description = Strings.Issues.NoSpicetify.Description(),
				Severity = IntegrationIssueSeverity.Warning,
				ActionLabel = Strings.Issues.NoSpicetify.Action(),
			},

			BridgeStatus.BridgeNotInstalled => new()
			{
				Id = IssueNoBridge,
				Title = Strings.Issues.NoBridge.Title(),
				Description = Strings.Issues.NoBridge.Description(),
				Severity = IntegrationIssueSeverity.Warning,
				ActionLabel = Strings.Issues.NoBridge.Action(),
			},

			BridgeStatus.Corrupted => new()
			{
				Id = IssueBridgeCorrupted,
				Title = Strings.Issues.Corrupted.Title(),
				Description = Strings.Issues.Corrupted.Description(),
				Severity = IntegrationIssueSeverity.Error,
				ActionLabel = Strings.Issues.Corrupted.Action(),
			},

			BridgeStatus.AuthenticationFailed => new()
			{
				Id = IssueAuthFailed,
				Title = Strings.Issues.AuthFailed.Title(),
				Description = Strings.Issues.AuthFailed.Description(),
				Severity = IntegrationIssueSeverity.Error,
				ActionLabel = Strings.Issues.AuthFailed.Action(),
			},

			BridgeStatus.UpdatePending => new()
			{
				Id = IssueUpdatePending,
				Title = Strings.Issues.UpdatePending.Title(),
				Description = Strings.Issues.UpdatePending.Description(),
				Severity = IntegrationIssueSeverity.Warning,
				ActionLabel = Strings.Issues.UpdatePending.Action(),
			},

			BridgeStatus.NotConnected or BridgeStatus.ConnectionLost or BridgeStatus.WaitingForSpotify => new()
			{
				Id = IssueNotConnected,
				Title = Strings.Issues.NotConnected.Title(),
				Description = Strings.Issues.NotConnected.Description(),
				Severity = IntegrationIssueSeverity.Warning,
				ActionLabel = Strings.Issues.NotConnected.Action(),
			},

			_ => null,
		};

		if (issue is not null)
		{
			issues.Add(issue);
		}

		return issues;
	}

	private IReadOnlyList<IActionDefinition> BuildActions()
	{
		return
		[

			new PlayAction(_state, _bridge, _logger),
			new PauseAction(_state, _bridge, _logger),
			new TogglePlayAction(_state, _bridge, _logger),
			new NextAction(_state, _bridge, _logger),
			new PreviousAction(_state, _bridge, _logger),
			new RestartTrackAction(_state, _bridge, _logger),
			new SkipAction(_state, _bridge, _logger),
			new SeekToAction(_state, _bridge, _logger),
			new SetVolumeAction(_state, _bridge, _logger),
			new VolumeStepAction(_state, _bridge, _logger),
			new SetMuteAction(_state, _bridge, _logger),

			new ToggleLikeAction(_state, _bridge, _logger),
			new PlayUriAction(_state, _bridge, _logger),
			new PlayLikedSongsAction(_state, _bridge, _logger),
			new AddToQueueAction(_state, _bridge, _logger),
			new RemoveFromQueueAction(_state, _bridge, _logger),
			new ClearQueueAction(_state, _bridge, _logger),

			new OpenPageAction(_state, _bridge, _logger),
			new OpenSpotifyAction(_state, _bridge, _logger),

			new InstallBridgeAction(_state, _bridge, _bridgeService, _logger),
			new ReinstallBridgeAction(_state, _bridge, _bridgeService, _logger),
			new RepairBridgeAction(_state, _bridge, _bridgeService, _logger),
			new RestartBridgeAction(_state, _bridge, _bridgeService, _logger),
			new ResetBridgeAuthenticationAction(_state, _bridge, _bridgeService, _logger),
			new UninstallBridgeAction(_state, _bridge, _bridgeService, _logger),
		];
	}
}
