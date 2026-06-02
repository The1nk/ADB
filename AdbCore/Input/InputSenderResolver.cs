namespace AdbCore.Input;

/// <summary>Selects the <see cref="IInputSender"/> for an action's configured input method. Defaults to
/// SendInput (foreground, reliable across modern apps); PostMessage is opt-in for background-capable apps.</summary>
public sealed class InputSenderResolver
{
    public const string SendInputMethod = "SendInput";
    public const string PostMessageMethod = "PostMessage";

    private readonly IInputSender _sendInput;
    private readonly IInputSender _postMessage;

    public InputSenderResolver(IInputSender sendInput, IInputSender postMessage)
    {
        ArgumentNullException.ThrowIfNull(sendInput);
        ArgumentNullException.ThrowIfNull(postMessage);
        _sendInput = sendInput;
        _postMessage = postMessage;
    }

    /// <summary>Returns the PostMessage sender when <paramref name="method"/> is "PostMessage"
    /// (case-insensitive); otherwise the default SendInput sender.</summary>
    public IInputSender Resolve(string? method)
        => string.Equals(method, PostMessageMethod, StringComparison.OrdinalIgnoreCase) ? _postMessage : _sendInput;
}
