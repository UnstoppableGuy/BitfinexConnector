using BitfinexConnector.Interfaces;
using BitfinexConnector.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace BitfinexConnector.Clients
{
    public class WebSocketClient : IWebSocketClient, IDisposable
    {
        private readonly ILogger<WebSocketClient> _logger;
        private readonly IDataMapper<decimal[], Trade> _tradeMapper;
        private readonly IDataMapper<decimal[], Candle> _candleMapper;
        private readonly object _lockObject = new();
        private readonly SemaphoreSlim _connectionSemaphore = new(1, 1);

        private ClientWebSocket _webSocket;
        private CancellationTokenSource _cancellationTokenSource;
        private record SubscriptionInfo(string Channel, string Symbol, string Key);

        private readonly Dictionary<int, SubscriptionInfo> _subscriptions = new();
        private bool _disposed = false;
        private bool _isConnecting = false;

        public event Action<Trade> NewBuyTrade;
        public event Action<Trade> NewSellTrade;
        public event Action<Candle> CandleSeriesProcessing;

        public WebSocketClient(
            ILogger<WebSocketClient> logger,
            IDataMapper<decimal[], Trade> tradeMapper,
            IDataMapper<decimal[], Candle> candleMapper)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _tradeMapper = tradeMapper ?? throw new ArgumentNullException(nameof(tradeMapper));
            _candleMapper = candleMapper ?? throw new ArgumentNullException(nameof(candleMapper));
        }

        private async Task EnsureConnectedAsync()
        {
            await _connectionSemaphore.WaitAsync();
            try
            {
                if (_webSocket?.State != WebSocketState.Open && !_isConnecting)
                {
                    _isConnecting = true;
                    try
                    {
                        await ConnectAsync();
                    }
                    finally
                    {
                        _isConnecting = false;
                    }
                }
            }
            finally
            {
                _connectionSemaphore.Release();
            }
        }

        private async Task ConnectAsync()
        {
            try
            {
                // Отменяем предыдущие операции
                _cancellationTokenSource?.Cancel();

                // Ждем немного, чтобы предыдущие операции завершились
                await Task.Delay(100);

                // Освобождаем ресурсы
                _webSocket?.Dispose();
                _cancellationTokenSource?.Dispose();

                _webSocket = new ClientWebSocket();
                _cancellationTokenSource = new CancellationTokenSource();

                var uri = new Uri("wss://api-pub.bitfinex.com/ws/2");
                await _webSocket.ConnectAsync(uri, _cancellationTokenSource.Token);

                _logger.LogInformation("WebSocket подключен к Bitfinex");

                // Запускаем прослушивание сообщений
                _ = Task.Run(ListenForMessagesAsync, _cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при подключении WebSocket");
                throw;
            }
        }      
     
        private const int MaxMessageSize = 10 * 1024 * 1024; // 10 МБ
        private const int BufferSize = 4096; // 4 КБ

        private async Task ListenForMessagesAsync()
        {
            var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
            var messageStream = new MemoryStream();

            try
            {
                while (_webSocket?.State == WebSocketState.Open &&
                       !_cancellationTokenSource?.Token.IsCancellationRequested == true)
                {
                    var result = await _webSocket.ReceiveAsync(
                        new ArraySegment<byte>(buffer),
                        _cancellationTokenSource.Token);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        messageStream.Write(buffer, 0, result.Count);

                        if (messageStream.Length > MaxMessageSize)
                        {
                            _logger.LogError("Превышен максимальный размер сообщения");
                            messageStream.SetLength(0);
                            continue;
                        }

                        if (result.EndOfMessage)
                        {
                            messageStream.Seek(0, SeekOrigin.Begin);
                            using var reader = new StreamReader(messageStream, Encoding.UTF8, false, 1024, true);
                            var completeMessage = await reader.ReadToEndAsync();

                            await ProcessMessageAsync(completeMessage);
                            messageStream.SetLength(0);
                        }
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _logger.LogInformation("WebSocket соединение закрыто сервером");
                        break;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Прослушивание WebSocket отменено");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении сообщений WebSocket");
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
                messageStream.Dispose();
            }
        }


        private async Task ProcessTradeDataAsync(JsonElement data, string symbol)
        {
            try
            {
                if (data.GetArrayLength() >= 2)
                {
                    var secondElement = data[1];

                    // Обработка одиночной сделки
                    if (secondElement.ValueKind == JsonValueKind.String)
                    {
                        var messageType = secondElement.GetString();
                        if ((messageType == "te" || messageType == "tu") && data.GetArrayLength() >= 3)
                        {
                            var tradeArray = data[2];
                            if (tradeArray.GetArrayLength() >= 4)
                            {
                                ProcessTrade(tradeArray, symbol);
                            }
                        }
                    }
                    // Обработка снимка сделок
                    else if (secondElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var tradeElement in secondElement.EnumerateArray())
                        {
                            if (tradeElement.GetArrayLength() >= 4)
                            {
                                ProcessTrade(tradeElement, symbol);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке данных сделок для {Symbol}", symbol);
            }
        }

        private void ProcessTrade(JsonElement tradeElement, string symbol)
        {
            var tradeData = new decimal[4];
            for (int i = 0; i < 4 && i < tradeElement.GetArrayLength(); i++)
            {
                tradeData[i] = tradeElement[i].GetDecimal();
            }

            var trade = _tradeMapper.MapFromArray(tradeData, symbol);

            if (trade.Amount > 0)
                NewBuyTrade?.Invoke(trade);
            else
                NewSellTrade?.Invoke(trade);
        }


        private async Task ProcessCandleDataAsync(JsonElement data, string key)
        {
            try
            {
                if (data.GetArrayLength() >= 2)
                {
                    var candleData = data[1];

                    // Проверяем тип данных
                    if (candleData.ValueKind == JsonValueKind.Array)
                    {
                        // Обработка снимка (массив свечей)
                        if (candleData.GetArrayLength() > 0 && candleData[0].ValueKind == JsonValueKind.Array)
                        {
                            foreach (var candleElement in candleData.EnumerateArray())
                            {
                                ProcessCandle(candleElement, key);
                            }
                        }
                        // Обработка одиночной свечи
                        else if (candleData.GetArrayLength() >= 6)
                        {
                            ProcessCandle(candleData, key);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("Неожиданный тип данных для свечей: {Type}", candleData.ValueKind);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке данных свечей для {Key}", key);
            }
        }

        private void ProcessCandle(JsonElement candleElement, string key)
        {
            try
            {
                var candleArray = new decimal[6];
                for (int i = 0; i < 6 && i < candleElement.GetArrayLength(); i++)
                {
                    candleArray[i] = candleElement[i].GetDecimal();
                }

                // Извлекаем символ из ключа
                var parts = key.Split(':');
                var symbol = parts.Length > 2 ? parts[2] : key;

                var candle = _candleMapper.MapFromArray(candleArray, symbol);
                CandleSeriesProcessing?.Invoke(candle);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке одиночной свечи для {Key}", key);
            }
        }



        private async Task ProcessMessageAsync(string message)
        {
            try
            {
                _logger.LogDebug("Получено сообщение: {Message}", message);

                // Проверяем, является ли это событием подписки
                if (message.StartsWith("{"))
                {
                    var eventData = JsonSerializer.Deserialize<JsonElement>(message);

                    if (eventData.TryGetProperty("event", out var eventType) &&
                        eventType.GetString() == "subscribed")
                    {
                        await HandleSubscriptionConfirmationAsync(eventData);
                        return;
                    }
                }

                // Проверяем, является ли это данными канала
                if (message.StartsWith("["))
                {
                    var channelData = JsonSerializer.Deserialize<JsonElement>(message);
                    if (channelData.ValueKind == JsonValueKind.Array && channelData.GetArrayLength() > 0)
                    {
                        var channelId = channelData[0].GetInt32();
                        await ProcessChannelDataAsync(channelId, channelData);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке сообщения: {Message}", message);
            }
        }

        private async Task HandleSubscriptionConfirmationAsync(JsonElement eventData)
        {
            if (eventData.TryGetProperty("chanId", out var chanIdElement) &&
                eventData.TryGetProperty("channel", out var channelElement))
            {
                var channelId = chanIdElement.GetInt32();
                var channel = channelElement.GetString();

                lock (_lockObject)
                {
                    if (eventData.TryGetProperty("symbol", out var symbolElement))
                    {
                        var symbol = symbolElement.GetString();
                        _subscriptions[channelId] = new SubscriptionInfo(channel, symbol, null);
                        _logger.LogInformation("Подписка подтверждена: канал {Channel}, символ {Symbol}, ID {ChannelId}",
                            channel, symbol, channelId);
                    }
                    else if (eventData.TryGetProperty("key", out var keyElement))
                    {
                        var key = keyElement.GetString();
                        _subscriptions[channelId] = new SubscriptionInfo(channel, null, key);
                        _logger.LogInformation("Подписка подтверждена: канал {Channel}, ключ {Key}, ID {ChannelId}",
                            channel, key, channelId);
                    }
                }
            }
        }




        private async Task ProcessChannelDataAsync(int channelId, JsonElement data)
        {
            SubscriptionInfo subscription;

            lock (_lockObject)
            {
                if (!_subscriptions.TryGetValue(channelId, out subscription))
                {
                    _logger.LogWarning("Получены данные для неизвестного канала: {ChannelId}", channelId);
                    return;
                }
            }

            switch (subscription.Channel)
            {
                case "trades":
                    await ProcessTradeDataAsync(data, subscription.Symbol);
                    break;
                case "candles":
                    await ProcessCandleDataAsync(data, subscription.Key);
                    break;
                default:
                    _logger.LogWarning("Неизвестный тип канала: {Channel}", subscription.Channel);
                    break;
            }
        }        

        public async void SubscribeTrades(string pair, int maxCount = 100)
        {
            try
            {
                await EnsureConnectedAsync();

                var subscription = new
                {
                    @event = "subscribe",
                    channel = "trades",
                    symbol = pair
                };

                await SendMessageAsync(JsonSerializer.Serialize(subscription));
                _logger.LogInformation("Отправлена подписка на сделки для {Pair}", pair);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при подписке на сделки для {Pair}", pair);
            }
        }

        public async void UnsubscribeTrades(string pair)
        {
            try
            {
                var channelId = GetChannelId("trades", pair);
                if (channelId.HasValue)
                {
                    var unsubscription = new
                    {
                        @event = "unsubscribe",
                        chanId = channelId.Value
                    };

                    await SendMessageAsync(JsonSerializer.Serialize(unsubscription));

                    lock (_lockObject)
                    {
                        _subscriptions.Remove(channelId.Value);
                    }

                    _logger.LogInformation("Отправлена отписка от сделок для {Pair}", pair);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при отписке от сделок для {Pair}", pair);
            }
        }

        public async void SubscribeCandles(string pair, int periodInSec, DateTimeOffset? from = null, DateTimeOffset? to = null, long? count = 0)
        {
            try
            {
                await EnsureConnectedAsync();

                var timeframe = ConvertPeriodToTimeframe(periodInSec);
                var key = $"trade:{timeframe}:{pair}";

                var subscription = new
                {
                    @event = "subscribe",
                    channel = "candles",
                    key
                };

                await SendMessageAsync(JsonSerializer.Serialize(subscription));
                _logger.LogInformation("Отправлена подписка на свечи для {Pair} с периодом {Period}", pair, timeframe);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при подписке на свечи для {Pair}", pair);
            }
        }

        public async void UnsubscribeCandles(string pair)
        {
            try
            {
                var channelId = GetChannelId("candles", null, pair);
                if (channelId.HasValue)
                {
                    var unsubscription = new
                    {
                        @event = "unsubscribe",
                        chanId = channelId.Value
                    };

                    await SendMessageAsync(JsonSerializer.Serialize(unsubscription));

                    lock (_lockObject)
                    {
                        _subscriptions.Remove(channelId.Value);
                    }

                    _logger.LogInformation("Отправлена отписка от свечей для {Pair}", pair);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при отписке от свечей для {Pair}", pair);
            }
        }

        private async Task SendMessageAsync(string message)
        {
            if (_webSocket?.State == WebSocketState.Open && _cancellationTokenSource?.Token.IsCancellationRequested == false)
            {
                var buffer = Encoding.UTF8.GetBytes(message);
                await _webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, _cancellationTokenSource.Token);
                _logger.LogDebug("Отправлено сообщение: {Message}", message);
            }
            else
            {
                _logger.LogWarning("Попытка отправить сообщение при неактивном WebSocket соединении");
            }
        }

        private int? GetChannelId(string channel, string symbol = null, string keyPart = null)
        {
            lock (_lockObject)
            {
                foreach (var subscription in _subscriptions)
                {
                    if (subscription.Value.Channel == channel)
                    {
                        if (!string.IsNullOrEmpty(symbol) && subscription.Value.Symbol == symbol)
                            return subscription.Key;

                        if (!string.IsNullOrEmpty(keyPart) && subscription.Value.Key?.Contains(keyPart) == true)
                            return subscription.Key;
                    }
                }
            }
            return null;
        }

        private static string ConvertPeriodToTimeframe(int periodInSec)
        {
            return periodInSec switch
            {
                60 => "1m",
                300 => "5m",
                900 => "15m",
                1800 => "30m",
                3600 => "1h",
                10800 => "3h",
                21600 => "6h",
                43200 => "12h",
                86400 => "1D",
                604800 => "7D",
                1209600 => "14D",
                2592000 => "1M",
                _ => $"{periodInSec}s"
            };
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _cancellationTokenSource?.Cancel();
                _webSocket?.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                _webSocket?.Dispose();
                _cancellationTokenSource?.Dispose();
                _connectionSemaphore?.Dispose();
                _disposed = true;
            }
        }       
    }
}