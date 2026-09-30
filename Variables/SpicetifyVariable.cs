using MacroDeck.Localization;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.Variables;
using SpicetifyDeck.State;

namespace SpicetifyDeck.Variables;

public enum VariableSource
{
		Transport,

		Client,

		Stable,
}

public sealed record SpicetifyVariableLabels(LocalizedText Display, LocalizedText Description, string? Icon = null);

public sealed record SpicetifyVariable(
	string Name,
	VariableType Type,
	Func<SpotifySnapshot, object?> Read,
	SpicetifyVariableLabels Labels,
	string? Unit = null,
	string? SemanticKind = null,
	int? DecimalPlaces = null,
	bool Writable = false,
	bool CommitOnRelease = false,
	VariableSource Source = VariableSource.Stable,
	TimeSpan? RefreshOverride = null)
{
	public string LocalId => Name.Replace('_', '-');

		public TimeSpan RefreshInterval => RefreshOverride ?? Source switch
	{
		VariableSource.Transport => TimeSpan.FromSeconds(1),
		VariableSource.Client => TimeSpan.FromSeconds(2),
		_ => TimeSpan.FromSeconds(5),
	};

	public VariableDefinition ToDefinition() =>
		new()
		{
			Id = LocalId,
			Name = Name,
			Type = Type,
			Materialization = VariableMaterialization.Eager,
			DisplayName = Labels.Display,
			Description = Labels.Description,
			Icon = Labels.Icon,
			Unit = Unit,
			DecimalPlaces = DecimalPlaces,
			SemanticKind = SemanticKind,
			RefreshInterval = RefreshInterval,
			Write = Writable ? new VariableWriteCapability { CommitOnRelease = CommitOnRelease } : null,
		};
}

public static class SpicetifyValues
{
		public const string PluginName = "SpicetifyDeck";

	public const string WaitingForSpicetify = "Waiting for Spicetify...";

		public const string Unavailable = "Unavailable";

		public static double? Percent(double? fraction) =>
		fraction is null ? null : Math.Round(Math.Clamp(fraction.Value, 0, 1) * 100d, 1);
}
