// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Reflection;
using Itexoft.TerminalKit;
using Itexoft.TerminalKit.ObjectExplorer;

namespace TerminalKit.Demo;

internal static class SnapshotDebugger
{
    public static void DumpExplorerSnapshot(object root)
    {
        var session = CreateExplorerSession(root);
        DumpSnapshot(session, "Initial state");

        var steps = GetSteps();
        for (var i = 0; i < steps; i++)
        {
            Invoke(session, "OpenSelectedEntry");
            DumpSnapshot(session, $"After step {i + 1}");
        }
    }

    private static object CreateExplorerSession(object root)
    {
        var assembly = typeof(TerminalObjectExplorer).Assembly;
        var type = assembly.GetType("Itexoft.TerminalKit.ObjectExplorer.TerminalExplorerSession", true)!;

        return Activator.CreateInstance(type, root, "Debug Session")!;
    }

    private static void DumpSnapshot(object session, string label)
    {
        Console.WriteLine($"=== {label} ===");
        var snapshot = BuildSnapshot(session);
        DumpNode(snapshot.Root, 0);
    }

    private static TerminalSnapshot BuildSnapshot(object session)
    {
        var method = session.GetType().GetMethod("BuildSnapshot", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (method == null)
            throw new InvalidOperationException("BuildSnapshot method not found.");

        return (TerminalSnapshot)method.Invoke(session, [])!;
    }

    private static void DumpNode(TerminalNode node, int indent)
    {
        var prefix = new string(' ', indent);
        Console.WriteLine($"{prefix}- {node.Component} ({node.ComponentType})");

        foreach (var property in node.Properties)
        {
            var value = FormatValue(property.Value);
            Console.WriteLine($"{prefix}    {property.Key}: {value}");
        }

        foreach (var child in node.Children)
            DumpNode(child, indent + 2);
    }

    private static void Invoke(object target, string methodName)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        method?.Invoke(target, []);
    }

    private static int GetSteps()
    {
        var value = Environment.GetEnvironmentVariable("CONSOLE_UI_DUMP_STEPS");

        return int.TryParse(value, out var steps) && steps > 0 ? steps : 0;
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "null",
            string s => $"\"{s}\"",
            _ => value.ToString() ?? string.Empty
        };
    }
}