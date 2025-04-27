TerminalKit (русский)
=====================

Общее описание
--------------

- `TerminalKit` — набор компонентов и DSL для построения насыщенных консольных экранов с управлением с клавиатуры.
- Интерфейс описывается снимком (`TerminalSnapshot`): дерево компонентов, срезы состояния и привязки ввода.
- Хост (`TerminalHost`) запускает рендер/ввод, следит за ресайзами, устанавливает `TerminalDispatcher` для инвалидаций.
- Рендерер (`TerminalSnapshotRenderer`) работает напрямую со снимком без промежуточного JSON, буферизуя вывод через
  `TerminalRenderBuffer`.
- Навигация и события описываются через `TerminalKeyBindingMap` и `TerminalEventKey`.
- Дополнительно есть готовые пресеты CRUD, DSL‑композиторы и интерактивный `TerminalObjectExplorer`.

Структура и компоновка
----------------------

- **Components/**: определения компонентов (списки, таблицы, формы, хлебные крошки, панели, ярлыки).
- **Builder/**: низкоуровневый `TerminalUiBuilder<T>`/`TerminalComponentBuilder<T>` и хелперы
  `TerminalComponentBuilderExtensions.BindState`.
- **Dsl/**: удобные композиторы (`TerminalSceneComposer`, `TerminalListComposer`, `TerminalTableComposer`,
  `TerminalFormComposer`, `TerminalCommandBarComposer`).
- **Presets/Controls/**: готовые блоки (прокручиваемый список, CRUD‑таблица, метаданные‑форма, панель команд).
- **Presets/**: пресет CRUD‑экрана, состояния (`TerminalViewportState`, `TerminalSelectionState`), дескрипторы
  кнопок/подсказок.
- **Binding/**: `DataBindingKey` для адресации свойств, `StateHandle<T>` и `TerminalBindingPath` для привязок состояний,
  `TerminalObservableBindingList<T>` для наблюдаемых списков.
- **Model/**: контракт узлов/снимков (`TerminalNode`, `TerminalStateSlice`, `TerminalSnapshot`), утилиты доступа (
  `TerminalNodeExtensions`).

Рендеринг, ввод и размеры
-------------------------

- `TerminalHost` управляет циклом: проверяет `_pendingRender`, обрабатывает ввод `Console.KeyAvailable`, синхронизирует
  буфер/окно (через `TerminalDimensions`).
- `TerminalResizeWatcher` опрашивает размеры, генерирует `Resized` — хост заново рисует кадр.
- `TerminalRuntime` индексирует узлы/состояния для быстрого доступа в рендере.
- `TerminalDimensions` даёт безопасные `GetWindowWidth/HeightOrDefault` и `GetBufferWidthOrDefault`, чтобы везде
  использовать единые fallback‑значения.
- `TerminalKeyBindingMap` описывает привязки клавиш, `TerminalEventKey` — ключи событий в компонентах.

Навигация и прокрутка
---------------------

- `TerminalScrollableWindowController` инкапсулирует выбор + окно просмотра, умеет PageUp/PageDown/Home/End и
  инвалидацию через диспетчер.
- Коллекции (`TerminalCollectionComponent`) поддерживают подсказки и статус‑сообщения (`NavigationHint`,
  `StatusMessage`, `ShowNavigationSummary`).

Object Explorer
---------------

- `TerminalObjectExplorer` — интерактивный инспектор/редактор объектов:
    - `TerminalExplorerSession` управляет фреймами, выделением, лэйзи‑загрузкой свойств/элементов, инвалидациями.
    - `TerminalExplorerFrame` формирует записи (`TerminalExplorerEntry`) для свойств/методов/коллекций; состояния
      загрузок через `TerminalPropertyValueState`.
    - Предпросмотры загружаются в фоне по видимой области; индикаторы: ⌛ (загрузка), ⟳ (обновление).
    - `FileTerminalObjectExplorer` открывает JSON‑файлы в экране исследований.

Валидация и формы
-----------------

- **Validation/**: атрибуты/валидаторы диапазонов, регулярных выражений, наборов символов, делегат‑валидатор.
- `TerminalFormDialog` строит интерактивные диалоги на основе `TerminalFormFieldDefinition`, использует
  `TerminalLineEditor` и валидаторы.

Расширение и лучшие практики
----------------------------

- Новые компоненты оформляйте через `TerminalComponentAttribute`; свойства должны быть сериализуемыми и понятными
  рендереру.
- Состояния передавайте через `StateHandle<T>` и `TerminalBindingPath.State`, избегайте глобальных синглтонов.
- Дублирующуюся инфраструктуру (размеры консоли, чтение свойств узлов, биндинги) берите из `TerminalDimensions`,
  `TerminalNodeExtensions`, `TerminalComponentBuilderExtensions`.
- Долгие операции выносите из рендера; при необходимости «покинуть» альтернативный буфер используйте
  `TerminalDispatcher.RunExternal`.

Карта директорий
----------------

- Attributes — атрибуты компонентов/валидации.
- Binding — ключи привязок, наблюдаемые списки.
- Builder — низкоуровневый билдер снимков, общие расширения.
- Components — определения UI‑компонентов.
- Dsl — композиторы для типовых блоков.
- Events — ключи событий.
- Interaction — редактор строк ввода.
- Model — узлы/состояния снимков, утилиты доступа.
- ObjectExplorer — интерактивный исследователь объектов.
- Presets — CRUD‑пресеты и контролы.
- Reflection — фабрика полей форм.
- Rendering — диспетчер, рендерер, буфер, размеры, отслеживание ресайза, хост.
- Runtime — индексатор снимка и контроллер прокрутки.
- Validation — валидаторы и атрибуты.

Заметки по использованию
------------------------

- Хост предполагает эксклюзивный контроль буфера консоли; вывод/интеракции вне UI делайте через `RunExternal`.
- Ресайзы обрабатываются опросом; если внешний код активно меняет размеры, при необходимости добавьте debounce на
  стороне вызывающего кода.
- Сообщения пользователю держите короткими (статус‑строки); детальные ошибки выводите через внешний контекст (
  `RunExternal`).

Быстрый старт: построить экран и запустить
------------------------------------------

```csharp
// Предполагается TerminalScreen с атрибутом [TerminalComponent]
var builder = TerminalUiBuilder<TerminalScreen>.Create();

var snapshot = builder
    .Configure(root =>
    {
        var items = builder.WithState("items", new[] { "Alpha", "Beta", "Gamma" });
        var viewport = builder.WithState("viewport", new TerminalViewportState { Offset = 0, WindowSize = 5 });

        root.AddChild<TerminalLabel>(l => l.Set(x => x.Text, "Пример списка"));
        root.AddChild<TerminalListView>(list =>
        {
            list.BindState(x => x.DataSource, items)
                .BindState(x => x.ViewportState, viewport)
                .Set(x => x.ItemTemplate, "{value}")
                .Set(x => x.EmptyStateText, "(пусто)");
        });
    })
    .BuildSnapshot();

using var host = new TerminalHost(
    snapshotFactory: () => snapshot,
    bindingsFactory: () => new TerminalKeyBindingMap());
host.Run();
```

DSL и пресеты (CRUD-поток)
-------------------------

```csharp
var scene = TerminalScene<TerminalScreen>.Create()
    .Compose(ui =>
    {
        var items = ui.WithState("items", new[] { new { Name = "Alice", Age = 30 } });
        var viewport = ui.WithState("viewport", new TerminalViewportState { Offset = 0, WindowSize = 10 });
        var selection = ui.WithState("selection", 0);

        ui.Breadcrumb(() => "Users");
        ui.List(items, viewport, list => list.ItemTemplate("{Name}").ShowNavigationSummary());
        ui.Table(items, viewport, selection, table =>
        {
            table.Column(DataBindingKey.For((dynamic u) => u.Name), "Name", 18);
            table.Column(DataBindingKey.For((dynamic u) => u.Age), "Age", 6);
            table.OnCellEdit(new TerminalHandlerId("UpdateUser"));
        });
        ui.Form(selection, form =>
        {
            form.Field(DataBindingKey.For((dynamic u) => u.Name), TerminalFormFieldEditor.Text);
            form.Field(DataBindingKey.For((dynamic u) => u.Age), TerminalFormFieldEditor.Number);
            form.OnSubmit(new TerminalHandlerId("SaveUser"));
            form.OnCancel(new TerminalHandlerId("CancelEdit"));
        });
    })
    .Build();
```

Объектный исследователь: пример
-------------------------------

```csharp
TerminalObjectExplorer.ShowObject(new
{
    Name = "Service",
    Port = 8080,
    Settings = new { Enabled = true, Tags = new[] { "prod", "api" } }
});
```

- Стрелки двигают выделение, Enter — открыть свойство/коллекцию, Esc/Left — назад.
- Ленивая загрузка значений, индикаторы: ⌛ (загрузка), ⟳ (обновление).
- Кнопки добавляются через `TerminalObjectExplorer.Buttons` (верните `TerminalExplorerButtonResult.Refresh`, чтобы
  перерисовать).

Хостинг и инвалидации
---------------------

- `TerminalHost` ставит `TerminalDispatcher` для вызовов `Invalidate()` из пользовательского кода при изменении
  состояния.
- Долгие операции и вывод вне альтернативного буфера выполняйте через `dispatcher.RunExternal(...)`.
- Размеры консоли берите через `TerminalDimensions`, чтобы избежать расхождений между ресайз‑вотчером, рендером и
  хостом.

Частые паттерны
---------------

- Держите снимки неизменяемыми: вычисляйте состояние снаружи, подавайте через `WithState`.
- Стабилизируйте названия биндингов (ASCII), так как `TerminalRuntime` ищет состояния по строковым ключам.
- При добавлении нового компонента расширьте `TerminalSnapshotRenderer`, чтобы он знал, как рисовать новый тип.
