namespace BotBuilder.Core.ExternalEdit;

/// <summary>Arguments for the <see cref="ExternalEditSession.Stopped"/> event.</summary>
public sealed class ExternalEditStoppedArgs : EventArgs
{
    public ExternalEditStoppedArgs(string finalText, bool userRequested)
    {
        FinalText = finalText;
        UserRequested = userRequested;
    }

    /// <summary>The last text read from the temp file at session end.</summary>
    public string FinalText { get; }

    /// <summary>True if the session ended because the user clicked Done; false if the editor process
    /// exited on its own.</summary>
    public bool UserRequested { get; }
}
