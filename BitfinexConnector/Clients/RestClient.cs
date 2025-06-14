using BitfinexConnector.Interfaces;
using BitfinexConnector.Models;
using Microsoft.Extensions.Logging;
using System.Text.Json;
namespace BitfinexConnector.Clients
{
    public class RestClient : IRestClient, IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<RestClient> _logger;
        private readonly IDataMapper<decimal[], Trade> _tradeMapper;
        private readonly IDataMapper<decimal[], Candle> _candleMapper;
        private readonly IDataMapper<decimal[], Ticker> _tickerMapper;

        public RestClient(HttpClient httpClient,
                          ILogger<RestClient> logger,
                          IDataMapper<decimal[], Trade> tradeMapper,
                          IDataMapper<decimal[], Candle> candleMapper,
                          IDataMapper<decimal[], Ticker> tickerMapper)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            if (_httpClient.BaseAddress == null)
            {
                _httpClient.BaseAddress = new Uri("https://api-pub.bitfinex.com/v2/");
            }
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _tradeMapper = tradeMapper ?? throw new ArgumentNullException(nameof(tradeMapper));
            _candleMapper = candleMapper ?? throw new ArgumentNullException(nameof(candleMapper));
            _tickerMapper = tickerMapper ?? throw new ArgumentNullException(nameof(tickerMapper));
        }

        public async Task<IEnumerable<Trade>> GetNewTradesAsync(string pair, int maxCount)
        {
            try
            {
                var path = $"trades/{pair}/hist?limit={maxCount}";
                _logger.LogInformation("Запрашиваю историю трейдов для пары {Pair} с лимитом {MaxCount}", pair, maxCount);

                var response = await _httpClient.GetAsync(path);
                LogResponse(response);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                _logger.LogDebug("Получен ответ от API: {Content}", Truncate(content, 200));

                var trades = JsonSerializer.Deserialize<List<decimal[]>>(content);

                if (trades == null || trades.Count == 0)
                    return [];

                return _tradeMapper.MapFromArrays(trades, pair);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении трейдов для пары {Pair}", pair);
                throw;
            }
        }

        public async Task<IEnumerable<Candle>> GetCandleSeriesAsync(string pair, int periodInSec, DateTimeOffset? from, DateTimeOffset? to = null, long? count = 0)
        {
            try
            {
                var query = PathBuilder(from, to, count);
                var path = $"candles/trade:{periodInSec}:{pair}/hist?{query}";

                _logger.LogInformation("Запрашиваю историю свечей для пары {Pair}, период: {Period}", pair, periodInSec);

                var response = await _httpClient.GetAsync(path);
                LogResponse(response);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                _logger.LogDebug("Получен ответ от API: {Content}", Truncate(content, 200));

                var candles = JsonSerializer.Deserialize<List<decimal[]>>(content);

                if (candles == null || candles.Count == 0)
                    return [];

                return _candleMapper.MapFromArrays(candles, pair);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении свечей для пары {Pair}", pair);
                throw;
            }
        }

        public async Task<Ticker> GetTickerAsync(string pair)
        {
            try
            {
                var response = await _httpClient.GetAsync($"ticker/{pair}");

                _logger.LogInformation("Запрашиваю тикер для пары {Pair}", pair);
                LogResponse(response);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                _logger.LogDebug("Получен ответ от API: {Content}", Truncate(content, 200));

                var values = JsonSerializer.Deserialize<decimal[]>(content);

                if (values == null || values.Length == 0)
                    return null;

                return _tickerMapper.MapFromArray(values, pair);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении тикера для пары {Pair}", pair);
                throw;
            }
        }

        private static string Truncate(string text, int maxLength)
        {
            return text.Length <= maxLength ? text : string.Concat(text.AsSpan(0, maxLength - 3), "...");
        }

        private void LogResponse(HttpResponseMessage response)
        {
            _logger.LogDebug(
                "HTTP статус: {Status}, Заголовки: {Headers}",
                response.StatusCode,
                string.Join(", ", response.Headers.Select(h => $"{h.Key}: {string.Join(",", h.Value)}")));
        }

        private static string PathBuilder(DateTimeOffset? from, DateTimeOffset? to, long? count)
        {
            var parameters = new Dictionary<string, string>();

            if (from.HasValue) parameters.Add("start", from.Value.ToUnixTimeMilliseconds().ToString());

            if (to.HasValue) parameters.Add("end", to.Value.ToUnixTimeMilliseconds().ToString());

            if (count.HasValue && count.Value > 0) parameters.Add("limit", count.Value.ToString());

            return string.Join("&", parameters.Select(kv => $"{kv.Key}={kv.Value}"));
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }
}
