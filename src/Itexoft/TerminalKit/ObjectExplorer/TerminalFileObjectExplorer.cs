// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at https://mozilla.org/MPL/2.0/.
// This Source Code Form is "Incompatible With Secondary Licenses", as defined by the Mozilla Public License, v. 2.0.

using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Itexoft.TerminalKit.Rendering;

namespace Itexoft.TerminalKit.ObjectExplorer;

/// <summary>
/// Explorer variant that loads the root object from a JSON file and persists changes back to disk.
/// </summary>
public sealed class TerminalFileObjectExplorer<T> : TerminalObjectExplorer where T : class
{
    private readonly JsonSerializerOptions serializerOptions;

    /// <summary>
    /// Initializes the explorer by loading JSON from the specified file.
    /// </summary>
    /// <param name="filePath">Relative or absolute path to the JSON payload.</param>
    public TerminalFileObjectExplorer(string filePath) : this(filePath, ResolvePath(filePath)) { }

    private TerminalFileObjectExplorer(string originalPath, string resolvedPath) : base(null, BuildTitle(resolvedPath))
    {
        this.FilePath = resolvedPath;
        this.serializerOptions = this.CreateSerializerOptions();
        base.Value = this.LoadValue(resolvedPath);
        this.Buttons.Add("Save", ConsoleKey.S, _ => this.Save());
        this.Buttons.AddQuit();
    }

    /// <summary>
    /// Gets the root object being explored.
    /// </summary>
    public new T Value => (T)((TerminalObjectExplorer)this).Value!;

    /// <summary>
    /// Gets file path.
    /// </summary>
    public string FilePath { get; }

    /// <summary>
    /// Saves the root object.
    /// </summary>
    public void Save(Action<Exception>? onException = null)
    {
        if (this.Value is not T typed)
            return;

        try
        {
            var directory = Path.GetDirectoryName(this.FilePath);

            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            using var stream = File.Create(this.FilePath);
            JsonSerializer.Serialize(stream, typed, this.serializerOptions);
            stream.Flush();
        }
        catch (Exception ex)
        {
            var dispatcher = TerminalDispatcher.Current;
            dispatcher?.RunExternal(() => onException?.Invoke(ex));
        }
    }

    private T? LoadValue(string fullPath)
    {
        if (!File.Exists(fullPath))
            return this.CreateDefaultInstance();

        try
        {
            using var stream = File.OpenRead(fullPath);
            var value = JsonSerializer.Deserialize<T>(stream, this.serializerOptions);

            return value ?? this.CreateDefaultInstance();
        }
        catch
        {
            return this.CreateDefaultInstance();
        }
    }

    private T? CreateDefaultInstance() => this.TryCreateInstance(typeof(T)) as T;

    private object? TryCreateInstance(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        var ctor = FindExplorerConstructor(type);

        if (ctor != null)
        {
            try
            {
                return ctor.Invoke([this]);
            }
            catch { }
        }

        if (type.GetConstructor(Type.EmptyTypes) == null)
            return null;

        try
        {
            return Activator.CreateInstance(type);
        }
        catch
        {
            return null;
        }
    }

    private JsonSerializerOptions CreateSerializerOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();

        resolver.Modifiers.Add(info =>
        {
            if (info.Kind != JsonTypeInfoKind.Object)
                return;

            var ctor = FindExplorerConstructor(info.Type);

            if (ctor == null)
                return;

            var fallback = info.CreateObject;

            info.CreateObject = () =>
            {
                var instance = this.TryInvokeExplorerConstructor(ctor);

                if (instance != null)
                    return instance;

                if (fallback != null)
                    return fallback();

                return Activator.CreateInstance(info.Type)!;
            };
        });

        return new()
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            TypeInfoResolver = resolver,
        };
    }

    private object? TryInvokeExplorerConstructor(ConstructorInfo ctor)
    {
        try
        {
            return ctor.Invoke([this]);
        }
        catch
        {
            return null;
        }
    }

    private static ConstructorInfo? FindExplorerConstructor(Type type) => type
        .GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(ctor =>
        {
            var parameters = ctor.GetParameters();

            return parameters.Length == 1 && typeof(TerminalObjectExplorer).IsAssignableFrom(parameters[0].ParameterType);
        });

    private static string BuildTitle(string path)
    {
        var fileName = Path.GetFileName(path);

        return string.IsNullOrWhiteSpace(fileName) ? typeof(T).Name : fileName!;
    }

    private static string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
            return path;

        var entry = Assembly.GetEntryAssembly()?.Location;
        var baseDirectory = !string.IsNullOrWhiteSpace(entry) ? Path.GetDirectoryName(entry) : AppContext.BaseDirectory;
        baseDirectory ??= AppContext.BaseDirectory;

        return Path.GetFullPath(Path.Combine(baseDirectory, path));
    }
}
