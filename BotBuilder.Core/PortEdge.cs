namespace BotBuilder.Core;

/// <summary>Which edge of a node card a port sits on (determines its anchor and the connector's
/// outgoing direction). Inputs are Left; failure outputs (onFailure/someFailed) are Bottom; all other
/// outputs are Right.</summary>
public enum PortEdge { Left, Right, Bottom }
