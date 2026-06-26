using System.ComponentModel;
using System.Threading.Tasks;
using ABCCons.Function.Services;
using Microsoft.SemanticKernel;

namespace ABCCons.Function.Plugins
{
    public class DatasheetPlugin
    {
        private readonly IDatasheetService _datasheetService;
        private readonly SessionContext _sessionContext;

        public DatasheetPlugin(IDatasheetService datasheetService, SessionContext sessionContext)
        {
            _datasheetService = datasheetService;
            _sessionContext = sessionContext;
        }

        [KernelFunction]
        [Description("Retrieves the authoritative bearing product datasheet in JSON format for a given designation.")]
        public async Task<string> GetProductDatasheet(
            [Description("The product designation, e.g. '6205' or '6205 N'.")] string designation,
            [Description("The attribute name or symbol the user is asking about, e.g. 'Width', 'Outside diameter', 'D', 'B', etc. This tracks the conversation context.")] string attributeName)
        {
            var jsonContent = await _datasheetService.GetProductDatasheetAsync(designation);

            if (_sessionContext.State != null)
            {
                // Capture the designation and attribute name for stateful follow-ups/feedback
                _sessionContext.State.LastDesignation = designation;
                _sessionContext.State.LastAttribute = attributeName;
            }

            if (jsonContent == null)
            {
                return $"Product datasheet for '{designation}' was not found in the database.";
            }

            return jsonContent;
        }
    }
}
