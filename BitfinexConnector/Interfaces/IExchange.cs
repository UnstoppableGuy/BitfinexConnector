namespace BitfinexConnector.Interfaces
{
    public interface IExchange
    {
        Task<decimal> GetExchangeRateAsync(string fromCurrency, string toCurrency);
    }
}
