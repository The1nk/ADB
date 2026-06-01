# M1 — Core + Schema Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the `AdbCore` class library with the bot domain model, `.bot` JSON serialization (version-tagged, round-trippable), and an action-definition registry skeleton — the shared foundation both executables will build on.

**Architecture:** A single .NET class library (`AdbCore`) with three cohesive namespaces — `AdbCore.Models` (POCO domain model), `AdbCore.Serialization` (`BotSerializer` using `System.Text.Json` with a version envelope), and `AdbCore.Actions` (the `IActionDefinition` contract plus an `ActionRegistry` that catalogues action types by key). A sibling xUnit project (`AdbCore.Tests`) drives every type via TDD. No UI, no execution engine, and no external/optional dependencies in this milestone — those arrive in later milestones.

**Tech Stack:** C# / .NET 10 (`net10.0-windows`), `System.Text.Json` (BCL, no extra package), xUnit for tests. Targets the design in `Docs/Design/V1.md` §4.1–4.3 and §4.5, milestone M1 (§9).

---

## Scope

In scope (design §4.1, §4.3 definition-side, §4.5, §9 M1):
- Solution scaffold: `ADB.sln`, `AdbCore`, `AdbCore.Tests`.
- Domain models: `Bot`, `BotTarget`, `BotTargetType`, `BotAction`, `RetryPolicy`, `ActionConnection`, `Position`.
- Serialization: `BotSerializer` — `Serialize`/`Deserialize`/`Save`/`Load`, version-tagged (`"1.0"`), camelCase, enums-as-strings, `CanvasPosition`↔`"position"`.
- Action registry skeleton: `ConfigFieldType`, `ConfigField`, `PortDefinition`, `IActionDefinition`, `ActionRegistry`.

Explicitly **out of scope** for M1 (deferred to later milestones, do NOT build):
- `ExecutionContext` / `ResolvedTarget` / `IActionExecutor` / `ActionResult` / `BotExecutor` (these are M2 — Runner/engine).
- Any concrete action *executors* or shipped built-in action definitions (M5).
- The `BotBuilder`, `BotRunner`, `BotCapture` projects.

## File Structure

```
ADB.sln
AdbCore/
  AdbCore.csproj
  Models/
    Position.cs            # canvas coordinate
    BotTargetType.cs       # enum: Window | AndroidDevice | Browser
    BotTarget.cs           # named automation target
    RetryPolicy.cs         # per-action retry config
    ActionConnection.cs    # directed edge between actions
    BotAction.cs           # a node in the graph
    Bot.cs                 # the aggregate root (DAG of actions)
  Serialization/
    BotSerializer.cs       # .bot read/write with version envelope
  Actions/
    ConfigFieldType.cs     # enum of properties-panel field kinds
    ConfigField.cs         # one config field's metadata
    PortDefinition.cs      # a named input/output port
    IActionDefinition.cs   # the action-type contract (registry entry)
    ActionRegistry.cs      # catalogue of IActionDefinition by TypeKey
AdbCore.Tests/
  AdbCore.Tests.csproj
  SmokeTests.cs
  Models/BotModelTests.cs
  Serialization/BotSerializerTests.cs
  Actions/ActionRegistryTests.cs
  Actions/FakeActionDefinition.cs   # test double implementing IActionDefinition
```

---

### Task 1: Solution & project scaffolding

Creates the solution and both projects, wires the test→core reference, retargets to `net10.0-windows`, and proves the test harness runs with a trivial smoke test.

**Files:**
- Create: `ADB.sln`, `AdbCore/AdbCore.csproj`, `AdbCore.Tests/AdbCore.Tests.csproj`
- Create: `AdbCore.Tests/SmokeTests.cs`
- Delete: `AdbCore/Class1.cs`, `AdbCore.Tests/UnitTest1.cs` (template leftovers)

- [ ] **Step 1: Scaffold solution and projects**

Run from the repo root (the worktree root):

```bash
dotnet new sln -n ADB
dotnet new classlib -o AdbCore
dotnet new xunit -o AdbCore.Tests
dotnet sln add AdbCore/AdbCore.csproj AdbCore.Tests/AdbCore.Tests.csproj
dotnet add AdbCore.Tests/AdbCore.Tests.csproj reference AdbCore/AdbCore.csproj
```

- [ ] **Step 2: Retarget AdbCore to `net10.0-windows`**

Overwrite `AdbCore/AdbCore.csproj` with exactly:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>AdbCore</RootNamespace>
  </PropertyGroup>

</Project>
```

Delete the template file `AdbCore/Class1.cs`.

- [ ] **Step 3: Retarget the test project to `net10.0-windows`**

In `AdbCore.Tests/AdbCore.Tests.csproj`, change the `<TargetFramework>` value from `net10.0` to `net10.0-windows`. **Leave the template's `<PackageReference>` versions exactly as generated** (they are matched to the installed SDK). The file should look like this, with the package versions left as whatever the template produced:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0-windows</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <!-- PackageReference items: leave exactly as generated by `dotnet new xunit` -->
  <!-- ProjectReference to AdbCore: added by `dotnet add reference` in Step 1 -->

</Project>
```

Delete the template file `AdbCore.Tests/UnitTest1.cs`.

- [ ] **Step 4: Write the smoke test**

Create `AdbCore.Tests/SmokeTests.cs`:

```csharp
using Xunit;

namespace AdbCore.Tests;

public class SmokeTests
{
    [Fact]
    public void TestHarness_Runs()
    {
        Assert.True(true);
    }
}
```

- [ ] **Step 5: Build and test**

Run: `dotnet test`
Expected: build succeeds; `1` test passes, `0` failures.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "chore: scaffold ADB solution with AdbCore library and test project"
```

---

### Task 2: Domain models

Implements the seven POCO domain types from design §4.1. Tests assert collection/reference properties default to non-null empties (the design treats them as always-present lists) and that values round-trip through properties.

**Files:**
- Create: `AdbCore/Models/Position.cs`, `BotTargetType.cs`, `BotTarget.cs`, `RetryPolicy.cs`, `ActionConnection.cs`, `BotAction.cs`, `Bot.cs`
- Test: `AdbCore.Tests/Models/BotModelTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `AdbCore.Tests/Models/BotModelTests.cs`:

```csharp
using AdbCore.Models;
using Xunit;

namespace AdbCore.Tests.Models;

public class BotModelTests
{
    [Fact]
    public void Bot_NewInstance_HasNonNullEmptyCollections()
    {
        var bot = new Bot();

        Assert.NotNull(bot.Targets);
        Assert.Empty(bot.Targets);
        Assert.NotNull(bot.Actions);
        Assert.Empty(bot.Actions);
        Assert.NotNull(bot.Connections);
        Assert.Empty(bot.Connections);
    }

    [Fact]
    public void BotAction_NewInstance_HasNonNullConfigAndPosition()
    {
        var action = new BotAction();

        Assert.NotNull(action.Config);
        Assert.Empty(action.Config);
        Assert.NotNull(action.CanvasPosition);
        Assert.Null(action.Retry);
        Assert.Null(action.TargetId);
    }

    [Fact]
    public void BotTarget_NewInstance_HasNonNullEmptyConfig()
    {
        var target = new BotTarget();

        Assert.NotNull(target.Config);
        Assert.Empty(target.Config);
    }

    [Fact]
    public void BotAction_PropertiesRoundTripThroughGetSet()
    {
        var id = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var action = new BotAction
        {
            Id = id,
            TypeKey = "screen.findImage",
            Label = "Find Attack Button",
            TargetId = targetId,
            Retry = new RetryPolicy { MaxAttempts = 5, DelayMs = 500 },
            CanvasPosition = new Position { X = 120, Y = 80 },
        };
        action.Config["confidence"] = 0.9;

        Assert.Equal(id, action.Id);
        Assert.Equal("screen.findImage", action.TypeKey);
        Assert.Equal("Find Attack Button", action.Label);
        Assert.Equal(targetId, action.TargetId);
        Assert.Equal(5, action.Retry!.MaxAttempts);
        Assert.Equal(500, action.Retry.DelayMs);
        Assert.Equal(120, action.CanvasPosition.X);
        Assert.Equal(80, action.CanvasPosition.Y);
        Assert.Equal(0.9, action.Config["confidence"]);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test`
Expected: build FAILS — `Bot`, `BotAction`, `BotTarget`, `RetryPolicy`, `Position` types do not exist.

- [ ] **Step 3: Create the model types**

Create `AdbCore/Models/Position.cs`:

```csharp
namespace AdbCore.Models;

/// <summary>A 2D coordinate on the editor canvas.</summary>
public class Position
{
    public double X { get; set; }
    public double Y { get; set; }
}
```

Create `AdbCore/Models/BotTargetType.cs`:

```csharp
namespace AdbCore.Models;

/// <summary>The kind of automation target a <see cref="BotTarget"/> represents.</summary>
public enum BotTargetType
{
    /// <summary>Win32 HWND / FlaUI — desktop apps or emulators.</summary>
    Window,

    /// <summary>ADB device.</summary>
    AndroidDevice,

    /// <summary>Playwright browser context.</summary>
    Browser,
}
```

Create `AdbCore/Models/BotTarget.cs`:

```csharp
namespace AdbCore.Models;

/// <summary>A named automation target — window, Android device, or browser context.</summary>
public class BotTarget
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public BotTargetType Type { get; set; }

    /// <summary>Type-specific configuration, e.g. { "selector": "process:BlueStacks" }.</summary>
    public Dictionary<string, string> Config { get; set; } = new();
}
```

Create `AdbCore/Models/RetryPolicy.cs`:

```csharp
namespace AdbCore.Models;

/// <summary>Per-action retry configuration for flaky operations.</summary>
public class RetryPolicy
{
    public int MaxAttempts { get; set; }
    public int DelayMs { get; set; }
}
```

Create `AdbCore/Models/ActionConnection.cs`:

```csharp
namespace AdbCore.Models;

/// <summary>A directed edge between two actions in the graph.</summary>
public class ActionConnection
{
    public Guid Id { get; set; }
    public Guid SourceActionId { get; set; }
    public string SourcePort { get; set; } = string.Empty;
    public Guid TargetActionId { get; set; }
    public string TargetPort { get; set; } = string.Empty;
}
```

Create `AdbCore/Models/BotAction.cs`:

```csharp
using System.Text.Json.Serialization;

namespace AdbCore.Models;

/// <summary>A single action node in the bot graph.</summary>
public class BotAction
{
    public Guid Id { get; set; }

    /// <summary>The action type key, e.g. "screen.findImage", "android.tap".</summary>
    public string TypeKey { get; set; } = string.Empty;

    /// <summary>User-editable display name.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>Which target this action operates on; null means the first target.</summary>
    public Guid? TargetId { get; set; }

    /// <summary>Action-specific settings.</summary>
    public Dictionary<string, object> Config { get; set; } = new();

    /// <summary>Optional per-action retry configuration.</summary>
    public RetryPolicy? Retry { get; set; }

    [JsonPropertyName("position")]
    public Position CanvasPosition { get; set; } = new();
}
```

Create `AdbCore/Models/Bot.cs`:

```csharp
namespace AdbCore.Models;

/// <summary>A bot is a named DAG of actions and the connections between them.</summary>
public class Bot
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Named targets, resolved to live handles at run start.</summary>
    public List<BotTarget> Targets { get; set; } = new();

    public List<BotAction> Actions { get; set; } = new();
    public List<ActionConnection> Connections { get; set; } = new();

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: all tests pass (smoke + 4 model tests), `0` failures.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): add bot domain model"
```

---

### Task 3: `.bot` serialization

Implements `BotSerializer` (design §4.5): JSON with a top-level `"version"` envelope, camelCase property names, enums as strings, and `CanvasPosition` serialized as `"position"`. Round-trip fidelity is verified by re-serialization equality (which sidesteps the fact that `Dictionary<string, object>` values deserialize as `JsonElement`).

**Files:**
- Create: `AdbCore/Serialization/BotSerializer.cs`
- Test: `AdbCore.Tests/Serialization/BotSerializerTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `AdbCore.Tests/Serialization/BotSerializerTests.cs`:

```csharp
using System.Text.Json.Nodes;
using AdbCore.Models;
using AdbCore.Serialization;
using Xunit;

namespace AdbCore.Tests.Serialization;

public class BotSerializerTests
{
    private static Bot BuildSampleBot()
    {
        var targetWindowId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var targetPhoneId = Guid.Parse("22222222-2222-2222-2222-222222222222");
        var actionId = Guid.Parse("33333333-3333-3333-3333-333333333333");
        var action2Id = Guid.Parse("44444444-4444-4444-4444-444444444444");

        var bot = new Bot
        {
            Id = Guid.Parse("a1a1a1a1-a1a1-a1a1-a1a1-a1a1a1a1a1a1"),
            Name = "Farm Gold",
            Description = "Sample bot",
            CreatedAt = new DateTime(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 6, 1, 12, 30, 0, DateTimeKind.Utc),
        };
        bot.Targets.Add(new BotTarget
        {
            Id = targetWindowId,
            Name = "Client 1",
            Type = BotTargetType.Window,
            Config = { ["selector"] = "process:BlueStacks" },
        });
        bot.Targets.Add(new BotTarget
        {
            Id = targetPhoneId,
            Name = "My Phone",
            Type = BotTargetType.AndroidDevice,
            Config = { ["selector"] = "serial:emulator-5554" },
        });

        var findImage = new BotAction
        {
            Id = actionId,
            TypeKey = "screen.findImage",
            Label = "Find Attack Button",
            TargetId = targetWindowId,
            Retry = new RetryPolicy { MaxAttempts = 5, DelayMs = 500 },
            CanvasPosition = new Position { X = 120, Y = 80 },
        };
        findImage.Config["templatePath"] = "assets/attack-btn.png";
        findImage.Config["confidence"] = 0.9;
        bot.Actions.Add(findImage);

        bot.Connections.Add(new ActionConnection
        {
            Id = Guid.Parse("55555555-5555-5555-5555-555555555555"),
            SourceActionId = actionId,
            SourcePort = "onSuccess",
            TargetActionId = action2Id,
            TargetPort = "input",
        });

        return bot;
    }

    [Fact]
    public void Serialize_Deserialize_Serialize_IsStable()
    {
        var serializer = new BotSerializer();
        var bot = BuildSampleBot();

        var json1 = serializer.Serialize(bot);
        var bot2 = serializer.Deserialize(json1);
        var json2 = serializer.Serialize(bot2);

        Assert.Equal(json1, json2);
    }

    [Fact]
    public void Serialize_IncludesVersionEnvelope()
    {
        var serializer = new BotSerializer();

        var json = serializer.Serialize(BuildSampleBot());
        var root = JsonNode.Parse(json)!.AsObject();

        Assert.Equal("1.0", root["version"]!.GetValue<string>());
    }

    [Fact]
    public void Serialize_WritesEnumsAsStrings()
    {
        var serializer = new BotSerializer();

        var json = serializer.Serialize(BuildSampleBot());
        var root = JsonNode.Parse(json)!.AsObject();

        Assert.Equal("Window", root["targets"]![0]!["type"]!.GetValue<string>());
        Assert.Equal("AndroidDevice", root["targets"]![1]!["type"]!.GetValue<string>());
    }

    [Fact]
    public void Serialize_WritesCanvasPositionAsPosition()
    {
        var serializer = new BotSerializer();

        var json = serializer.Serialize(BuildSampleBot());
        var root = JsonNode.Parse(json)!.AsObject();
        var action = root["actions"]![0]!.AsObject();

        Assert.NotNull(action["position"]);
        Assert.False(action.ContainsKey("canvasPosition"));
        Assert.Equal(120, action["position"]!["x"]!.GetValue<double>());
    }

    [Fact]
    public void Deserialize_PreservesStronglyTypedFields()
    {
        var serializer = new BotSerializer();
        var bot = BuildSampleBot();

        var roundTripped = serializer.Deserialize(serializer.Serialize(bot));

        Assert.Equal(bot.Name, roundTripped.Name);
        Assert.Equal(bot.Id, roundTripped.Id);
        Assert.Equal(2, roundTripped.Targets.Count);
        Assert.Equal(BotTargetType.AndroidDevice, roundTripped.Targets[1].Type);
        Assert.Equal(5, roundTripped.Actions[0].Retry!.MaxAttempts);
        Assert.Equal("onSuccess", roundTripped.Connections[0].SourcePort);
    }

    [Fact]
    public void SaveLoad_RoundTripsThroughDisk()
    {
        var serializer = new BotSerializer();
        var bot = BuildSampleBot();
        var path = Path.Combine(Path.GetTempPath(), $"adb-test-{Guid.NewGuid():N}.bot");

        try
        {
            serializer.Save(bot, path);
            var loaded = serializer.Load(path);

            Assert.Equal(serializer.Serialize(bot), serializer.Serialize(loaded));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void Deserialize_UnsupportedVersion_Throws()
    {
        var serializer = new BotSerializer();

        Assert.Throws<NotSupportedException>(
            () => serializer.Deserialize("{\"version\":\"0.1\"}"));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test`
Expected: build FAILS — `BotSerializer` does not exist.

- [ ] **Step 3: Implement `BotSerializer`**

Create `AdbCore/Serialization/BotSerializer.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using AdbCore.Models;

namespace AdbCore.Serialization;

/// <summary>Reads and writes <see cref="Bot"/> instances as version-tagged `.bot` JSON files.</summary>
public class BotSerializer
{
    /// <summary>The schema version this serializer reads and writes.</summary>
    public const string SchemaVersion = "1.0";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>Serializes a bot to its `.bot` JSON representation, with the version envelope first.</summary>
    public string Serialize(Bot bot)
    {
        var body = JsonSerializer.SerializeToNode(bot, Options)!.AsObject();

        // Rebuild so "version" is the first property; re-parenting requires detaching from `body`.
        var result = new JsonObject { ["version"] = SchemaVersion };
        foreach (var property in body.ToArray())
        {
            body.Remove(property.Key);
            result[property.Key] = property.Value;
        }

        return result.ToJsonString(Options);
    }

    /// <summary>Parses `.bot` JSON into a <see cref="Bot"/>, validating the schema version.</summary>
    public Bot Deserialize(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject()
            ?? throw new InvalidDataException("Bot content is not a JSON object.");

        var version = root["version"]?.GetValue<string>();
        if (version != SchemaVersion)
        {
            throw new NotSupportedException(
                $"Unsupported .bot version '{version ?? "(none)"}'. Expected '{SchemaVersion}'.");
        }

        return root.Deserialize<Bot>(Options)
            ?? throw new InvalidDataException("Failed to deserialize bot.");
    }

    /// <summary>Serializes a bot and writes it to <paramref name="path"/>.</summary>
    public void Save(Bot bot, string path)
        => File.WriteAllText(path, Serialize(bot));

    /// <summary>Reads and deserializes a bot from <paramref name="path"/>.</summary>
    public Bot Load(string path)
        => Deserialize(File.ReadAllText(path));
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: all tests pass, `0` failures.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): add .bot JSON serializer with version envelope"
```

---

### Task 4: Action-definition registry skeleton

Implements the action-type *contract* and a registry that catalogues definitions by `TypeKey` (design §4.3). No concrete action executors here — those are M5. The registry is exercised in tests via a `FakeActionDefinition` test double.

**Files:**
- Create: `AdbCore/Actions/ConfigFieldType.cs`, `ConfigField.cs`, `PortDefinition.cs`, `IActionDefinition.cs`, `ActionRegistry.cs`
- Test: `AdbCore.Tests/Actions/ActionRegistryTests.cs`, `AdbCore.Tests/Actions/FakeActionDefinition.cs`

- [ ] **Step 1: Write the failing tests**

Create `AdbCore.Tests/Actions/FakeActionDefinition.cs`:

```csharp
using AdbCore.Actions;

namespace AdbCore.Tests.Actions;

/// <summary>Minimal test double for exercising the registry without shipping real actions.</summary>
internal sealed class FakeActionDefinition : IActionDefinition
{
    public required string TypeKey { get; init; }
    public string DisplayName { get; init; } = "Fake";
    public string Category { get; init; } = "Test";
    public string Description { get; init; } = "A fake action for tests.";
    public List<PortDefinition> InputPorts { get; init; } = new();
    public List<PortDefinition> OutputPorts { get; init; } = new();
    public List<ConfigField> ConfigFields { get; init; } = new();
    public bool SupportsRetry { get; init; }
}
```

Create `AdbCore.Tests/Actions/ActionRegistryTests.cs`:

```csharp
using AdbCore.Actions;
using Xunit;

namespace AdbCore.Tests.Actions;

public class ActionRegistryTests
{
    [Fact]
    public void Register_ThenGet_ReturnsSameInstance()
    {
        var registry = new ActionRegistry();
        var def = new FakeActionDefinition { TypeKey = "test.alpha" };

        registry.Register(def);

        Assert.Same(def, registry.Get("test.alpha"));
        Assert.Equal(1, registry.Count);
    }

    [Fact]
    public void TryGet_UnknownKey_ReturnsFalseAndNull()
    {
        var registry = new ActionRegistry();

        var found = registry.TryGet("does.not.exist", out var def);

        Assert.False(found);
        Assert.Null(def);
    }

    [Fact]
    public void Get_UnknownKey_Throws()
    {
        var registry = new ActionRegistry();

        Assert.Throws<KeyNotFoundException>(() => registry.Get("does.not.exist"));
    }

    [Fact]
    public void Register_DuplicateKey_Throws()
    {
        var registry = new ActionRegistry();
        registry.Register(new FakeActionDefinition { TypeKey = "test.dup" });

        var ex = Assert.Throws<InvalidOperationException>(
            () => registry.Register(new FakeActionDefinition { TypeKey = "test.dup" }));
        Assert.Contains("test.dup", ex.Message);
    }

    [Fact]
    public void Register_Null_Throws()
    {
        var registry = new ActionRegistry();

        Assert.Throws<ArgumentNullException>(() => registry.Register(null!));
    }

    [Fact]
    public void GetByCategory_ReturnsOnlyMatching()
    {
        var registry = new ActionRegistry();
        registry.Register(new FakeActionDefinition { TypeKey = "a", Category = "Screen" });
        registry.Register(new FakeActionDefinition { TypeKey = "b", Category = "Screen" });
        registry.Register(new FakeActionDefinition { TypeKey = "c", Category = "Android" });

        var screen = registry.GetByCategory("Screen").ToList();

        Assert.Equal(2, screen.Count);
        Assert.All(screen, d => Assert.Equal("Screen", d.Category));
    }

    [Fact]
    public void All_ReturnsEveryRegisteredDefinition()
    {
        var registry = new ActionRegistry();
        registry.Register(new FakeActionDefinition { TypeKey = "a" });
        registry.Register(new FakeActionDefinition { TypeKey = "b" });

        Assert.Equal(2, registry.All.Count);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test`
Expected: build FAILS — `IActionDefinition`, `ActionRegistry`, `PortDefinition`, `ConfigField` do not exist.

- [ ] **Step 3: Implement the action contract and registry**

Create `AdbCore/Actions/ConfigFieldType.cs`:

```csharp
namespace AdbCore.Actions;

/// <summary>The kind of UI control a <see cref="ConfigField"/> renders as in the properties panel.</summary>
public enum ConfigFieldType
{
    String,
    MultilineString,
    Number,
    Boolean,
    Enum,
    FilePath,
    ImagePath,
}
```

Create `AdbCore/Actions/ConfigField.cs`:

```csharp
namespace AdbCore.Actions;

/// <summary>Metadata describing one configurable field of an action, used to drive the properties panel.</summary>
public class ConfigField
{
    public string Key { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public ConfigFieldType Type { get; set; }
    public object? DefaultValue { get; set; }

    /// <summary>Allowed values when <see cref="Type"/> is <see cref="ConfigFieldType.Enum"/>.</summary>
    public List<string> Options { get; set; } = new();
}
```

Create `AdbCore/Actions/PortDefinition.cs`:

```csharp
namespace AdbCore.Actions;

/// <summary>A named input or output port on an action node.</summary>
public class PortDefinition
{
    public string Name { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
}
```

Create `AdbCore/Actions/IActionDefinition.cs`:

```csharp
namespace AdbCore.Actions;

/// <summary>
/// Describes an action type — its identity, ports, and configurable fields.
/// This is the primary extension point for adding new actions.
/// </summary>
public interface IActionDefinition
{
    /// <summary>Unique key, e.g. "screen.findImage".</summary>
    string TypeKey { get; }

    string DisplayName { get; }

    /// <summary>Category for palette grouping, e.g. "Screen", "Android", "Control Flow".</summary>
    string Category { get; }

    string Description { get; }

    List<PortDefinition> InputPorts { get; }
    List<PortDefinition> OutputPorts { get; }

    /// <summary>Fields that drive the properties-panel form.</summary>
    List<ConfigField> ConfigFields { get; }

    /// <summary>Whether a retry policy is applicable to this action.</summary>
    bool SupportsRetry { get; }
}
```

Create `AdbCore/Actions/ActionRegistry.cs`:

```csharp
namespace AdbCore.Actions;

/// <summary>Catalogue of available action types, keyed by <see cref="IActionDefinition.TypeKey"/>.</summary>
public class ActionRegistry
{
    private readonly Dictionary<string, IActionDefinition> _byKey = new(StringComparer.Ordinal);

    /// <summary>The number of registered action definitions.</summary>
    public int Count => _byKey.Count;

    /// <summary>All registered action definitions.</summary>
    public IReadOnlyCollection<IActionDefinition> All => _byKey.Values;

    /// <summary>Registers a definition. Throws if its <see cref="IActionDefinition.TypeKey"/> is already registered.</summary>
    public void Register(IActionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (!_byKey.TryAdd(definition.TypeKey, definition))
        {
            throw new InvalidOperationException(
                $"An action with TypeKey '{definition.TypeKey}' is already registered.");
        }
    }

    /// <summary>Looks up a definition by key, returning false if not found.</summary>
    public bool TryGet(string typeKey, out IActionDefinition? definition)
        => _byKey.TryGetValue(typeKey, out definition);

    /// <summary>Gets a definition by key. Throws <see cref="KeyNotFoundException"/> if not found.</summary>
    public IActionDefinition Get(string typeKey)
    {
        if (!_byKey.TryGetValue(typeKey, out var definition))
        {
            throw new KeyNotFoundException($"No action registered with TypeKey '{typeKey}'.");
        }

        return definition;
    }

    /// <summary>Returns all definitions in the given category.</summary>
    public IEnumerable<IActionDefinition> GetByCategory(string category)
        => _byKey.Values.Where(d => d.Category == category);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test`
Expected: all tests pass, `0` failures.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(core): add action definition contract and registry"
```

---

## Self-Review

**Spec coverage (design §9 M1):**
- `AdbCore` library — Task 1. ✓
- Domain models including `BotTarget` (§4.1) — Task 2 (all 7 types). ✓
- `.bot` serialization (§4.5: version-tagged, human-readable JSON) — Task 3. ✓
- Action registry skeleton (§4.3 definition side) — Task 4. ✓

**Placeholder scan:** No "TBD"/"handle edge cases"/"similar to" — every code step has complete content. ✓

**Type consistency:** `Bot`/`BotTarget`/`BotTargetType`/`BotAction`/`RetryPolicy`/`ActionConnection`/`Position` defined in Task 2 are the exact names used in Task 3 tests/serializer. `IActionDefinition` members defined in Task 4 (`TypeKey`, `DisplayName`, `Category`, `Description`, `InputPorts`, `OutputPorts`, `ConfigFields`, `SupportsRetry`) match `FakeActionDefinition` and `ActionRegistry` usage. `CanvasPosition`↔`"position"` mapping is consistent between the model (Task 2) and the serializer test (Task 3). ✓

**Deferred-type check:** No task references `ExecutionContext`, `IActionExecutor`, `ActionResult`, or `BotExecutor` — those are intentionally M2/M5. ✓
