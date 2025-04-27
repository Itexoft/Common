# Надёжные TCP-подключения

В этой папке находится упрощённый API клиент/сервер, построенный поверх P2P-рантайма. Он пригодится, когда нужна
автоматическая реконнекция с make-before-break, heartbeat, retry-budget и sticky blacklist, но не требуется весь
peer-to-peer функционал.

## Компоненты

| Тип                                                     | Описание                                                          |
|---------------------------------------------------------|-------------------------------------------------------------------|
| `ReliableTcpClient`                                     | Поддерживает одно устойчивое соединение с набором конечных точек. |
| `ReliableTcpServer`                                     | Принимает входящие подключения и уведомляет обработчики.          |
| `ReliableTcpClientOptions` / `ReliableTcpServerOptions` | Настройки heartbeat, повторов, метаданных и т.д.                  |
| `ReliableTcpEndpoint`                                   | Описание TCP-узла (host, port, приоритет, TLS).                   |
| `ReliableConnectionMetrics`                             | Метрики клиента (SRTT, φ, счётчики повторов).                     |
| `ReliableConnectionState`                               | Состояния жизненного цикла соединения.                            |
| `ReliableStreamWriter` / `ReliableStreamReader`         | Потоковые обёртки, шлют ACK и выдерживают порядок доставки.       |
| `ReliableStreamOptions`                                 | Настройки буфера и гарантий доставки.                             |
| `P2PReliableStreamTransport`                            | Мост, превращающий P2P-соединение в `IReliableStreamTransport`.   |

## Надёжные потоки

```csharp
IReliableStreamTransport clientTransport = /* источник транспорта */;
IReliableStreamTransport serverTransport = /* источник транспорта */;
var writer = new ReliableStreamWriter(clientTransport);
var reader = new ReliableStreamReader(serverTransport);

await writer.WriteAsync("important"u8.ToArray()); // блокируется, пока читатель не подтвердит

var buffer = new byte[16];
var read = await reader.ReadAsync(buffer);
Console.WriteLine(Encoding.UTF8.GetString(buffer, 0, read));
```

По умолчанию `ReliableStreamWriter` держит ограниченный буфер (`BufferCapacityBytes`) и автоматически ждёт ACK
(`EnsureDeliveredBeforeRelease = true`). Если отключить автомат, запустите фон, который вызывает
`ProcessNextAcknowledgementAsync()`, чтобы освобождать буфер. Для наблюдения за состоянием используйте
`GetMetrics()` — там текущий размер очереди и задержки ACK.

## Пример

```csharp
var serverOptions = new ReliableTcpServerOptions { Port = 4500, ServerId = "server" };
await using var server = new ReliableTcpServer(serverOptions);
server.ConnectionAccepted += async ctx =>
{
    using var reader = new StreamReader(ctx.DataStream, Encoding.UTF8, leaveOpen: true);
    await using var writer = new StreamWriter(ctx.DataStream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
    var line = await reader.ReadLineAsync();
    await writer.WriteLineAsync($"echo: {line}");
};
await server.StartAsync();

var clientOptions = new ReliableTcpClientOptions("client-1", new[] { new ReliableTcpEndpoint("localhost", 4500) });
await using var client = new ReliableTcpClient(clientOptions);
var handle = await client.ConnectAsync();
await using var writer = new StreamWriter(handle.DataStream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
using var reader = new StreamReader(handle.DataStream, Encoding.UTF8, leaveOpen: true);
await writer.WriteLineAsync("hello");
Console.WriteLine(await reader.ReadLineAsync()); // echo: hello

await client.DisconnectAsync();
await server.StopAsync();
```

Клиент наследует все механизмы устойчивости от P2P-менеджера: hedged dialing, sticky blacklist, retry-budget, φ-
детектор heartbeat и make-before-break реконнекты.
