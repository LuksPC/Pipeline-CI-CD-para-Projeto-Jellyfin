using System.Net.Mime;
using System.Text;
using MediaBrowser.Common.Json;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Server.Formatters
{
    /// <summary>
    /// Pascal Case Json Profile Formatter.
    /// </summary>
    public class PascalCaseJsonProfileFormatter : SystemTextJsonOutputFormatter
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="PascalCaseJsonProfileFormatter"/> class.
        /// </summary>
        public PascalCaseJsonProfileFormatter() : base(JsonDefaults.GetPascalCaseOptions())
        {
            SupportedMediaTypes.Clear();
            // Add application/json for default formatter
            SupportedEncodings.Add(Encoding.UTF8);
            SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse(MediaTypeNames.Application.Json));
            SupportedMediaTypes.Add(MediaTypeHeaderValue.Parse("application/json; profile=\"PascalCase\""));
        }
    }
}
