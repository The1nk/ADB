using System.Reflection;

namespace BotBuilder.Core.ExternalEdit;

/// <summary>Provides the LuaLS workspace scaffold files that give Lua editors (VS Code + Lua Language
/// Server) autocomplete for ADB's injected globals: <c>log</c>, <c>vars</c>, <c>json</c>, <c>fs</c>,
/// <c>process</c>, and <c>http</c>.
/// <para>
/// The scaffold consists of two files:
/// <list type="bullet">
///   <item><term><see cref="LuaRcJsonRelativePath"/></term><description>A <c>.luarc.json</c> dropped
///   into the edit base dir that points LuaLS at the <c>library/</c> subfolder.</description></item>
///   <item><term><see cref="DefinitionsRelativePath"/></term><description>The <c>adb.lua</c>
///   <c>---@meta</c> definition file placed inside that subfolder.</description></item>
/// </list>
/// Both files are written by the BotBuilder UI before launching the external editor. IntelliSense is
/// best-effort — a failure to write the scaffold must not block opening the editor.
/// </para>
/// </summary>
public static class LuaWorkspaceScaffold
{
    /// <summary>Path of the LuaLS config file, relative to the edit base directory
    /// (<c>%TEMP%\ADB\LuaEdit</c>).</summary>
    public const string LuaRcJsonRelativePath = ".luarc.json";

    /// <summary>Path of the ADB definition file, relative to the edit base directory.
    /// The <c>library/</c> prefix matches <c>workspace.library</c> in <see cref="LuaRcJson"/>.</summary>
    public const string DefinitionsRelativePath = "library/adb.lua";

    // Manifest resource name set via LogicalName in BotBuilder.Core.csproj.
    private const string ResourceName = "BotBuilder.Core.Assets.lua.adb.lua";

    private static string? _definitions;

    /// <summary>The content of the <c>adb.lua</c> LuaLS definition file, read from the embedded
    /// resource at first access and cached. Contains <c>---@meta</c> annotations for all ADB globals.
    /// </summary>
    /// <exception cref="InvalidOperationException">The embedded resource could not be found — this
    /// indicates a build misconfiguration.</exception>
    public static string Definitions => _definitions ??= LoadDefinitions();

    /// <summary>The content of the <c>.luarc.json</c> workspace configuration file for the Lua
    /// Language Server. Points <c>workspace.library</c> at <c>library/</c> (relative to the file's
    /// own location) and declares all six ADB globals.</summary>
    public static string LuaRcJson =>
        """
        {
          "$schema": "https://raw.githubusercontent.com/LuaLS/vscode-lua/master/setting/schema.json",
          "runtime.version": "Lua 5.2",
          "workspace.library": ["library"],
          "workspace.checkThirdParty": false,
          "diagnostics.globals": ["log", "vars", "json", "fs", "process", "http"]
        }
        """;

    private static string LoadDefinitions()
    {
        var assembly = typeof(LuaWorkspaceScaffold).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Embedded resource '{ResourceName}' not found in {assembly.GetName().Name}. " +
                "Ensure BotBuilder.Core.csproj includes the EmbeddedResource item for Assets/lua/adb.lua.");

        using var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
