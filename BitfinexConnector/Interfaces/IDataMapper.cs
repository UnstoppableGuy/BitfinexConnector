namespace BitfinexConnector.Interfaces
{
    public interface IDataMapper<TSource, TTarget>
    {
        TTarget MapFromArray(decimal[] source, string? additionalData = null);
        IEnumerable<TTarget> MapFromArrays(IEnumerable<decimal[]> source, string? additionalData = null);
    }
}
