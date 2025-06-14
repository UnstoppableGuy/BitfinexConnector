using BitfinexConnector.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BitfinexConnector.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class RestClientController : ControllerBase
    {
        private readonly IRestClient _restClient;
        private readonly ILogger<RestClientController> _logger;

        public RestClientController(IRestClient restClient, ILogger<RestClientController> logger)
        {
            _restClient = restClient ?? throw new ArgumentNullException(nameof(restClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Получает последние сделки по указанной паре.
        /// </summary>
        [HttpGet("trades/{pair}")]
        public async Task<IActionResult> GetNewTradesAsync([FromRoute] string pair, [FromQuery] int maxCount = 100)
        {
            try
            {
                var trades = await _restClient.GetNewTradesAsync(pair, maxCount);
                return Ok(trades);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении трейдов для пары {} ", pair);
                return StatusCode(500, "Внутренняя ошибка сервера");
            }
        }

        /// <summary>
        /// Получает исторические свечи по указанной паре и периоду.
        /// </summary>
        [HttpGet("candles/{pair}/{periodInSec}")]
        public async Task<IActionResult> GetCandleSeriesAsync(
            [FromRoute] string pair,
            [FromRoute] int periodInSec,
            [FromQuery] long? from = null,
            [FromQuery] long? to = null,
            [FromQuery] long? limit = null)
        {
            try
            {
                var candles = await _restClient.GetCandleSeriesAsync(pair, periodInSec, from.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(from.Value) : (DateTimeOffset?)null,
                    to.HasValue ? DateTimeOffset.FromUnixTimeMilliseconds(to.Value) : (DateTimeOffset?)null, limit);
                return Ok(candles);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении свечей для пары {}", pair);
                return StatusCode(500, "Внутренняя ошибка сервера");
            }
        }

        /// <summary>
        /// Получает информацию о тикере по указанной паре.
        /// </summary>
        [HttpGet("ticker/{pair}")]
        public async Task<IActionResult> GetTickerAsync([FromRoute] string pair)
        {
            try
            {
                var ticker = await _restClient.GetTickerAsync(pair);
                return Ok(ticker);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при получении тикера для пары {}", pair);
                return StatusCode(500, "Внутренняя ошибка сервера");
            }
        }
    }
}
