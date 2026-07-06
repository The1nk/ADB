namespace BotBuilder.Core;

/// <summary>Which edge of a node card a port sits on (determines its anchor and the connector's
/// outgoing direction). Inputs are Left; failure outputs (onFailure/someFailed) are Bottom; all other
/// outputs are Right. <see cref="Top"/> and <see cref="Bottom"/> are also used by the derived
/// single-connection orientation pass to turn a serpentine band-turn into a vertical bottom-out→top-in
/// drop; that assignment is display-only and never persisted.</summary>
public enum PortEdge { Left, Right, Bottom, Top }
