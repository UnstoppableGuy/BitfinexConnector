using BitfinexConnector.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Text.Json;

namespace BitfinexConnector.Services
{
    public class BitfinexExchange : IExchange
    {
        private readonly HttpClient _httpClient;
        private const string FX_ENDPOINT = "https://api-pub.bitfinex.com/v2/calc/fx";

        public BitfinexExchange(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));           
        }

        /// <summary>
        /// Получаем обменный курс между валютами
        /// </summary>
        /// <param name="from">Валют для обмена</param>
        /// <param name="to">Валюта для получения</param>
        /// <returns>Ставка обмена</returns>
        public async Task<decimal> GetExchangeRateAsync(string fromCurrency, string toCurrency)
        {
            if (fromCurrency.Equals(toCurrency, StringComparison.OrdinalIgnoreCase))
            {
                return 1m;
            }

            try
            {
                var request = new
                {
                    ccy1 = fromCurrency.ToUpper(),
                    ccy2 = toCurrency.ToUpper()
                };
                var jsonContent = new StringContent(JsonSerializer.Serialize(request),
                                                    Encoding.UTF8,
                                                    "application/json");

                var response = await _httpClient.PostAsync(FX_ENDPOINT, jsonContent);
                var content = await response.Content.ReadAsStringAsync();

                var trades = JsonSerializer.Deserialize<decimal[]>(content);
                return trades[0];
            }
            catch (HttpRequestException ex)
            {
                throw new ApplicationException("Ошибка при получении курса обмена.", ex);
            }
            catch (JsonException ex)
            {
                throw new ApplicationException("Ошибка десериализации ответа от API.", ex);
            }
        }
    }
}
