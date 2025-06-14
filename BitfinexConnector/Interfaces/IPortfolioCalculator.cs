namespace BitfinexConnector.Interfaces
{
    public interface IPortfolioCalculator
    {
        Task<Dictionary<string, decimal>> CalculatePortfolioBalanceAsync(Dictionary<string, decimal> holdings);
    }
}
