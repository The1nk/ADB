using AdbCore.Execution;

namespace AdbCore.Actions.BuiltIn.Android;

/// <summary>Installs an APK file onto the device.</summary>
public sealed class InstallApkAction : AndroidActionBase
{
    public override string TypeKey => "android.installApk";
    public override string DisplayName => "Install APK";
    public override string Description => "Installs an APK file onto the device.";

    public override List<ConfigField> ConfigFields { get; } = new()
    {
        new ConfigField { Key = "apkPath", Label = "APK File", Type = ConfigFieldType.FilePath, DefaultValue = "" },
    };

    public override Task<ActionResult> ExecuteAsync(ActionExecutionContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (ResolveDevice(context) is not { } device)
        {
            return Task.FromResult(RequiresDevice());
        }

        var apkPath = ConfigValues.GetString(context.Action.Config, "apkPath");
        if (string.IsNullOrWhiteSpace(apkPath))
        {
            return Task.FromResult(ActionResult.Fail("Install APK: an APK file path is required."));
        }

        device.InstallApk(apkPath);
        return Task.FromResult(ActionResult.Ok(SuccessPort));
    }
}
