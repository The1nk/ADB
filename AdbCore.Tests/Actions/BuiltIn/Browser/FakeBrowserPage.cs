using System.Collections.Generic;
using System.Threading.Tasks;
using AdbCore.Browser;

namespace AdbCore.Tests.Actions.BuiltIn.Browser;

internal sealed class FakeBrowserPage : IBrowserPage
{
    public List<string> Calls { get; } = new();
    public string TextResult { get; set; } = string.Empty;

    public Task GotoAsync(string url) { Calls.Add($"goto {url}"); return Task.CompletedTask; }
    public Task ClickAsync(string selector) { Calls.Add($"click {selector}"); return Task.CompletedTask; }
    public Task TypeAsync(string selector, string text) { Calls.Add($"type {selector} {text}"); return Task.CompletedTask; }
    public Task WaitForSelectorAsync(string selector, int timeoutMs) { Calls.Add($"wait {selector} {timeoutMs}"); return Task.CompletedTask; }
    public Task<string> GetTextAsync(string selector) { Calls.Add($"gettext {selector}"); return Task.FromResult(TextResult); }
}
