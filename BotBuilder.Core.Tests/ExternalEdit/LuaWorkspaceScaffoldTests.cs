using BotBuilder.Core.ExternalEdit;

namespace BotBuilder.Core.Tests.ExternalEdit;

public class LuaWorkspaceScaffoldTests
{
    // ---------------------------------------------------------------------------
    // Definitions (embedded resource)
    // ---------------------------------------------------------------------------

    [Fact]
    public void Definitions_IsNotEmpty()
    {
        Assert.NotEmpty(LuaWorkspaceScaffold.Definitions);
    }

    [Theory]
    [InlineData("function log")]
    [InlineData("vars")]
    [InlineData("json")]
    [InlineData("fs")]
    [InlineData("function process.run")]
    [InlineData("http.get")]
    [InlineData("http.post")]
    [InlineData("ProcessResult")]
    [InlineData("HttpResponse")]
    public void Definitions_ContainsExpectedDeclaration(string expectedFragment)
    {
        Assert.Contains(expectedFragment, LuaWorkspaceScaffold.Definitions, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------
    // LuaRcJson
    // ---------------------------------------------------------------------------

    [Fact]
    public void LuaRcJson_ContainsLibraryWorkspaceEntry()
    {
        Assert.Contains("\"library\"", LuaWorkspaceScaffold.LuaRcJson, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("log")]
    [InlineData("vars")]
    [InlineData("json")]
    [InlineData("fs")]
    [InlineData("process")]
    [InlineData("http")]
    public void LuaRcJson_ListsAllSixGlobalsInDiagnosticsGlobals(string globalName)
    {
        // The global name appears as a JSON string value inside "diagnostics.globals".
        Assert.Contains($"\"{globalName}\"", LuaWorkspaceScaffold.LuaRcJson, StringComparison.Ordinal);
    }

    // ---------------------------------------------------------------------------
    // Path constants
    // ---------------------------------------------------------------------------

    [Fact]
    public void LuaRcJsonRelativePath_IsLuaRcJson()
    {
        Assert.Equal(".luarc.json", LuaWorkspaceScaffold.LuaRcJsonRelativePath);
    }

    [Fact]
    public void DefinitionsRelativePath_IsLibraryAdbLua()
    {
        Assert.Equal("library/adb.lua", LuaWorkspaceScaffold.DefinitionsRelativePath);
    }

    [Fact]
    public void DefinitionsRelativePath_StartsWithLibraryPrefix()
    {
        // The library/ prefix must match the workspace.library entry in LuaRcJson.
        Assert.StartsWith("library/", LuaWorkspaceScaffold.DefinitionsRelativePath, StringComparison.Ordinal);
    }
}
