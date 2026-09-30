using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using Serilog;
using SpicetifyDeck.Bridge;
using SpicetifyDeck.State;

namespace SpicetifyDeck.Actions;

public abstract class SpicetifyActionBase(
	SpotifyStateManager state,
	BridgeConnectionManager bridge,
	ILogger logger) : IActionDefinition
{
	protected SpotifyStateManager State { get; } = state;

	protected BridgeConnectionManager Bridge { get; } = bridge;

	protected ILogger Log { get; } = logger.ForContext<SpicetifyActionBase>();

	public abstract string Id { get; }

	public abstract LocalizedText Name { get; }

	public abstract LocalizedText Description { get; }

	public virtual IReadOnlyList<ActionParameter> Parameters => Array.Empty<ActionParameter>();

	public virtual IActionExecutor CreateExecutor() => new Executor(this);

		protected abstract Task<LocalizedText?> ExecuteAsync(ActionExecutionContext context);

		protected async Task<LocalizedText?> SendAsync(
		string kind,
		IReadOnlyDictionary<string, object?>? payload,
		CancellationToken cancellationToken)
	{
		if (!State.IsConnected)
		{
			return Strings.Actions.Errors.NoBridge();
		}

		var result = await Bridge.SendAsync(kind, payload, null, cancellationToken).ConfigureAwait(false);
		if (result.Ok)
		{
			return null;
		}

		return LocalizedText.FromLiteral(result.Error);
	}

		protected static IReadOnlyDictionary<string, object?> Payload(string key, object value) =>
		new Dictionary<string, object?>(StringComparer.Ordinal) { [key] = value };

		protected static LocalizedText? Guard(bool supported, LocalizedText what)
	{
		if (supported)
		{
			return null;
		}

		return what;
	}

	private static ActionResult Succeeded => ActionResult.Success();

	private sealed class Executor(SpicetifyActionBase action) : IActionExecutor	{
		public async Task<ActionResult> ExecuteAsync(ActionExecutionContext context)
		{
			try
			{
				var failure = await action.ExecuteAsync(context).ConfigureAwait(false);
				return failure is null ? ActionResult.Success() : ActionResult.Failed(ActionErrorCodes.ProviderError, failure.Value);
			}
			catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
			{
				return ActionResult.Failed(ActionErrorCodes.Unavailable, Strings.Actions.Errors.Cancelled());
			}
			catch (Exception exception)
			{

				action.Log.Error(exception, "The action {Action} failed unexpectedly.", action.Id);
				return ActionResult.Failed(ActionErrorCodes.ProviderError, Strings.Actions.Errors.Unexpected());
			}
		}
	}
}
