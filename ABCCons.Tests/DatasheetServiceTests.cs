using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ABCCons.Function.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ABCCons.Tests
{
    public class DatasheetServiceTests
    {
        private readonly IDatasheetService _datasheetService;

        public DatasheetServiceTests()
        {
            // Setup Configuration pointing to the actual ABCproducts folder
            var inMemorySettings = new Dictionary<string, string?>
            {
                { "ABCProducts__Path", Path.Combine(Directory.GetCurrentDirectory(), "../../../../ABCproducts") }
            };

            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();

            _datasheetService = new DatasheetService(configuration, NullLogger<DatasheetService>.Instance);
        }

        [Fact]
        public async Task ProductExistsAsync_ShouldReturnTrueForExistingProducts()
        {
            // Act & Assert
            Assert.True(await _datasheetService.ProductExistsAsync("6205"));
            Assert.True(await _datasheetService.ProductExistsAsync("6205 N"));
        }

        [Fact]
        public async Task ProductExistsAsync_ShouldReturnFalseForNonExistingProducts()
        {
            // Act & Assert
            Assert.False(await _datasheetService.ProductExistsAsync("9999"));
            Assert.False(await _datasheetService.ProductExistsAsync("random-name"));
        }

        [Fact]
        public async Task GetProductDatasheetAsync_ShouldReturnJsonStringForExistingProducts()
        {
            // Act
            var sheet6205 = await _datasheetService.GetProductDatasheetAsync("6205");
            var sheet6205N = await _datasheetService.GetProductDatasheetAsync("6205 N");

            // Assert
            Assert.NotNull(sheet6205);
            Assert.Contains("\"designation\": \"6205\"", sheet6205);
            Assert.Contains("\"name\": \"Width\"", sheet6205);

            Assert.NotNull(sheet6205N);
            Assert.Contains("\"designation\": \"6205 N\"", sheet6205N);
        }

        [Fact]
        public async Task GetProductDatasheetAsync_ShouldReturnNullForNonExistingProducts()
        {
            // Act
            var sheet = await _datasheetService.GetProductDatasheetAsync("9999");

            // Assert
            Assert.Null(sheet);
        }
    }
}
