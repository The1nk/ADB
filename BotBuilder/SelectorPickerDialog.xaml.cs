using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using AdbCore.Android;
using AdbCore.Models;
using AdbCore.Targets;
using BotBuilder.Core.Targets;

namespace BotBuilder;

/// <summary>Picks a single target selector for a chip, populated for the chip's <see cref="BotTargetType"/>:
/// open windows, connected ADB devices, or browser engines. Writes the chosen selector via SelectorFormat.</summary>
public partial class SelectorPickerDialog : Window
{
    public SelectorPickerDialog(BotTargetType type, IWindowEnumerator windows)
    {
        InitializeComponent();
        Choices.ItemsSource = BuildChoices(type, windows);
    }

    /// <summary>The chosen selector string, valid after the dialog returns true.</summary>
    public string? ChosenSelector { get; private set; }

    private sealed record Choice(string Display, string Selector);

    private static IReadOnlyList<Choice> BuildChoices(BotTargetType type, IWindowEnumerator windows) => type switch
    {
        BotTargetType.Window => windows.Enumerate()
            .Select(w => new Choice(
                string.IsNullOrEmpty(w.ProcessName) ? w.Title : $"{w.ProcessName} — {w.Title}",
                SelectorFormat.Window(w.ProcessName, w.Title)))
            .ToList(),
        BotTargetType.AndroidDevice => ListDevices(),
        BotTargetType.Browser => AdbCore.Browser.BrowserSelector.Engines
            .Select(e => new Choice(e, SelectorFormat.Browser(e))).ToList(),
        _ => Array.Empty<Choice>(),
    };

    private static IReadOnlyList<Choice> ListDevices()
    {
        try
        {
            return new AdvancedSharpAdbDevices().List()
                .Select(d => new Choice($"{d.Serial} ({d.State})", SelectorFormat.Android(d.Serial)))
                .ToList();
        }
        catch
        {
            return Array.Empty<Choice>();   // no ADB server / devices — leave manual entry
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Choices.SelectedItem is Choice choice)
        {
            ChosenSelector = choice.Selector;
            DialogResult = true;
        }
        Close();
    }

    private void Choices_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Choices.SelectedItem is Choice)
        {
            Ok_Click(sender, e);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
