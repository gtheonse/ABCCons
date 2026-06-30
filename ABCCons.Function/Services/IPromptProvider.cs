using System.Threading;
using System.Threading.Tasks;

namespace ABCCons.Function.Services
{
    public interface IPromptProvider
    {
        /// <summary>
        /// Retrieves the system or instructions prompt template by its key name.
        /// </summary>
        /// <param name="promptName">The name of the prompt (e.g. ClassificationPrompt).</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The prompt text template.</returns>
        Task<string> GetPromptAsync(string promptName, CancellationToken cancellationToken = default);
    }
}
