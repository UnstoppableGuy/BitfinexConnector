using BitfinexConnector.Interfaces;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BitfinexConnector.Services
{
    public class PortfolioCalculatorService : IPortfolioCalculator 
    {
        private readonly IExchange _exchange;
        private readonly ILogger<PortfolioCalculatorService> _logger;
        private readonly Dictionary<string, decimal> _exchangeRates = new();
        private string[] TargetCurrencies { get; set; } = ["USD", "BTC", "XRP", "XMR", "DSH"];

        public PortfolioCalculatorService(IExchange exchange, 
                                         ILogger<PortfolioCalculatorService> logger)
        {
            _exchange = exchange ?? throw new ArgumentNullException(nameof(exchange));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<Dictionary<string, decimal>> CalculatePortfolioBalanceAsync(Dictionary<string, decimal> holdings)
        {
            try
            {
                _logger.LogInformation("Начинаю расчет портфеля для {Count} валют", holdings.Count);

                // Получаем курсы валют
                await LoadExchangeRatesAsync(holdings.Keys);

                var result = new Dictionary<string, decimal>();

                foreach (var targetCurrency in TargetCurrencies)
                {
                    var totalBalance = CalculateBalanceInCurrency(holdings, targetCurrency);
                    result[targetCurrency] = totalBalance;
                    _logger.LogInformation("Общий баланс в {Currency}: {Balance}", targetCurrency, totalBalance);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка при расчете портфеля");
                return new Dictionary<string, decimal>();
            }
        }

        private decimal CalculateBalanceInCurrency(Dictionary<string, decimal> holdings, string targetCurrency)
        {
            decimal totalBalance = 0;

            foreach (var holding in holdings)
            {
                var sourceCurrency = holding.Key;
                var amount = holding.Value;

                if (sourceCurrency == targetCurrency)
                {
                    totalBalance += amount;
                }
                else
                {
                    var rate = GetExchangeRate(sourceCurrency, targetCurrency);
                    totalBalance += amount * rate;
                }
            }

            return totalBalance;
        }

        private async Task LoadExchangeRatesAsync(IEnumerable<string> currencies)
        {
            var rateTasks = currencies
                .SelectMany(source => TargetCurrencies
                    .Where(target => source != target)
                    .Select(target => GetAndCacheRateAsync(source, target)))
                .ToList();

            await Task.WhenAll(rateTasks);
        }

        private async Task GetAndCacheRateAsync(string from, string to)
        {
            try
            {
                var rate = await _exchange.GetExchangeRateAsync(from, to);
                _exchangeRates[$"{from}_{to}"] = rate;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Не удалось получить курс обмена {From} -> {To}", from, to);
                // Устанавливаем курс по умолчанию, чтобы избежать ошибок
                _exchangeRates[$"{from}_{to}"] = 0;
            }
        }

        private decimal GetExchangeRate(string fromCurrency, string toCurrency)
        {
            if (fromCurrency == toCurrency)
                return 1;

            var rateKey = $"{fromCurrency}_{toCurrency}";

            if (_exchangeRates.TryGetValue(rateKey, out var rate))
            {
                return rate;
            }

            _logger.LogWarning("Не удалось найти курс обмена {From} -> {To}", fromCurrency, toCurrency);
            return 0; // Возвращаем 1 вместо исключения для стабильности
        }
    }
}
