// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using Itexoft.Extensions;
using Itexoft.TerminalKit.Interaction;
using Itexoft.TerminalKit.Rendering;

namespace Itexoft.TerminalKit.Presets;

/// <summary>
/// Console-based dialog that maps TerminalFormFieldDefinition metadata to CLI prompts.
/// </summary>
public sealed class TerminalFormDialog
{
    private readonly IReadOnlyList<TerminalFormFieldDefinition> fields;

    /// <summary>
    /// Initializes the dialog with field metadata.
    /// </summary>
    /// <param name="fields">Field descriptors to prompt.</param>
    public TerminalFormDialog(IReadOnlyList<TerminalFormFieldDefinition> fields) =>
        this.fields = fields ?? throw new ArgumentNullException(nameof(fields));

    /// <summary>
    /// Prompts the user for values covering every configured field.
    /// </summary>
    /// <param name="defaultProvider">Optional delegate that returns default values per field.</param>
    /// <param name="validator">Optional delegate that performs cross-field validation.</param>
    /// <param name="optionsProvider">Optional delegate that supplies dynamic option lists.</param>
    public IDictionary<DataBindingKey, string?> Prompt(
        Func<TerminalFormFieldDefinition, string?>? defaultProvider = null,
        Func<TerminalFormFieldDefinition, string?, string?>? validator = null,
        Func<TerminalFormFieldDefinition, IReadOnlyList<string>?>? optionsProvider = null) => RunWithExternalScope(() =>
    {
        try
        {
            return this.PromptInternal(defaultProvider, validator, optionsProvider);
        }
        catch (TerminalFormDialogCanceledException)
        {
            return new Dictionary<DataBindingKey, string?>();
        }
    });

    private IDictionary<DataBindingKey, string?> PromptInternal(
        Func<TerminalFormFieldDefinition, string?>? defaultProvider,
        Func<TerminalFormFieldDefinition, string?, string?>? validator,
        Func<TerminalFormFieldDefinition, IReadOnlyList<string>?>? optionsProvider)
    {
        var result = new Dictionary<DataBindingKey, string?>();

        foreach (var field in this.fields)
        {
            var value = PromptField(field, defaultProvider, validator, optionsProvider);
            result[field.Key] = value;
        }

        return result;
    }

    private static string? PromptField(
        TerminalFormFieldDefinition field,
        Func<TerminalFormFieldDefinition, string?>? defaultProvider,
        Func<TerminalFormFieldDefinition, string?, string?>? validator,
        Func<TerminalFormFieldDefinition, IReadOnlyList<string>?>? optionsProvider)
    {
        var defaultValue = defaultProvider?.Invoke(field);
        var options = optionsProvider?.Invoke(field) ?? field.Options;

        if (field.IsReadOnly)
            return defaultValue;

        if (options is { Count: > 0 })
            return PromptFromOptions(field, options, defaultValue, validator);

        return PromptTextField(field, defaultValue, validator);
    }

    private static string? PromptTextField(
        TerminalFormFieldDefinition field,
        string? defaultValue,
        Func<TerminalFormFieldDefinition, string?, string?>? validator)
    {
        while (true)
        {
            var prompt = BuildPrompt(field, defaultValue);
            Console.WriteLine($"{prompt}:");

            if (string.IsNullOrWhiteSpace(defaultValue))
                Console.WriteLine($"  Current value: {FormatCurrentValue(defaultValue)}");

            Console.Write("> ");
            var input = TerminalLineEditor.ReadLine(defaultValue, true, out var cancelled);

            if (cancelled)
                throw new TerminalFormDialogCanceledException();

            var raw = input ?? string.Empty;
            var value = string.IsNullOrEmpty(raw) ? string.Empty : raw.Trim();

            if (field.IsRequired && string.IsNullOrWhiteSpace(value))
            {
                Console.WriteLine($"{GetLabel(field)} is required.");

                continue;
            }

            var intrinsicError = field.Validate(value);

            if (!string.IsNullOrWhiteSpace(intrinsicError))
            {
                Console.WriteLine(intrinsicError);

                continue;
            }

            if (validator != null)
            {
                var error = validator(field, value);

                if (!string.IsNullOrWhiteSpace(error))
                {
                    Console.WriteLine(error);

                    continue;
                }
            }

            return value;
        }
    }

    private static string? PromptFromOptions(
        TerminalFormFieldDefinition field,
        IReadOnlyList<string> options,
        string? defaultValue,
        Func<TerminalFormFieldDefinition, string?, string?>? validator)
    {
        var selection = ResolveDefaultIndex(options, defaultValue);

        if (selection < 0)
            selection = 0;

        var label = BuildPrompt(field, defaultValue);
        Console.WriteLine($"{label} (Use ↑/↓, Home/End, Enter, Esc/← cancels):");
        var listTop = Console.CursorTop;
        RenderSelectableOptions(options, selection, listTop);
        var footerTop = listTop + options.Count;

        while (true)
        {
            Console.SetCursorPosition(0, footerTop);
            Console.Write(new string(' ', Math.Max(0, GetConsoleWidth() - 1)));
            Console.SetCursorPosition(0, footerTop);

            var key = Console.ReadKey(intercept: true);

            switch (key.Key)
            {
                case ConsoleKey.UpArrow:
                    selection = selection <= 0 ? options.Count - 1 : selection - 1;

                    break;
                case ConsoleKey.DownArrow:
                    selection = (selection + 1) % options.Count;

                    break;
                case ConsoleKey.Home:
                    selection = 0;

                    break;
                case ConsoleKey.End:
                    selection = options.Count - 1;

                    break;
                case ConsoleKey.PageUp:
                    selection = Math.Max(0, selection - 5);

                    break;
                case ConsoleKey.PageDown:
                    selection = Math.Min(options.Count - 1, selection + 5);

                    break;
                case ConsoleKey.Enter:
                    var choice = options[selection];
                    var intrinsicError = field.Validate(choice);

                    if (!string.IsNullOrWhiteSpace(intrinsicError))
                    {
                        Console.WriteLine(intrinsicError);

                        continue;
                    }

                    if (validator != null)
                    {
                        var error = validator(field, choice);

                        if (!string.IsNullOrWhiteSpace(error))
                        {
                            Console.WriteLine(error);

                            continue;
                        }
                    }

                    Console.SetCursorPosition(0, footerTop + 1);
                    Console.WriteLine();

                    return choice;
                case ConsoleKey.Escape:
                case ConsoleKey.LeftArrow:
                    throw new TerminalFormDialogCanceledException();
            }

            RenderSelectableOptions(options, selection, listTop);
        }
    }

    private static string BuildPrompt(TerminalFormFieldDefinition field, string? defaultValue)
    {
        var paddedLabel = GetLabel(field) + (field.IsRequired ? "*" : string.Empty);
        var suffix = string.IsNullOrWhiteSpace(defaultValue) ? string.Empty : $" ({defaultValue})";

        return paddedLabel + suffix;
    }

    private static string GetLabel(TerminalFormFieldDefinition field) =>
        string.IsNullOrWhiteSpace(field.Label) ? field.Key.Path : field.Label;

    private static string FormatCurrentValue(string? value) => string.IsNullOrEmpty(value) ? "(empty)" : value;

    private static int ResolveDefaultIndex(IReadOnlyList<string> options, string? defaultValue)
    {
        if (string.IsNullOrWhiteSpace(defaultValue))
            return -1;

        for (var i = 0; i < options.Count; i++)
        {
            if (string.Equals(options[i], defaultValue, StringComparison.OrdinalIgnoreCase))
                return i;
        }

        return -1;
    }

    private static void RenderSelectableOptions(IReadOnlyList<string> options, int selectedIndex, int top)
    {
        var width = GetConsoleWidth();
        var originalForeground = Console.ForegroundColor;
        var originalBackground = Console.BackgroundColor;

        for (var i = 0; i < options.Count; i++)
        {
            Console.SetCursorPosition(0, top + i);
            var prefix = i == selectedIndex ? "> " : "  ";
            var text = prefix + options[i];

            if (text.Length < width)
                text = text.PadRight(width);
            else if (text.Length > width)
                text = text[..width];

            if (i == selectedIndex)
            {
                Console.BackgroundColor = ConsoleColor.DarkBlue;
                Console.ForegroundColor = ConsoleColor.White;
            }
            else
            {
                Console.BackgroundColor = originalBackground;
                Console.ForegroundColor = originalForeground;
            }

            Console.Write(text);
        }

        Console.ForegroundColor = originalForeground;
        Console.BackgroundColor = originalBackground;
        Console.SetCursorPosition(0, top + options.Count);
    }

    private static int GetConsoleWidth() => TerminalDimensions.GetBufferWidthOrDefault(80);

    private static T RunWithExternalScope<T>(Func<T> operation)
    {
        operation.Required();
        var dispatcher = TerminalDispatcher.Current;

        return dispatcher != null ? dispatcher.RunExternal(operation) : operation();
    }

    private sealed class TerminalFormDialogCanceledException : Exception { }
}
