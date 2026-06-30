using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ABCCons.Function.Services
{
    public class FileSystemPromptProvider : IPromptProvider
    {
        private readonly string _promptsDirectory;
        private readonly ILogger<FileSystemPromptProvider> _logger;
        private readonly ConcurrentDictionary<string, string> _cache = new();

        public FileSystemPromptProvider(IConfiguration configuration, ILogger<FileSystemPromptProvider> logger)
        {
            _logger = logger;
            // Use the "Prompts" folder under the content root by default.
            // If another path is configured, use it.
            _promptsDirectory = configuration["Prompts:DirectoryPath"] 
                                ?? Path.Combine(AppContext.BaseDirectory, "Prompts");
        }

        public async Task<string> GetPromptAsync(string promptName, CancellationToken cancellationToken = default)
        {
            if (_cache.TryGetValue(promptName, out var cachedPrompt))
            {
                return cachedPrompt;
            }

            // Check for promptName.md, then promptName.txt
            var mdFilePath = Path.Combine(_promptsDirectory, $"{promptName}.md");
            var txtFilePath = Path.Combine(_promptsDirectory, $"{promptName}.txt");

            string filePath;
            if (File.Exists(mdFilePath))
            {
                filePath = mdFilePath;
            }
            else if (File.Exists(txtFilePath))
            {
                filePath = txtFilePath;
            }
            else
            {
                _logger.LogError("Prompt template '{PromptName}' not found. Searched paths: '{MdPath}' and '{TxtPath}'",
                    promptName, mdFilePath, txtFilePath);

                throw new FileNotFoundException(
                    $"Prompt template '{promptName}' not found. Searched for: {promptName}.md or {promptName}.txt");
            }

            var promptText = await File.ReadAllTextAsync(filePath, cancellationToken);
            _cache[promptName] = promptText;
            return promptText;
        }
    }
}
