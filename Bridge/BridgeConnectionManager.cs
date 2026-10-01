using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Serilog;

namespace SpicetifyDeck.Bridge;

public enum BridgeConnectionState
{
		Disconnected,

		Connecting,

		Authenticating,

		Connected,

		AuthenticationFailed,
}

public sealed class BridgeConnectionManager(BridgeCredentials credentials, ILogger logger) : IAsyncDisposable
{
		private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(10);

		private static readonly TimeSpan StateStaleAfter = TimeSpan.FromSeconds(90);

		private static readonly TimeSpan HelloTimeout = TimeSpan.FromSeconds(10);

	private readonly ILogger _logger = logger.ForContext<BridgeConnectionManager>();
	private readonly ConcurrentDictionary<string, PendingCommand> _pending = new(StringComparer.Ordinal);
	private readonly SemaphoreSlim _writeGate = new(1, 1);
	private readonly Lock _sessionGate = new();
	private readonly CancellationTokenSource _stopping = new();
	private Session? _session;
	private long _sequence;
	private int _disposed;
	private volatile bool _hasEverConnected;
	private BridgeConnectionState _connectionState = BridgeConnectionState.Disconnected;
	private bool _authenticating;
	private DateTimeOffset? _listenerStartedAt;

		public string Token => credentials.Token;

		public bool HasAuthenticationFailure { get; private set; }

		public string? AuthenticationFailureReason { get; private set; }

		public void ClearAuthenticationFailure()
	{
		HasAuthenticationFailure = false;
		AuthenticationFailureReason = null;
	}

	public bool IsConnected
	{
		get
		{
			lock (_sessionGate)
			{
				return _session is { Socket.State: WebSocketState.Open };
			}
		}
	}

		public bool IsReporting => _reportingOverride || IsReportingSession();

	private bool IsReportingSession()
	{
		lock (_sessionGate)
		{
			return _session is { } session &&
				DateTimeOffset.UtcNow - session.LastStateAt < StateStaleAfter;
		}
	}

		internal void MarkReportingForTest() => _reportingOverride = true;

	private volatile bool _reportingOverride;

	public BridgeConnectionState ConnectionState
	{
		get
		{
			if (HasAuthenticationFailure)
			{
				return BridgeConnectionState.AuthenticationFailed;
			}

			lock (_sessionGate)
			{

				if (_authenticating)
				{
					return BridgeConnectionState.Authenticating;
				}

				if (_connectionState == BridgeConnectionState.Disconnected)
				{
					return BridgeConnectionState.Disconnected;
				}

				if (_session is not { } session)
				{
					if (_connectionState == BridgeConnectionState.Connecting
						&& _listenerStartedAt is { } startedAt
						&& DateTimeOffset.UtcNow - startedAt >= StateStaleAfter)
					{
						return BridgeConnectionState.Disconnected;
					}

					return _connectionState;
				}

				if (session.LastStateAt == default)
				{
					return DateTimeOffset.UtcNow - session.ConnectedAt < StateStaleAfter
						? BridgeConnectionState.Connecting
						: BridgeConnectionState.Disconnected;
				}

				return DateTimeOffset.UtcNow - session.LastStateAt < StateStaleAfter
					? BridgeConnectionState.Connected
					: BridgeConnectionState.Disconnected;
			}
		}
	}

	public void ListenerStarted()
	{
		lock (_sessionGate)
		{
			_connectionState = BridgeConnectionState.Connecting;
			_listenerStartedAt = DateTimeOffset.UtcNow;
		}
	}

	public void ListenerStopped()
	{
		lock (_sessionGate)
		{
			_connectionState = BridgeConnectionState.Disconnected;
			_listenerStartedAt = null;
		}
	}

	public DateTimeOffset? LastReportAt
	{
		get
		{
			lock (_sessionGate)
			{
				return _session is { } session && session.LastStateAt != default
					? session.LastStateAt
					: null;
			}
		}
	}

		public event Func<BridgeState, CancellationToken, Task>? StateReceived;

		public event Action<bool>? ConnectionChanged;

		private string? CurrentSessionId
	{
		get
		{
			lock (_sessionGate)
			{
				return _session?.Id;
			}
		}
	}

		public bool HasEverConnected => _hasEverConnected;

		public TimeSpan SinceLastReport
	{
		get
		{
			lock (_sessionGate)
			{
				return _session is { } session && session.LastStateAt != default
					? DateTimeOffset.UtcNow - session.LastStateAt
					: TimeSpan.MaxValue;
			}
		}
	}

		public async Task<BridgeCommandResult> SendAsync(
		string kind,
		IReadOnlyDictionary<string, object?>? payload,
		TimeSpan? timeout,
		CancellationToken cancellationToken)
	{
		if (Volatile.Read(ref _disposed) != 0 || !IsConnected)
		{
			return BridgeCommandResult.Offline();
		}

		var wait = timeout ?? CommandTimeout;
		var id = Interlocked.Increment(ref _sequence).ToString(System.Globalization.CultureInfo.InvariantCulture);
		var source = new TaskCompletionSource<BridgeCommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		var pending = new PendingCommand(kind, payload, DateTimeOffset.UtcNow + wait, source)
		{

			SessionId = CurrentSessionId,
		};

		if (!_pending.TryAdd(id, pending))
		{
			return BridgeCommandResult.Offline();
		}

		await FlushPendingAsync(cancellationToken).ConfigureAwait(false);

		await using var registration = cancellationToken.Register(() => Expire(id, BridgeCommandResult.Cancelled()))
			.ConfigureAwait(false);

		try
		{
			return await source.Task.WaitAsync(wait, cancellationToken).ConfigureAwait(false);
		}
		catch (TimeoutException)
		{
			Expire(id, BridgeCommandResult.TimedOut());
			return await source.Task.ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			Expire(id, BridgeCommandResult.Cancelled());
			throw;
		}
	}

		public async Task ServeAsync(WebSocket socket, CancellationToken cancellationToken)
	{

		lock (_sessionGate)
		{
			_authenticating = true;
		}

		try
		{
			if (!await AuthenticateAsync(socket, cancellationToken).ConfigureAwait(false))
			{
				return;
			}
		}
		catch (OperationCanceledException)
		{
			return;
		}
		catch (Exception exception)
		{

			_logger.Warning(exception, "The bridge handshake failed, so this client was not served.");
			return;
		}
		finally
		{
			lock (_sessionGate)
			{
				_authenticating = false;
			}
		}

		var session = new Session(socket, Guid.NewGuid().ToString("n"));

		Session? displaced;
		lock (_sessionGate)
		{
			displaced = _session;
			_session = session;
			_connectionState = BridgeConnectionState.Connecting;
		}

		if (displaced is not null)
		{
			_logger.Information("A newer Spotify client connected, replacing the previous bridge session.");
			await CloseQuietlyAsync(displaced).ConfigureAwait(false);
		}

		ClearAuthenticationFailure();
		ConnectionChanged?.Invoke(true);
		_hasEverConnected = true;
		_logger.Information("Spicetify bridge connected, session {SessionId}.", session.Id);

		try
		{
			await SendAsync(session, new BridgeWelcome
			{
				SessionId = session.Id,
				UnavailableTimeoutSeconds = 30,
				StateIntervalMs = 1000,
			}, cancellationToken).ConfigureAwait(false);

			while (socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
			{
				var message = await ReceiveAsync(socket, cancellationToken).ConfigureAwait(false);
				if (message is null)
				{
					break;
				}

				await HandleMessageAsync(session, message, cancellationToken).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException)
		{

		}
		catch (Exception exception)
		{
			_logger.Debug(exception, "The Spicetify bridge session ended.");
		}
		finally
		{
			lock (_sessionGate)
			{
				if (ReferenceEquals(_session, session))
				{
					_session = null;
					_connectionState = BridgeConnectionState.Disconnected;
				}
			}

			AbandonAll(BridgeCommandResult.Offline());
			ConnectionChanged?.Invoke(false);
			_logger.Information("Spicetify bridge disconnected, session {SessionId}.", session.Id);
		}
	}

		public async Task DropSessionAsync()
	{
		Session? session;
		lock (_sessionGate)
		{
			session = _session;
			_session = null;
			_connectionState = BridgeConnectionState.Disconnected;
		}

		if (session is null)
		{
			return;
		}

		_logger.Information("Dropping the Spicetify bridge session {SessionId} on request.", session.Id);
		AbandonAll(BridgeCommandResult.Offline());
		ConnectionChanged?.Invoke(false);
		await CloseQuietlyAsync(session).ConfigureAwait(false);
	}

		public void AbandonAll(BridgeCommandResult result)
	{
		foreach (var id in _pending.Keys)
		{
			Expire(id, result);
		}
	}

	public async ValueTask DisposeAsync()
	{
		if (Interlocked.Exchange(ref _disposed, 1) != 0)
		{
			return;
		}

		await _stopping.CancelAsync().ConfigureAwait(false);

		Session? session;
		lock (_sessionGate)
		{
			session = _session;
			_session = null;
		}

		if (session is not null)
		{
			await CloseQuietlyAsync(session).ConfigureAwait(false);
		}

		AbandonAll(BridgeCommandResult.Offline());
		_writeGate.Dispose();
		_stopping.Dispose();
	}

		private async Task<bool> AuthenticateAsync(WebSocket socket, CancellationToken cancellationToken)
	{
		using var helloTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		helloTimeout.CancelAfter(HelloTimeout);

		string? message;
		try
		{
			message = await ReceiveAsync(socket, helloTimeout.Token).ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			await RefuseAsync(socket, "The client did not identify itself in time.").ConfigureAwait(false);
			return false;
		}

		if (message is null)
		{
			return false;
		}

		string? presented = null;
		string? kind = null;
		try
		{
			using var document = JsonDocument.Parse(message);
			var root = document.RootElement;

			if (root.ValueKind == JsonValueKind.Object)
			{
				kind = root.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String
					? type.GetString()
					: null;

				presented = root.TryGetProperty("token", out var token) && token.ValueKind == JsonValueKind.String
					? token.GetString()
					: null;
			}
		}
		catch (JsonException)
		{
			await RefuseAsync(socket, "The first message was not readable.").ConfigureAwait(false);
			return false;
		}

		if (!string.Equals(kind, BridgeMessageKind.Hello, StringComparison.Ordinal))
		{
			await RefuseAsync(socket, "The client did not send a hello first.").ConfigureAwait(false);
			return false;
		}

		if (!Matches(presented, Token))
		{
			_logger.Warning("A bridge client presented a token this plugin did not issue. Reinstall the bridge.");
			await RefuseAsync(socket, "The bridge token does not match. Run Repair Spicetify Bridge.").ConfigureAwait(false);
			return false;
		}

		_logger.Debug("Bridge client authenticated.");
		return true;

		async Task RefuseAsync(WebSocket target, string reason)
		{
			HasAuthenticationFailure = true;
			AuthenticationFailureReason = reason;
			lock (_sessionGate)
			{
				_connectionState = BridgeConnectionState.AuthenticationFailed;
			}

			try
			{
				var bytes = JsonSerializer.SerializeToUtf8Bytes(
					new BridgeAuthFailed { Reason = reason },
					BridgeJson.Options);

				await target.SendAsync(bytes, WebSocketMessageType.Text, true, CancellationToken.None)
					.ConfigureAwait(false);
				await target.CloseOutputAsync(
					WebSocketCloseStatus.PolicyViolation,
					"Bridge authentication failed.",
					CancellationToken.None).ConfigureAwait(false);
			}
			catch (Exception exception) when (exception is WebSocketException or ObjectDisposedException or OperationCanceledException)
			{

			}
		}
	}

		private static bool Matches(string? presented, string expected)
	{
		if (presented is null || presented.Length != expected.Length)
		{
			return false;
		}

		var difference = 0;
		for (var index = 0; index < expected.Length; index++)
		{
			difference |= presented[index] ^ expected[index];
		}

		return difference == 0;
	}

		private async Task HandleMessageAsync(Session session, string message, CancellationToken cancellationToken)
	{
		BridgeStateMessage? payload;
		try
		{
			payload = JsonSerializer.Deserialize<BridgeStateMessage>(message, BridgeJson.Options);
		}
		catch (JsonException)
		{
			_logger.Debug("Discarded a bridge message that was not readable JSON.");
			return;
		}

		if (payload?.State is not { } state)
		{
			return;
		}

		session.LastStateAt = DateTimeOffset.UtcNow;
		lock (_sessionGate)
		{
			if (ReferenceEquals(_session, session))
			{
				_connectionState = BridgeConnectionState.Connected;
			}
		}

		if (payload.Results is { Count: > 0 } results)
		{
			foreach (var (id, outcome) in results)
			{
				if (_pending.TryRemove(id, out var pending))
				{
					pending.Source.TrySetResult(outcome.Ok
						? BridgeCommandResult.Success()
						: BridgeCommandResult.Failure(outcome.Error ?? "The client reported a failure."));
				}
			}
		}

		if (StateReceived is { } handler)
		{
			try
			{
				await handler(state, cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception exception)
			{
				_logger.Debug(exception, "A state listener failed while handling a bridge report.");
			}
		}

		await FlushAsync(session, cancellationToken).ConfigureAwait(false);
	}

		private Task FlushPendingAsync(CancellationToken cancellationToken)
	{
		Session? session;
		lock (_sessionGate)
		{
			session = _session;
		}

		return session is null ? Task.CompletedTask : FlushAsync(session, cancellationToken);
	}

	private async Task FlushAsync(Session session, CancellationToken cancellationToken)
	{
		var now = DateTimeOffset.UtcNow;
		var batch = new List<BridgeCommand>();

		foreach (var (id, pending) in _pending)
		{
			if (pending.Deadline <= now)
			{
				Expire(id, BridgeCommandResult.TimedOut());
				continue;
			}

			if (pending.SessionId is not null && pending.SessionId != session.Id)
			{
				continue;
			}

			batch.Add(new BridgeCommand { Id = id, Kind = pending.Kind, Payload = pending.Payload });
		}

		if (batch.Count == 0)
		{
			return;
		}

		await SendAsync(session, new BridgeCommandsMessage { Commands = batch }, cancellationToken).ConfigureAwait(false);
	}

	private async Task SendAsync(Session session, object payload, CancellationToken cancellationToken)
	{
		var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, BridgeJson.Options);

		await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			await session.Socket.SendAsync(bytes, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			_writeGate.Release();
		}
	}

		private static async Task<string?> ReceiveAsync(WebSocket socket, CancellationToken cancellationToken)
	{
		var buffer = new byte[16 * 1024];
		using var message = new MemoryStream();

		while (true)
		{
			WebSocketReceiveResult result;
			try
			{
				result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
			}
			catch (WebSocketException)
			{
				return null;
			}

			if (result.MessageType == WebSocketMessageType.Close)
			{
				return null;
			}

			await message.WriteAsync(buffer.AsMemory(0, result.Count), cancellationToken).ConfigureAwait(false);

			if (result.EndOfMessage)
			{
				return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
			}
		}
	}

	private void Expire(string id, BridgeCommandResult result)
	{
		if (_pending.TryRemove(id, out var pending))
		{
			pending.Source.TrySetResult(result);
		}
	}

	private static async Task CloseQuietlyAsync(Session session)
	{
		try
		{
			if (session.Socket.State == WebSocketState.Open)
			{
				using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
				await session.Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "replaced", timeout.Token)
					.ConfigureAwait(false);
			}
		}
		catch (Exception exception) when (exception is WebSocketException or OperationCanceledException or ObjectDisposedException)
		{

		}
	}

		private sealed class Session(WebSocket socket, string id)
	{
		public WebSocket Socket { get; } = socket;

		public string Id { get; } = id;

		public DateTimeOffset ConnectedAt { get; } = DateTimeOffset.UtcNow;

		public DateTimeOffset LastStateAt { get; set; }
	}

	private sealed record PendingCommand(
		string Kind,
		IReadOnlyDictionary<string, object?>? Payload,
		DateTimeOffset Deadline,
		TaskCompletionSource<BridgeCommandResult> Source)
	{
				public string? SessionId { get; init; }
	}
}
