using AdbCore.Actions.BuiltIn.Android;
using AdbCore.Actions.BuiltIn.Browser;
using AdbCore.Execution;
using AdbCore.Input;
using AdbCore.Screen;
using AdbCore.Window;

namespace AdbCore.Actions.BuiltIn;

/// <summary>Registers the built-in action set into the definition and executor registries.</summary>
public static class BuiltInActions
{
    public static IDisposable Register(ActionRegistry definitions, ActionExecutorRegistry executors)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        ArgumentNullException.ThrowIfNull(executors);

        Add(new StartAction(), definitions, executors);
        Add(new EndAction(), definitions, executors);
        Add(new LogAction(), definitions, executors);
        Add(new DelayAction(), definitions, executors);
        Add(new BranchAction(), definitions, executors);
        Add(new SetVariableAction(), definitions, executors);
        Add(new CommentAction(), definitions, executors);
        Add(new MathAction(), definitions, executors);

        // Scripting (no external deps — MoonSharp is in-process; two-way `vars` bridge to run variables).
        Add(new RunLuaScriptAction(), definitions, executors);

        // Input actions share one resolver: SendInput (foreground, default) + PostMessage (background, opt-in per node).
        var inputSenders = new InputSenderResolver(new Win32SendInputSender(), new Win32PostMessageSender());
        Add(new ClickAction(inputSenders), definitions, executors);
        Add(new RightClickAction(inputSenders), definitions, executors);
        Add(new DoubleClickAction(inputSenders), definitions, executors);
        Add(new MouseMoveAction(inputSenders), definitions, executors);
        Add(new TypeTextAction(inputSenders), definitions, executors);
        Add(new KeyPressAction(inputSenders), definitions, executors);

        // Screen actions share one capture + matcher + RNG (OpenCvSharp/Win32 adapters; foreground-bound).
        var windowCapture = new Win32WindowCapture();
        var templateMatcher = new OpenCvSharpTemplateMatcher();
        var randomSource = new SystemRandomSource();
        Add(new FindImageAction(windowCapture, templateMatcher, randomSource), definitions, executors);
        Add(new WaitForImageAction(windowCapture, templateMatcher, randomSource), definitions, executors);
        Add(new AssertImageAbsentAction(windowCapture, templateMatcher), definitions, executors);
        Add(new ScreenshotAction(windowCapture), definitions, executors);

        // Screen OCR (Tesseract; reuses the window capture + RNG. The engine is internally locked for concurrency.)
        var ocrEngine = new AdbCore.Ocr.TesseractOcrEngine();
        Add(new ReadTextAction(windowCapture, ocrEngine), definitions, executors);
        Add(new FindTextAction(windowCapture, ocrEngine, randomSource), definitions, executors);
        Add(new WaitForTextAction(windowCapture, ocrEngine, randomSource), definitions, executors);
        Add(new AssertTextAbsentAction(windowCapture, ocrEngine), definitions, executors);

        // Android (handle-based — the bound IAndroidDevice is the ResolvedTarget handle; no injection).
        Add(new TapAction(), definitions, executors);
        Add(new SwipeAction(), definitions, executors);
        Add(new PressBackAction(), definitions, executors);
        Add(new LaunchAppAction(), definitions, executors);
        Add(new InstallApkAction(), definitions, executors);
        Add(new AndroidScreenshotAction(), definitions, executors);

        // Android image matching (handle-based device + injected matcher/RNG; mirrors Screen via TemplateMatchCore).
        Add(new AndroidFindImageAction(templateMatcher, randomSource), definitions, executors);
        Add(new AndroidWaitForImageAction(templateMatcher, randomSource), definitions, executors);
        Add(new AndroidAssertImageAbsentAction(templateMatcher), definitions, executors);

        // Android OCR (Tesseract; reuses the shared OCR engine + RNG).
        Add(new AndroidReadTextAction(ocrEngine), definitions, executors);
        Add(new AndroidFindTextAction(ocrEngine, randomSource), definitions, executors);
        Add(new AndroidWaitForTextAction(ocrEngine, randomSource), definitions, executors);
        Add(new AndroidAssertTextAbsentAction(ocrEngine), definitions, executors);

        // Window actions (injected activator; same HWND resolution as Screen/Input).
        Add(new ActivateWindowAction(new Win32WindowActivator()), definitions, executors);

        // Browser (handle-based — the bound IBrowserPage is the ResolvedTarget handle; no injection).
        Add(new OpenUrlAction(), definitions, executors);
        Add(new BrowserClickAction(), definitions, executors);
        Add(new BrowserTypeAction(), definitions, executors);
        Add(new WaitForSelectorAction(), definitions, executors);
        Add(new GetTextAction(), definitions, executors);

        // Loop is engine-native: register its definition only (no executor).
        definitions.Register(new LoopAction());
        // Loop-Break is engine-native: definition only (no executor).
        definitions.Register(new LoopBreakAction());

        // Run Parallel and Join are engine-native: register their definitions only (no executors).
        definitions.Register(new RunParallelAction());
        definitions.Register(new JoinAction());

        // Nested Bot: a leaf card that runs another bot from the library as a child executor. The executor
        // captures the executor registry so it can build child BotExecutors (including for deeper nesting).
        definitions.Register(new NestedBotAction());
        executors.Register(new NestedBotExecutor(executors));

        return ocrEngine;
    }

    private static void Add<T>(T action, ActionRegistry definitions, ActionExecutorRegistry executors)
        where T : IActionDefinition, IActionExecutor
    {
        definitions.Register(action);
        executors.Register(action);
    }
}
