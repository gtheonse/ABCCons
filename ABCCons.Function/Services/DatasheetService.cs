using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ABCCons.Function.Services
{
    public class DatasheetService : IDatasheetService
    {
        private readonly string _productsPath;
        private readonly ILogger<DatasheetService> _logger;
        private readonly ConcurrentDictionary<string, JsonElement> _productCache = new(StringComparer.OrdinalIgnoreCase);
        private volatile bool _isInitialized = false;
        private readonly object _lock = new();

        public DatasheetService(IConfiguration configuration, ILogger<DatasheetService> logger)
        {
            _productsPath = configuration["ABCProducts:Path"] 
                            ?? configuration["ABCProducts__Path"]
                            ?? configuration["Values:ABCProducts:Path"]
                            ?? configuration["Values:ABCProducts__Path"]
                            ?? "ABCproducts";
            _logger = logger;
        }

        private void Initialize()
        {
            if (_isInitialized) return;
            lock (_lock)
            {
                if (_isInitialized) return;
                try
                {
                    _logger.LogInformation("Initializing DatasheetService with path: {Path}", _productsPath);
                    if (!Directory.Exists(_productsPath))
                    {
                        _logger.LogWarning("Products directory not found: {Path}", _productsPath);
                        _isInitialized = true;
                        return;
                    }

                    var files = Directory.GetFiles(_productsPath, "*.json");
                    foreach (var file in files)
                    {
                        try
                        {
                            var content = File.ReadAllText(file);
                            using var doc = JsonDocument.Parse(content);
                            var root = doc.RootElement.Clone();
                            if (root.TryGetProperty("designation", out var designationProp))
                            {
                                var designation = designationProp.GetString();
                                if (!string.IsNullOrEmpty(designation))
                                {
                                    _productCache[designation] = root;
                                    _logger.LogInformation("Loaded product: {Designation} from {File}", designation, Path.GetFileName(file));
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error reading product file {File}", file);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error scanning products directory {Path}", _productsPath);
                }
                _isInitialized = true;
            }
        }

        public Task<bool> ProductExistsAsync(string designation)
        {
            Initialize();
            return Task.FromResult(_productCache.ContainsKey(designation.Trim()));
        }

        public Task<string?> GetProductDatasheetAsync(string designation)
        {
            Initialize();
            var normalizedDesignation = designation.Trim();
            if (!_productCache.TryGetValue(normalizedDesignation, out var productJson))
            {
                _logger.LogWarning("Product '{Designation}' not found in datasheet cache.", normalizedDesignation);
                return Task.FromResult<string?>(null);
            }

            return Task.FromResult<string?>(productJson.GetRawText());
        }
    }
}
