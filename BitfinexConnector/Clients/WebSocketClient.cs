using BitfinexConnector.Interfaces;
using BitfinexConnector.Models;
using Microsoft.Extensions.Logging;
using System;
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

        private ClientWebSocket _webSocket;
        private CancellationTokenSource _cancellationTokenSource;
        private readonly Dictionary<int, SubscriptionInfo> _subscriptions = new();
        private bool _disposed = false;
        private readonly object _lockObject = new object();
        private record SubscriptionInfo(string Channel, string Symbol, string Key);

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
            if (_disposed) return;

            lock (_lockObject)
            {
                if (_disposed) return;
                if (_webSocket?.State == WebSocketState.Open) return;
            }

            await ConnectAsync();
        }

        private async Task ConnectAsync()
        {
            if (_disposed) return;

            try
            {
                lock (_lockObject)
                {
                    if (_disposed) return;

                    _webSocket?.Dispose();
                    _cancellationTokenSource?.Dispose();

                    _webSocket = new ClientWebSocket();
                    _cancellationTokenSource = new CancellationTokenSource();
                }

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

        private async Task ListenForMessagesAsync()
        {
            var buffer = new byte[8192];

            try
            {
                while (!_disposed && !_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    ClientWebSocket webSocket;
                    lock (_lockObject)
                    {
                        if (_disposed || _webSocket == null) break;
                        webSocket = _webSocket;
                        if (webSocket.State != WebSocketState.Open) break;
                    }

                    var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), _cancellationTokenSource.Token);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var message = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        await ProcessMessageAsync(message);
                    }
                }
            }
            catch (ObjectDisposedException)
            {
                
            }
            catch (Exception ex) when (!_cancellationTokenSource.Token.IsCancellationRequested && !_disposed)
            {
                _logger.LogError(ex, "Ошибка при получении сообщений WebSocket");
            }
        }

        private async Task SendMessageAsync(string message)
        {
            if (_disposed) return;

            ClientWebSocket webSocket;
            CancellationToken token;

            lock (_lockObject)
            {
                if (_disposed || _webSocket?.State != WebSocketState.Open) return;
                webSocket = _webSocket;
                token = _cancellationTokenSource.Token;
            }

            try
            {
                var buffer = Encoding.UTF8.GetBytes(message);
                await webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, token);
                _logger.LogDebug("Отправлено сообщение: {Message}", message);
            }
            catch (ObjectDisposedException)
            {
                
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при отправке сообщения");
            }
        }

        public async void SubscribeTrades(string pair, int maxCount = 100)
        {
            if (_disposed) return;

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

        public async void UnsubscribeTrades(string pair)
        {
            if (_disposed) return;

            var channelId = GetChannelId("trades", pair);
            if (channelId.HasValue)
            {
                var unsubscription = new
                {
                    @event = "unsubscribe",
                    chanId = channelId.Value
                };

                await SendMessageAsync(JsonSerializer.Serialize(unsubscription));
                _subscriptions.Remove(channelId.Value);
                _logger.LogInformation("Отправлена отписка от сделок для {Pair}", pair);
            }
        }

        public async void SubscribeCandles(string pair, int periodInSec, DateTimeOffset? from = null, DateTimeOffset? to = null, long? count = 0)
        {
            if (_disposed) return;

            await EnsureConnectedAsync();

            var timeframe = ConvertPeriodToTimeframe(periodInSec);
            var key = $"trade:{timeframe}:{pair}";

            var subscription = new
            {
                @event = "subscribe",
                channel = "candles",
                key = key
            };

            await SendMessageAsync(JsonSerializer.Serialize(subscription));
            _logger.LogInformation("Отправлена подписка на свечи для {Pair} с периодом {Period}", pair, timeframe);
        }

        public async void UnsubscribeCandles(string pair)
        {
            if (_disposed) return;

            var channelId = GetChannelId("candles", null, pair);
            if (channelId.HasValue)
            {
                var unsubscription = new
                {
                    @event = "unsubscribe",
                    chanId = channelId.Value
                };

                await SendMessageAsync(JsonSerializer.Serialize(unsubscription));
                _subscriptions.Remove(channelId.Value);
                _logger.LogInformation("Отправлена отписка от свечей для {Pair}", pair);
            }
        }

        public void Dispose()
        {
            lock (_lockObject)
            {
                if (_disposed) return;
                _disposed = true;
            }

            try
            {
                _cancellationTokenSource?.Cancel();
                _webSocket?.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None)?.Wait(1000);
            }
            catch
            {
               
            }
            finally
            {
                _webSocket?.Dispose();
                _cancellationTokenSource?.Dispose();
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

        private async Task ProcessChannelDataAsync(int channelId, JsonElement data)
        {
            if (!_subscriptions.TryGetValue(channelId, out var subscription))
            {
                _logger.LogWarning("Получены данные для неизвестного канала: {ChannelId}", channelId);
                return;
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

        private async Task ProcessTradeDataAsync(JsonElement data, string symbol)
        {
            try
            {
                // Проверяем формат данных
                if (data.GetArrayLength() >= 2)
                {
                    var secondElement = data[1];

                    // Если второй элемент - строка "te" или "tu", это одиночная сделка
                    if (secondElement.ValueKind == JsonValueKind.String)
                    {
                        var tradeArray = data[2];
                        var tradeData = new decimal[4];

                        for (int i = 0; i < 4 && i < tradeArray.GetArrayLength(); i++)
                        {
                            tradeData[i] = tradeArray[i].GetDecimal();
                        }

                        var trade = _tradeMapper.MapFromArray(tradeData, symbol);

                        if (trade.Amount > 0)
                            NewBuyTrade?.Invoke(trade);
                        else
                            NewSellTrade?.Invoke(trade);
                    }
                    // Если второй элемент - массив, это снимок сделок
                    else if (secondElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var tradeElement in secondElement.EnumerateArray())
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
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке данных сделок для {Symbol}", symbol);
            }
        }

        private async Task ProcessCandleDataAsync(JsonElement data, string key)
        {
            try
            {
                if (data.GetArrayLength() >= 2)
                {
                    var candleData = data[1];
                    var candleArray = new decimal[6];

                    for (int i = 0; i < 6 && i < candleData.GetArrayLength(); i++)
                    {
                        candleArray[i] = candleData[i].GetDecimal();
                    }

                    // Извлекаем символ из ключа (например, "trade:1m:tBTCUSD" -> "tBTCUSD")
                    var parts = key.Split(':');
                    var symbol = parts.Length > 2 ? parts[2] : key;

                    var candle = _candleMapper.MapFromArray(candleArray, symbol);
                    CandleSeriesProcessing?.Invoke(candle);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при обработке данных свечей для {Key}", key);
            }
        }

        private int? GetChannelId(string channel, string symbol = null, string keyPart = null)
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

        
    }
}
