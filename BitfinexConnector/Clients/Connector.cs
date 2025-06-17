using BitfinexConnector.Interfaces;
using BitfinexConnector.Models;
using Microsoft.Extensions.Logging;
namespace BitfinexConnector.Clients
{
    public class Connector : ITestConnector, IDisposable
    {
        private readonly IRestClient _restClient;
        private readonly IWebSocketClient _socketClient;
        private readonly ILogger<Connector> _logger;
        public Connector(
                        IRestClient restClient,
                        IWebSocketClient socketClient,
                        ILogger<Connector> logger)
        {
            _restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
            _socketClient = socketClient ?? throw new ArgumentNullException(nameof(socketClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _socketClient.NewBuyTrade += (trade) => NewBuyTrade?.Invoke(trade);
            _socketClient.NewSellTrade += (trade) => NewSellTrade?.Invoke(trade);
            _socketClient.CandleSeriesProcessing += (candle) => CandleSeriesProcessing?.Invoke(candle);
        }


        #region Rest

        public async Task<IEnumerable<Trade>> GetNewTradesAsync(string pair, int maxCount)
        {
            try
            {
                _logger.LogInformation("Получение трейдов для пары {Pair}", pair);
                return await _restClient.GetNewTradesAsync(pair, maxCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении трейдов для пары {Pair}", pair);
                throw new ApplicationException($"Ошибка при получении трейдов для {pair}: {ex.Message}", ex);
            }
        }

        public async Task<IEnumerable<Candle>> GetCandleSeriesAsync(string pair, int periodInSec, DateTimeOffset? from, DateTimeOffset? to = null, long? count = 0)
        {
            try
            {
                _logger.LogInformation("Получение свечей для пары {Pair}", pair);
                return await _restClient.GetCandleSeriesAsync(pair, periodInSec, from, to, count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении свечей для пары {Pair}", pair);
                throw new ApplicationException($"Ошибка при получении свечей для {pair}: {ex.Message}", ex);
            }
        }

        public async Task<Ticker> GetTickerAsync(string pair)
        {
            try
            {
                _logger.LogInformation("Получение тикера для пары {Pair}", pair);
                return await _restClient.GetTickerAsync(pair);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении тикера для пары {Pair}", pair);
                throw new ApplicationException($"Ошибка при получении тикера для {pair}: {ex.Message}", ex);
            }
        }

        #endregion

        #region Socket

        public event Action<Trade> NewBuyTrade;
        public event Action<Trade> NewSellTrade;
        public event Action<Candle> CandleSeriesProcessing;

        public void SubscribeTrades(string pair, int maxCount = 100)
        {
            try
            {
                _logger.LogInformation("Подписка на трейды для пары {Pair}", pair);
                _socketClient.SubscribeTrades(pair, maxCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при подписке на трейды для пары {Pair}", pair);
                throw;
            }
        }

        public void UnsubscribeTrades(string pair)
        {
            try
            {
                _logger.LogInformation("Отписка от трейдов для пары {Pair}", pair);
                _socketClient.UnsubscribeTrades(pair);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при отписке от трейдов для пары {Pair}", pair);
                throw;
            }
        }

        public void SubscribeCandles(string pair, int periodInSec, DateTimeOffset? from = null, DateTimeOffset? to = null, long? count = 0)
        {
            try
            {
                _logger.LogInformation("Подписка на свечи для пары {Pair}", pair);
                _socketClient.SubscribeCandles(pair, periodInSec, from, to, count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при подписке на свечи для пары {Pair}", pair);
                throw;
            }
        }

        public void UnsubscribeCandles(string pair)
        {
            try
            {
                _logger.LogInformation("Отписка от свечей для пары {Pair}", pair);
                _socketClient.UnsubscribeCandles(pair);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при отписке от свечей для пары {Pair}", pair);
                throw;
            }
        }

        #endregion

        #region Event Handlers

        private void OnNewBuyTrade(Trade trade)
        {
            _logger.LogDebug("Получен новый трейд на покупку для пары {Pair}", trade.Pair);
            NewBuyTrade?.Invoke(trade);
        }

        private void OnNewSellTrade(Trade trade)
        {
            _logger.LogDebug("Получен новый трейд на продажу для пары {Pair}", trade.Pair);
            NewSellTrade?.Invoke(trade);
        }

        private void OnCandleSeriesProcessing(Candle candle)
        {
            _logger.LogDebug("Получена новая свеча для пары {Pair}", candle.Pair);
            CandleSeriesProcessing?.Invoke(candle);
        }

        #endregion

        public void Dispose()
        {
            _socketClient?.Dispose();
        }
    }
}
