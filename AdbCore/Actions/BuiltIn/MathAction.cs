using System.Globalization;
using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Computes <c>operation(left, right)</c> over numeric literals/<c>${var}</c> operands and stores the
/// result (a double) in a run variable. Arithmetic plus standard-library functions (rounding, abs, sqrt,
/// min/max, power, random). Computation errors (non-numeric operand, divide/modulo by zero, non-finite result)
/// route to onFailure.</summary>
public sealed class MathAction : IActionDefinition, IActionExecutor
{
    public const string OperationKey = "operation";
    public const string LeftKey = "left";
    public const string RightKey = "right";
    public const string ResultKey = "resultVariable";

    public const string SuccessPort = "onSuccess";
    public const string FailurePort = "onFailure";

    public const string OpAdd = "Add";
    public const string OpSubtract = "Subtract";
    public const string OpMultiply = "Multiply";
    public const string OpDivide = "Divide";
    public const string OpModulo = "Modulo";
    public const string OpPower = "Power";
    public const string OpMin = "Min";
    public const string OpMax = "Max";
    public const string OpFloor = "Floor";
    public const string OpCeil = "Ceil";
    public const string OpRound = "Round";
    public const string OpAbs = "Abs";
    public const string OpSqrt = "Sqrt";
    public const string OpNegate = "Negate";
    public const string OpRandom = "Random";
    public const string OpRandomInt = "RandomInt";

    public string TypeKey => "data.math";
    public string DisplayName => "Math";
    public string Category => "Data";
    public string Description => "Computes an arithmetic or standard-library math operation and stores the result in a variable.";

    public List<PortDefinition> InputPorts { get; } = new() { new PortDefinition { Name = "in", Label = "In" } };

    public List<PortDefinition> OutputPorts { get; } = new()
    {
        new PortDefinition { Name = SuccessPort, Label = "On Success" },
        new PortDefinition { Name = FailurePort, Label = "On Failure" },
    };

    public List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField
        {
            Key = OperationKey,
            Label = "Operation",
            Type = ConfigFieldType.Enum,
            DefaultValue = OpAdd,
            Options = new()
            {
                OpAdd, OpSubtract, OpMultiply, OpDivide, OpModulo, OpPower, OpMin, OpMax,
                OpFloor, OpCeil, OpRound, OpAbs, OpSqrt, OpNegate,
                OpRandom, OpRandomInt,
            },
        },
        new ConfigField { Key = LeftKey, Label = "Left", Type = ConfigFieldType.String },
        new ConfigField { Key = RightKey, Label = "Right", Type = ConfigFieldType.String },
        new ConfigField { Key = ResultKey, Label = "Result Variable", Type = ConfigFieldType.String },
    };

    public bool SupportsRetry => false;

    public Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var op = ConfigValues.GetString(context.Action.Config, OperationKey, OpAdd);
        var leftText = ConfigValues.GetString(context.Action.Config, LeftKey);
        var rightText = ConfigValues.GetString(context.Action.Config, RightKey);
        var resultVar = ConfigValues.GetString(context.Action.Config, ResultKey);

        if (string.IsNullOrWhiteSpace(resultVar))
            return Task.FromResult(ActionResult.Fail("Math: result variable name is required"));

        if (!Compute(op, leftText, rightText, out var value, out var error))
            return Task.FromResult(ActionResult.Fail($"Math: {error}"));

        if (double.IsNaN(value) || double.IsInfinity(value))
            return Task.FromResult(ActionResult.Fail("Math: result is not a finite number"));

        context.Context.Variables[resultVar] = value;
        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }

    private static bool TryParse(string text, out double value)
        => double.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value);

    private static bool Compute(string op, string leftText, string rightText, out double value, out string error)
    {
        value = 0;
        error = string.Empty;

        if (op == OpRandom)
        {
            value = Random.Shared.NextDouble();
            return true;
        }

        if (!TryParse(leftText, out var a))
        {
            error = $"left operand '{leftText}' is not a number";
            return false;
        }

        switch (op)
        {
            case OpFloor:  value = Math.Floor(a);                               return true;
            case OpCeil:   value = Math.Ceiling(a);                             return true;
            case OpRound:  value = Math.Round(a, MidpointRounding.AwayFromZero); return true;
            case OpAbs:    value = Math.Abs(a);                                 return true;
            case OpSqrt:   value = Math.Sqrt(a);                                return true;
            case OpNegate: value = -a;                                          return true;
        }

        if (!TryParse(rightText, out var b))
        {
            error = $"right operand '{rightText}' is not a number";
            return false;
        }

        switch (op)
        {
            case OpAdd:      value = a + b; return true;
            case OpSubtract: value = a - b; return true;
            case OpMultiply: value = a * b; return true;
            case OpDivide:
                if (b == 0) { error = "divide by zero"; return false; }
                value = a / b; return true;
            case OpModulo:
                if (b == 0) { error = "modulo by zero"; return false; }
                value = a % b; return true;
            case OpPower: value = Math.Pow(a, b); return true;
            case OpMin:   value = Math.Min(a, b); return true;
            case OpMax:   value = Math.Max(a, b); return true;
            case OpRandomInt:
                var lo = (long)Math.Round(a, MidpointRounding.AwayFromZero);
                var hi = (long)Math.Round(b, MidpointRounding.AwayFromZero);
                if (lo > hi) { error = "RandomInt requires left (min) <= right (max)"; return false; }
                value = Random.Shared.NextInt64(lo, hi + 1);
                return true;
            default:
                error = $"unknown operation '{op}'";
                return false;
        }
    }
}
