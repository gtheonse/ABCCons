namespace ABCCons.Function.Services
{
    public interface IDatasheetService
    {
        Task<string?> GetProductDatasheetAsync(string designation);
        Task<bool> ProductExistsAsync(string designation);
    }
}
