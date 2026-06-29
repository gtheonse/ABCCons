using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;

namespace ABCCons.Function.Services
{
    public class FileSystemPromptProvider : IPromptProvider
    {
        private readonly string _promptsDirectory;
        private readonly ConcurrentDictionary<string, string> _cache = new();

        public FileSystemPromptProvider(IConfiguration configuration)
        {
            // Use the "Prompts" folder under the content root by default.
            // If another path is configured, use it.
            _promptsDirectory = configuration["Prompts:DirectoryPath"] 
                                ?? Path.Combine(AppContext.BaseDirectory, "Prompts");
        }

        public async Task<string> GetPromptAsync(string promptName)
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
                throw new FileNotFoundException(
                    $"Prompt template '{promptName}' not found. Searched for: {mdFilePath} and {txtFilePath}");
            }

            var promptText = await File.ReadAllTextAsync(filePath);
            _cache[promptName] = promptText;
            return promptText;
        }
    }
}
