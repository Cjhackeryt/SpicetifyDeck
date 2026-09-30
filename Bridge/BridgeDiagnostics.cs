namespace SpicetifyDeck.Bridge;

public enum BridgeStatus
{
		SpicetifyNotInstalled,

		BridgeNotInstalled,

		Corrupted,

		WaitingForSpotify,

		NotConnected,

		AuthenticationFailed,

		ConnectionLost,

		UpdatePending,

		Connected,
}

public sealed record BridgeDiagnostics(
	BridgeStatus Status,
	string? SpicetifyVersion,
	bool IsExtensionEnabled,
	string? BridgeVersion,
	string? LastError,
	bool RequiresApply,
	bool IsApplied = true)
{
		public bool IsInstalled => Status is not (BridgeStatus.SpicetifyNotInstalled or BridgeStatus.BridgeNotInstalled);

		public bool IsUpToDate => IsInstalled && IsApplied && !RequiresApply;

		public string StatusName => Status.ToString();
}
