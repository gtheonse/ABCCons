using ABCCons.Function.Services;
using Microsoft.SemanticKernel;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

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
            CancellationToken cancellationToken = default)
        {
            var jsonContent = await _datasheetService.GetProductDatasheetAsync(designation, cancellationToken);

            if (_sessionContext.State != null)
            {
                // Capture the designation for stateful follow-ups/feedback
                _sessionContext.State.LastDesignation = designation;
            }

            if (jsonContent == null)
            {
                return $"Product datasheet for '{designation}' was not found in the database.";
            }

            return jsonContent;
        }

        [KernelFunction]
        [Description("Records the product designation and attribute that was just discussed, for follow-up context. Call this after answering a question.")]
        public string TrackConversationContext(
            [Description("The product designation just discussed, e.g. '6205'.")] string designation,
            [Description("The attribute name just discussed, e.g. 'Width', 'Outside diameter'.")] string attributeName)
        {
            if (_sessionContext.State != null)
            {
                _sessionContext.State.LastDesignation = designation;
                _sessionContext.State.LastAttribute = attributeName;
            }
            return "Context saved.";
        }
    }
}
