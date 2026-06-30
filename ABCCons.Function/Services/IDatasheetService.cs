using System.Threading;
using System.Threading.Tasks;

namespace ABCCons.Function.Services
{
    public interface IDatasheetService
    {
        Task<string?> GetProductDatasheetAsync(string designation, CancellationToken cancellationToken = default);
        Task<bool> ProductExistsAsync(string designation, CancellationToken cancellationToken = default);
    }
}
