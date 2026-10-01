using MacroDeck.Localization;
using MacroDeck.Sdk.Variables;
using SpicetifyDeck.Bridge;

namespace SpicetifyDeck.Variables;

public sealed record DeckStatusVariable(
	string Name,
	VariableType Type,
	Func<SpicetifyBridgeService, object?> Read,
	LocalizedText Display,
	LocalizedText Description,
	TimeSpan Refresh)
{
	public string LocalId => Name.Replace('_', '-');

	public VariableDefinition ToDefinition() =>
		new()
		{
			Id = LocalId,
			Name = Name,
			Type = Type,
			Materialization = VariableMaterialization.Eager,
			DisplayName = Display,
			Description = Description,
			RefreshInterval = Refresh,
		};
}

public static class DeckStatusVariableCatalog
{
	private static readonly TimeSpan Steady = TimeSpan.FromSeconds(2);

		public static IReadOnlyList<DeckStatusVariable> All { get; } =
	[
		new(
			"spicetifydeck_connection_status",
			VariableType.Text,
			service => StatusWord(service),
			Strings.Variables.DeckConnectionStatus.Display(),
			Strings.Variables.DeckConnectionStatus.Description(),
			Steady),
	];

	private static readonly Dictionary<string, DeckStatusVariable> ByLocalId =
		All.ToDictionary(variable => variable.LocalId, StringComparer.OrdinalIgnoreCase);

	public static DeckStatusVariable? Find(string localId) => ByLocalId.GetValueOrDefault(localId);

	public static IReadOnlyList<VariableDefinition> Definitions { get; } =
		[.. All.Select(variable => variable.ToDefinition())];

		private static string StatusWord(SpicetifyBridgeService service) => service.Describe().Status switch
	{
		BridgeStatus.SpicetifyNotInstalled => "Spicetify Not Installed",
		BridgeStatus.BridgeNotInstalled => "Bridge Not Installed",
		BridgeStatus.Corrupted => "Bridge Corrupted",
		BridgeStatus.WaitingForSpotify => "Bridge Installed - Waiting for Spotify",
		BridgeStatus.NotConnected => "Bridge Disconnected",
		BridgeStatus.AuthenticationFailed => "Authentication Failed",
		BridgeStatus.ConnectionLost => "Bridge Disconnected",
		BridgeStatus.UpdatePending => "Bridge Update Pending",
		_ => "Bridge Connected",
	};
}
