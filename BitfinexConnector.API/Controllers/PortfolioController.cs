using BitfinexConnector.Interfaces;
using BitfinexConnector.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BitfinexConnector.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PortfolioController : ControllerBase
    {
        private readonly IPortfolioCalculator _portfolioCalculator;
        private readonly ILogger<PortfolioController> _logger;

        public PortfolioController(IPortfolioCalculator portfolioCalculator, ILogger<PortfolioController> logger)
        {
            _portfolioCalculator = portfolioCalculator ?? throw new ArgumentNullException(nameof(portfolioCalculator));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        ///// <summary>
        ///// Рассчитывает баланс портфеля в различных валютах
        ///// </summary>
        ///// <returns>Баланс портфеля в USDT, BTC, XRP, XMR, DASH</returns>
        [HttpGet("balance")]
        public async Task<ActionResult<List<PortfolioCalculatorService>>> GetPortfolioBalances()
        {
            try
            {
                _logger.LogInformation("Calculating portfolio balances");
                var portfolio = new Dictionary<string, decimal>
                {
                    ["BTC"] = 1m,
                    ["XRP"] = 15000m,
                    ["XMR"] = 50m,
                    ["DSH"] = 30m
                };

                var targetCurrencies = new List<string> { "USDT", "BTC", "XRP", "XMR", "DSH" };

                var balances = await _portfolioCalculator.CalculatePortfolioBalanceAsync(portfolio);

                _logger.LogInformation("Successfully calculated portfolio balances");

                return Ok(balances);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating portfolio balances");
                return StatusCode(500, "An error occurred while calculating portfolio balances");
            }
        }
    }
}
