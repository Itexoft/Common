TerminalKit (English)
=====================

Overview
--------

- `TerminalKit` is a composable toolkit for building rich, keyboard‑driven console screens.
- Screens are assembled into immutable snapshots: a tree of components (`TerminalNode`) plus state slices and input
  bindings.
- A lightweight runtime/renderer (`TerminalHost` + `TerminalSnapshotRenderer`) drives layout, input dispatch, and resize
  handling.
- Presets and DSL helpers provide batteries‑included CRUD flows; `ObjectExplorer` offers reflective browsing/editing.

Layout and Composition
----------------------

- Components live in **Components/** (lists, tables, forms, breadcrumbs, labels, panels, shortcut hints).
- Snapshots are composed with:
    - Low‑level `TerminalUiBuilder<TScreen>` / `TerminalComponentBuilder<T>` for property assignment and event binding.
    - High‑level DSL in **Dsl/** (`TerminalSceneComposer`, `TerminalListComposer`, `TerminalTableComposer`,
      `TerminalFormComposer`, `TerminalCommandBarComposer`).
    - Presets in **Presets/Controls/** for reusable building blocks (scrollable list, CRUD table, metadata form, command
      panel).
- Binding helpers:
    - `StateHandle<T>` + `TerminalBindingPath.State(...)` for state references.
    - `DataBindingKey` to address fields inside bound models.
    - `TerminalComponentBuilderExtensions.BindState` to avoid repetitive binding boilerplate.

Runtime and Rendering
---------------------

- `TerminalHost` owns the render/input loop, installs `TerminalDispatcher`, and monitors resizes via
  `TerminalResizeWatcher`.
- `TerminalSnapshotRenderer` renders directly from `TerminalSnapshot` (no intermediate JSON), using
  `TerminalRenderBuffer` to batch output.
- `TerminalRuntime` indexes nodes/state for fast lookup; `TerminalNodeExtensions` simplifies typed property/binding
  access in renderers.
- `TerminalDimensions` centralizes safe console size reads (window/buffer) with fallbacks to keep helpers consistent.

Input and Navigation
--------------------

- `TerminalKeyBindingMap` and `TerminalEventKey` wire keyboard gestures to handlers declared on components.
- Scrollable navigation is encapsulated in `TerminalScrollableWindowController` (selection + viewport maintenance), used
  by ObjectExplorer and CRUD presets.
- Standard navigation hints and status lines are exposed via `TerminalCollectionComponent` properties (`NavigationHint`,
  `StatusMessage`, `ShowNavigationSummary`).

Object Explorer
---------------

- `TerminalObjectExplorer` hosts a reflective inspector/editor:
    - `TerminalExplorerFrame` snapshots properties/methods/collections with lazy loading (`TerminalExplorerSession`
      orchestrates selection, pending loads, invalidation).
    - `TerminalExplorerEntry` records hold display text, preview, type, and load status (symbols: ⌛ loading, ⟳
      refreshing).
    - Background loads respect viewport to avoid flooding; invalidations run through the dispatcher to repaint.
- `TerminalFileObjectExplorer` convenience helper loads JSON config into an explorer session.

Validation and Forms
--------------------

- **Validation/** contains reusable validators/attributes (number/date ranges, regex, character sets, delegate
  validator).
- `TerminalFormDialog` renders interactive prompts driven by `TerminalFormFieldDefinition` metadata; integrates with
  validators and `TerminalLineEditor`.

Extensibility Guidelines
------------------------

- Add new components by decorating classes with `TerminalComponentAttribute` and exposing properties the renderer
  understands.
- Prefer snapshot/state flows over mutable globals; keep state slices serializable.
- Reuse shared helpers (`TerminalDimensions`, `TerminalNodeExtensions`, `TerminalComponentBuilderExtensions`) to avoid
  duplicate safety logic.
- Keep long‑running or blocking work outside the render loop; use `TerminalDispatcher.RunExternal` when you must
  interact with the host console.

Directory Map
-------------

- Attributes: component metadata annotations.
- Binding: binding keys, observable lists.
- Builder: low‑level snapshot builder, binding helpers.
- Components: component definitions.
- Dsl: fluent composers for common layouts.
- Events: strongly typed event keys.
- Interaction: line editor utilities.
- Model: snapshot/node/state contracts.
- ObjectExplorer: reflective explorer UI.
- Presets: CRUD scaffolding and controls.
- Reflection: form field factory helpers.
- Rendering: dispatcher, renderer, dimensions, buffers, resize watcher, host.
- Runtime: state/node runtime and navigation controller.
- Validation: form validators and attributes.

Testing and Usage Notes
-----------------------

- Render loop assumes exclusive control of the console buffer; wrap integrations with `TerminalHost` to preserve/restore
  state.
- Resize handling is cooperative: `TerminalResizeWatcher` triggers re-render; heavy work should debounce externally if
  needed.
- Keep exceptions user-friendly: status lines show short messages; detailed errors can be shown via `RunExternal`.

Quick start: build and run a screen
-----------------------------------

```csharp
// Define a screen component (must be decorated with [TerminalComponent] elsewhere)
var builder = TerminalUiBuilder<TerminalScreen>.Create();

var scene = builder
    .Configure(root =>
    {
        var items = builder.WithState("items", new[] { "Alpha", "Beta", "Gamma" });
        var viewport = builder.WithState("viewport", new TerminalViewportState { Offset = 0, WindowSize = 5 });

        root.AddChild<TerminalLabel>(l => l.Set(x => x.Text, "Sample list"));
        root.AddChild<TerminalListView>(list =>
        {
            list.BindState(x => x.DataSource, items)
                .BindState(x => x.ViewportState, viewport)
                .Set(x => x.ItemTemplate, "{value}")
                .Set(x => x.EmptyStateText, "(empty)");
        });
    })
    .BuildSnapshot();

using var host = new TerminalHost(
    snapshotFactory: () => scene,
    bindingsFactory: () => new TerminalKeyBindingMap());
host.Run();
```

Using the DSL + presets (CRUD-ish)
----------------------------------

```csharp
// Build a CRUD layout with list + table + form using TerminalSceneComposer and presets
var ui = TerminalScene<TerminalScreen>.Create()
    .Compose(scene =>
    {
        var items = scene.WithState("items", new[] { new { Name = "Alice", Age = 30 } });
        var viewport = scene.WithState("viewport", new TerminalViewportState { Offset = 0, WindowSize = 10 });
        var selection = scene.WithState("selection", 0);

        scene.Breadcrumb(() => "Users");
        scene.List(items, viewport, list => list.ItemTemplate("{Name}").ShowNavigationSummary());
        scene.Table(items, viewport, selection, table =>
        {
            table.Column(DataBindingKey.For((dynamic u) => u.Name), "Name", 18);
            table.Column(DataBindingKey.For((dynamic u) => u.Age), "Age", 6);
            table.OnCellEdit(new TerminalHandlerId("UpdateUser"));
        });
        scene.Form(selection, form =>
        {
            form.Field(DataBindingKey.For((dynamic u) => u.Name), TerminalFormFieldEditor.Text);
            form.Field(DataBindingKey.For((dynamic u) => u.Age), TerminalFormFieldEditor.Number);
            form.OnSubmit(new TerminalHandlerId("SaveUser"));
            form.OnCancel(new TerminalHandlerId("CancelEdit"));
        });
    })
    .Build();
```

Hosting and invalidation basics
-------------------------------

- Wrap your snapshot/binding factories in `TerminalHost`. The host installs a `TerminalDispatcher` so UI code can call
  `TerminalDispatcher.Current?.Invalidate()` when state changes.
- Long/interactive operations should run through `dispatcher.RunExternal(...)` to temporarily leave the alternate
  buffer.
- `TerminalDimensions` keeps buffer/window sizing consistent across resize watcher, host sync, and render helpers.

Object Explorer usage
---------------------

```csharp
// Quick inspection of any object graph
TerminalObjectExplorer.ShowObject(new
{
    Name = "Service",
    Port = 8080,
    Settings = new { Enabled = true, Tags = new[] { "prod", "api" } }
});
```

- Arrow keys move selection; Enter opens properties/collections; Esc/Left goes back.
- Values load lazily; loading indicator is ⌛, refreshing uses ⟳ suffix.
- Provide `TerminalExplorerButton` instances via `TerminalObjectExplorer.Buttons` to add custom actions (return
  `TerminalExplorerButtonResult.Refresh` to repaint).

Common patterns and pitfalls
----------------------------

- Prefer immutable snapshots: compute state externally, then expose via `WithState`. Avoid mutating shared globals
  inside renderers.
- Debounce expensive work triggered by `TerminalDispatcher.Invalidate` if your snapshot factories are heavy.
- Keep binding names stable and ASCII; `TerminalRuntime` uses string keys to resolve state and nodes.
- When adding new components, ensure renderer support (extend `TerminalSnapshotRenderer` if needed).
