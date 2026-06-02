using System.Globalization;
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Evaluates a simple condition over a run variable and follows the "true" or "false" port.</summary>
public sealed class BranchAction : IActionDefinition, IActionExecutor
{
    public const string VariableKey = "variable";
    public const string OperatorKey = "operator";
    public const string ValueKey = "value";

    public const string TruePort = "true";
    public const string FalsePort = "false";

    public const string OpEquals = "Equals";
    public const string OpNotEquals = "NotEquals";
    public const string OpGreaterThan = "GreaterThan";
    public const string OpLessThan = "LessThan";
    public const string OpIsTrue = "IsTrue";
    public const string OpIsFalse = "IsFalse";
    public const string OpIsEmpty = "IsEmpty";
    public const string OpIsNotEmpty = "IsNotEmpty";

    public string TypeKey => "control.branch";
    public string DisplayName => "Branch";
    public string Category => "Control Flow";
    public string Description => "Follows the True or False path based on a condition over a variable.";
    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };
    public List<PortDefinition> OutputPorts { get; } = new()
    {
        new PortDefinition { Name = TruePort, Label = "True" },
        new PortDefinition { Name = FalsePort, Label = "False" },
    };
    public List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = VariableKey, Label = "Variable", Type = ConfigFieldType.String },
        new ConfigField
        {
            Key = OperatorKey,
            Label = "Operator",
            Type = ConfigFieldType.Enum,
            DefaultValue = OpEquals,
            Options = new() { OpEquals, OpNotEquals, OpGreaterThan, OpLessThan, OpIsTrue, OpIsFalse, OpIsEmpty, OpIsNotEmpty },
        },
        new ConfigField { Key = ValueKey, Label = "Value", Type = ConfigFieldType.String },
    };
    public bool SupportsRetry => false;

    public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        var variable = ConfigValues.GetString(context.Action.Config, VariableKey);
        var op = ConfigValues.GetString(context.Action.Config, OperatorKey, OpEquals);
        var operand = ConfigValues.GetString(context.Action.Config, ValueKey);

        var variableValue = context.Context.Variables.TryGetValue(variable, out var v) ? v : null;
        var matched = Evaluate(variableValue, op, operand);

        return Task.FromResult(ActionResult.Ok(matched ? TruePort : FalsePort));
    }

    private static bool Evaluate(object? variableValue, string op, string operand) => op switch
    {
        OpEquals => string.Equals(ConfigValues.AsString(variableValue), operand, StringComparison.Ordinal),
        OpNotEquals => !string.Equals(ConfigValues.AsString(variableValue), operand, StringComparison.Ordinal),
        OpGreaterThan => CompareNumbers(variableValue, operand, out var c) && c > 0,
        OpLessThan => CompareNumbers(variableValue, operand, out var c) && c < 0,
        OpIsTrue => ConfigValues.AsBool(variableValue),
        OpIsFalse => !ConfigValues.AsBool(variableValue),
        OpIsEmpty => string.IsNullOrWhiteSpace(ConfigValues.AsString(variableValue)),
        OpIsNotEmpty => !string.IsNullOrWhiteSpace(ConfigValues.AsString(variableValue)),
        _ => false,
    };

    private static bool CompareNumbers(object? variableValue, string operand, out int comparison)
    {
        comparison = 0;
        if (ConfigValues.TryAsDouble(variableValue, out var a)
            && double.TryParse(operand, NumberStyles.Any, CultureInfo.InvariantCulture, out var b))
        {
            comparison = a.CompareTo(b);
            return true;
        }

        return false;
    }
}
