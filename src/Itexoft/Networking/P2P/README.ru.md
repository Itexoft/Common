# Модуль P2P-транспорта и менеджер соединений

Каталог содержит готовый peer-to-peer стек. Он позволяет приложениям быстро установить и поддерживать TCP-соединения
между процессами, автоматически обнаруживать обрывы и восстанавливаться без написания собственного «контроля живости».

## Что решает компонент

- Несколько процессов должны обмениваться командами или потоками данных в режиме «все со всеми».
- Нужно реагировать на отвалившийся сокет за миллисекунды, а не ждать системный keep-alive.
- Требуется make-before-break: сначала поднять новое соединение, затем безопасно отключить старое.
- Важно делиться «штрафами» на плохие эндпоинты между участниками, чтобы не устраивать штурм.

Всё перечисленное реализовано средствами данного набора классов.

## Коротко об устройстве

| Слой                | Задача                                                       | Ключевые типы                                                            |
|---------------------|--------------------------------------------------------------|--------------------------------------------------------------------------|
| Адаптеры транспорта | Открывают каналы данных и управления                         | `IP2PTransportAdapter`, `TcpP2PTransportAdapter`, `P2PTransportEndpoint` |
| Менеджер соединения | Управляет dial, handshake, реконнектами                      | `P2PConnectionManager`, `P2PConnectionHandle`, `P2PConnectionOptions`    |
| Liveness & метрики  | Heartbeat, φ-детектор, retry budget                          | `P2PControlFrame`, `FailureSeverity`, `RetryBudget`                      |
| Общие штрафы        | Sticky blacklist и Retry-After для всех локальных менеджеров | `SharedBlacklistRegistry`                                                |
| Сторона приёма      | Привязка входящих сокетов, ServerHello                       | `P2PConnectionHost`, `P2PTransportConnection`                            |

## Принцип работы (простыми словами)

1. Менеджер строит очередь эндпоинтов и запускает параллельные dial-попытки с ограниченной конкуренцией (
   Happy-Eyeballs).
2. После подключения обмен `P2PHandshake` фиксирует ID узлов, epoch сессии и (по желанию) resume-токен.
3. Фоновый heartbeat шлёт ping/pong либо встраивает подтверждения в рабочий трафик; φ-детектор плюс write-probe решают,
   жив ли канал.
4. При реконнекте новый транспорт поднимается раньше старого (`make-before-break`), поэтому данные не теряются.
5. Любой серьёзный сбой или Retry-After заносится в `SharedBlacklistRegistry`; остальные менеджеры временно обходят
   проблемный endpoint.

## Где это полезно

- Связь между сервисами одного приложения (воркеры, надсмотрщики, плагины).
- Автоматизация, которая должна управлять внешними процессами и быстро узнавать об их слете.
- Локальные стенды с хаос-тестами (падения, задержки, частые реконнекты).

## Быстрый старт

```csharp
using var listener = await P2PConnectionHost.StartAsync(new P2PConnectionHostOptions
{
    NodeId = "server-1",
    TransportListenerOptions = new TcpP2PTransportListenerOptions
    {
        Port = 4500
    }
});

var options = new P2PConnectionOptions(
    nodeId: "client-1",
    endpoints: new[]
    {
        new P2PTransportEndpoint("127.0.0.1", 4500, transport: P2PTransportNames.Tcp)
    },
    heartbeatInterval: TimeSpan.FromMilliseconds(250),
    heartbeatTimeout: TimeSpan.FromSeconds(2))
{
    EndpointBlacklistDuration = TimeSpan.FromSeconds(10),
    MaxConcurrentDials = 2
};

await using var manager = new P2PConnectionManager(options, new TcpP2PTransportAdapter());
manager.ConnectionStateChanged += (_, args) =>
    Console.WriteLine($"{args.PreviousState} -> {args.CurrentState} ({args.Cause})");

var connection = await manager.ConnectAsync();
await connection.DataStream.WriteAsync(Encoding.UTF8.GetBytes("привет, пир!\n"));

var metrics = manager.GetMetrics();
Console.WriteLine($"Последний RTT (мс): {metrics.SrttMilliseconds:F2}");
await manager.DisconnectAsync();
```

### Sticky blacklist в действии

```csharp
SharedBlacklistRegistry.SetPenalty(
    key: "tcp:127.0.0.1:4500",
    severity: FailureSeverity.Hard,
    duration: TimeSpan.FromSeconds(15));
```

Менеджеры внутри процесса автоматически подписываются на такие обновления. Штрафы истекают по TTL, а очистка выполняется
с дросселированием, поэтому механизм не мешает частым dial-попыткам.

## Функции менеджера соединений

- **Happy-Eyeballs + приоритеты.** Параллельные dial-попытки с ограничением конкуренции и задержками между группами.
- **Make-before-break.** `ReconnectAsync` сначала поднимает новый канал, затем закрывает текущий.
- **Heartbeat и φ.** Адаптивный интервал, учёт джиттера, write-probe перед объявлением failure.
- **Retry budget и sticky blacklist.** Общие квоты на dial и уважение Retry-After.
- **Метрики.** `P2PConnectionMetrics` возвращает SRTT, φ, uptime/downtime, долю успешных реконнектов, P50/P95 heartbeat
  и др.

## Тесты и хаос

- Реальные loopback-сценарии строятся на `TcpP2PTransportAdapter`.
- `ChaosStream` и `ChaosTcpTransportAdapter` позволяют добавлять задержки, дропы, half-open.
- Набор `SharedBlacklist_*` проверяет истечение sticky-блокировок и корректную отписку.

Все примеры см. в `src/Itexoft.Common.Tests/P2P`.

## Рекомендации по использованию

1. **Определяйте эндпоинты явно.** Минимум один `P2PTransportEndpoint` с нужным `transport`.
2. **Используйте `await using`.** Это гарантирует останов heartbeat и освобождение сокетов.
3. **Собирайте метрики.** `GetMetrics()` пригодится для мониторинга и алертинга.
4. **Сбрасывайте `SharedBlacklistRegistry` в тестах.** Это исключает перекрёстные штрафы.
5. **При добавлении своих адаптеров** переиспользуйте `RetryBudget` и `EndpointDialTracker`, чтобы соблюдать общие
   квоты.
