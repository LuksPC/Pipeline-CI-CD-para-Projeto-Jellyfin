using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Net.Mime;
using System.Text;
using System.Threading.Tasks;
using Emby.Dlna;
using Emby.Dlna.Eventing;
using Emby.Dlna.PlayTo.EventArgs;
using Emby.Dlna.Server;
using Jellyfin.Api.Attributes;
using MediaBrowser.Controller.Dlna;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Api.Controllers
{
    /// <summary>
    /// Dlna Server Controller.
    /// </summary>
    [Route("Dlna")]
    public class DlnaServerController : BaseJellyfinApiController
    {
        private readonly IDlnaManager _dlnaManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="DlnaServerController"/> class.
        /// </summary>
        /// <param name="dlnaManager">Instance of the <see cref="IDlnaManager"/> interface.</param>
        public DlnaServerController(IDlnaManager dlnaManager)
        {
            _dlnaManager = dlnaManager;
        }

        /// <summary>
        /// Get Description Xml.
        /// </summary>
        /// <param name="serverId">Server UUID.</param>
        /// <response code="200">Description xml returned.</response>
        /// <returns>An <see cref="OkResult"/> containing the description xml.</returns>
        [HttpGet("{serverId}/description")]
        [HttpGet("{serverId}/description.xml", Name = "GetDescriptionXml_2")]
        [Produces(MediaTypeNames.Text.Xml)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public ActionResult GetDescriptionXml([FromRoute] string serverId)
        {
            if (DlnaEntryPoint.Instance.IsDLNAServerEnabled)
            {
                var xml = _dlnaManager.GetServerDescriptionXml(Request.Headers, serverId, Request);
                return Ok(xml);
            }

            return NotFound();
        }

        /// <summary>
        /// Gets Dlna content directory xml.
        /// </summary>
        /// <param name="serverId">Server UUID.</param>
        /// <response code="200">Dlna content directory returned.</response>
        /// <returns>An <see cref="OkResult"/> containing the dlna content directory xml.</returns>
        [HttpGet("{serverId}/ContentDirectory")]
        [HttpGet("{serverId}/ContentDirectory/ContentDirectory")]
        [HttpGet("{serverId}/ContentDirectory/ContentDirectory.xml", Name = "GetContentDirectory_2")]
        [Produces(MediaTypeNames.Text.Xml)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [SuppressMessage("Microsoft.Performance", "CA1801:ReviewUnusedParameters", MessageId = "serverId", Justification = "Required for DLNA")]
        public ActionResult GetContentDirectory([FromRoute] string serverId)
        {
            if (DlnaEntryPoint.ContentDirectory != null)
            {
                return Ok(DlnaEntryPoint.ContentDirectory.GetServiceXml());
            }

            return NotFound();
        }

        /// <summary>
        /// Gets Dlna media receiver registrar xml.
        /// </summary>
        /// <param name="serverId">Server UUID.</param>
        /// <returns>Dlna media receiver registrar xml.</returns>
        [HttpGet("{serverId}/MediaReceiverRegistrar")]
        [HttpGet("{serverId}/MediaReceiverRegistrar/MediaReceiverRegistrar")]
        [HttpGet("{serverId}/MediaReceiverRegistrar/MediaReceiverRegistrar.xml", Name = "GetMediaReceiverRegistrar_2")]
        [Produces(MediaTypeNames.Text.Xml)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [SuppressMessage("Microsoft.Performance", "CA1801:ReviewUnusedParameters", MessageId = "serverId", Justification = "Required for DLNA")]
        public ActionResult GetMediaReceiverRegistrar([FromRoute] string serverId)
        {
            if (DlnaEntryPoint.MediaReceiverRegistrar != null)
            {
                return Ok(DlnaEntryPoint.MediaReceiverRegistrar.GetServiceXml());
            }

            return NotFound();
        }

        /// <summary>
        /// Gets Dlna media receiver registrar xml.
        /// </summary>
        /// <param name="serverId">Server UUID.</param>
        /// <returns>Dlna media receiver registrar xml.</returns>
        [HttpGet("{serverId}/ConnectionManager")]
        [HttpGet("{serverId}/ConnectionManager/ConnectionManager")]
        [HttpGet("{serverId}/ConnectionManager/ConnectionManager.xml", Name = "GetConnectionManager_2")]
        [Produces(MediaTypeNames.Text.Xml)]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [SuppressMessage("Microsoft.Performance", "CA1801:ReviewUnusedParameters", MessageId = "serverId", Justification = "Required for DLNA")]
        public ActionResult GetConnectionManager([FromRoute] string serverId)
        {
            if (DlnaEntryPoint.ConnectionManager != null)
            {
                return Ok(DlnaEntryPoint.ConnectionManager.GetServiceXml());
            }

            return NotFound();
        }

        /// <summary>
        /// Process a content directory control request.
        /// </summary>
        /// <param name="serverId">Server UUID.</param>
        /// <returns>Control response.</returns>
        [HttpPost("{serverId}/ContentDirectory/Control")]
        [Produces(MediaTypeNames.Text.Xml)]
        public async Task<ActionResult<ControlResponse>> ProcessContentDirectoryControlRequest([FromRoute] string serverId)
        {
            if (DlnaEntryPoint.ContentDirectory != null)
            {
                return await ProcessControlRequestInternalAsync(serverId, Request.Body, DlnaEntryPoint.ContentDirectory).ConfigureAwait(false);
            }

            return NotFound();
        }

        /// <summary>
        /// Process a connection manager control request.
        /// </summary>
        /// <param name="serverId">Server UUID.</param>
        /// <returns>Control response.</returns>
        [HttpPost("{serverId}/ConnectionManager/Control")]
        [Produces(MediaTypeNames.Text.Xml)]
        public async Task<ActionResult<ControlResponse>> ProcessConnectionManagerControlRequest([FromRoute] string serverId)
        {
            if (DlnaEntryPoint.ConnectionManager != null)
            {
                return await ProcessControlRequestInternalAsync(serverId, Request.Body, DlnaEntryPoint.ConnectionManager).ConfigureAwait(false);
            }

            return NotFound();
        }

        /// <summary>
        /// Process a media receiver registrar control request.
        /// </summary>
        /// <param name="serverId">Server UUID.</param>
        /// <returns>Control response.</returns>
        [HttpPost("{serverId}/MediaReceiverRegistrar/Control")]
        [Produces(MediaTypeNames.Text.Xml)]
        public async Task<ActionResult<ControlResponse>> ProcessMediaReceiverRegistrarControlRequest([FromRoute] string serverId)
        {
            if (DlnaEntryPoint.MediaReceiverRegistrar != null)
            {
                return await ProcessControlRequestInternalAsync(serverId, Request.Body, DlnaEntryPoint.MediaReceiverRegistrar).ConfigureAwait(false);
            }

            return NotFound();
        }

        /// <summary>
        /// Processes an event subscription request.
        /// </summary>
        /// <param name="serverId">Server UUID.</param>
        /// <returns>Event subscription response.</returns>
        [HttpSubscribe("{serverId}/MediaReceiverRegistrar/Events")]
        [HttpUnsubscribe("{serverId}/MediaReceiverRegistrar/Events")]
        [ApiExplorerSettings(IgnoreApi = true)] // Ignore in openapi docs
        [Produces(MediaTypeNames.Text.Xml)]
        [SuppressMessage("Microsoft.Performance", "CA1801:ReviewUnusedParameters", MessageId = "serverId", Justification = "Required for DLNA")]
        public ActionResult<EventSubscriptionResponse> ProcessMediaReceiverRegistrarEventRequest(string serverId)
        {
            if (DlnaEntryPoint.MediaReceiverRegistrar != null)
            {
                return ProcessEventRequest(DlnaEntryPoint.MediaReceiverRegistrar);
            }

            return NotFound();
        }

        /// <summary>
        /// Processes an event subscription request.
        /// </summary>
        /// <param name="serverId">Server UUID.</param>
        /// <returns>Event subscription response.</returns>
        [HttpSubscribe("{serverId}/ContentDirectory/Events")]
        [HttpUnsubscribe("{serverId}/ContentDirectory/Events")]
        [ApiExplorerSettings(IgnoreApi = true)] // Ignore in openapi docs
        [SuppressMessage("Microsoft.Performance", "CA1801:ReviewUnusedParameters", MessageId = "serverId", Justification = "Required for DLNA")]
        [Produces(MediaTypeNames.Text.Xml)]
        public ActionResult<EventSubscriptionResponse> ProcessContentDirectoryEventRequest(string serverId)
        {
            if (DlnaEntryPoint.ContentDirectory != null)
            {
                return ProcessEventRequest(DlnaEntryPoint.ContentDirectory);
            }

            return NotFound();
        }

        /// <summary>
        /// Processes an event subscription request.
        /// </summary>
        /// <param name="serverId">Server UUID.</param>
        /// <returns>Event subscription response.</returns>
        [HttpSubscribe("{serverId}/ConnectionManager/Events")]
        [HttpUnsubscribe("{serverId}/ConnectionManager/Events")]
        [ApiExplorerSettings(IgnoreApi = true)] // Ignore in openapi docs
        [SuppressMessage("Microsoft.Performance", "CA1801:ReviewUnusedParameters", MessageId = "serverId", Justification = "Required for DLNA")]
        [Produces(MediaTypeNames.Text.Xml)]
        public ActionResult<EventSubscriptionResponse> ProcessConnectionManagerEventRequest(string serverId)
        {
            if (DlnaEntryPoint.ConnectionManager != null)
            {
                return ProcessEventRequest(DlnaEntryPoint.ConnectionManager);
            }

            return NotFound();
        }

        /// <summary>
        /// Gets a server icon.
        /// </summary>
        /// <param name="serverId">Server UUID.</param>
        /// <param name="fileName">The icon filename.</param>
        /// <returns>Icon stream.</returns>
        [HttpGet("{serverId}/icons/{fileName}")]
        [SuppressMessage("Microsoft.Performance", "CA1801:ReviewUnusedParameters", MessageId = "serverId", Justification = "Required for DLNA")]
        [Produces(MediaTypeNames.Text.Xml)]
        public ActionResult GetIconId([FromRoute] string serverId, [FromRoute] string fileName)
        {
            if (DlnaEntryPoint.Instance.IsDLNAServerEnabled)
            {
                return GetIconInternal(fileName);
            }

            return NotFound();
        }

        /// <summary>
        /// Gets a server icon.
        /// </summary>
        /// <param name="fileName">The icon filename.</param>
        /// <returns>Icon stream.</returns>
        [HttpGet("icons/{fileName}")]
        [Produces(MediaTypeNames.Text.Xml)]
        public ActionResult GetIcon([FromRoute] string fileName)
        {
            if (DlnaEntryPoint.Instance.IsDLNAServerEnabled)
            {
                return GetIconInternal(fileName);
            }

            return NotFound();
        }

        /// <summary>
        /// Processes device subscription events.
        /// </summary>
        /// <param name="id">Id of the device.</param>
        /// <param name="requestStream">XML data stream.</param>
        /// <returns>Event subscription response.</returns>
        [HttpSubscribe("Eventing/{Id}/")]
        [ApiExplorerSettings(IgnoreApi = true)] // Ignore in openapi docs
        [SuppressMessage("Microsoft.Performance", "CA1801:ReviewUnusedParameters", MessageId = "serverId", Justification = "Required for DLNA")]
        [Produces(MediaTypeNames.Text.Xml)]
        public async Task<ActionResult> ProcessDeviceNotifification(string id, Stream requestStream)
        {
            try
            {
                using var reader = new StreamReader(requestStream, Encoding.UTF8);
                string response = await reader.ReadToEndAsync().ConfigureAwait(false);

                if (DlnaEntryPoint.PlayToManager != null)
                {
                    await DlnaEntryPoint.PlayToManager.NotifyDevice(new DlnaEventArgs(id, response)).ConfigureAwait(false);
                }
            }
            catch
            {
                // Ignore connection forcible closed messages.
            }

            return Ok();
        }

        private ActionResult GetIconInternal(string fileName)
        {
            var icon = _dlnaManager.GetIcon(fileName);
            if (icon == null)
            {
                return NotFound();
            }

            var contentType = "image/" + Path.GetExtension(fileName)
                .TrimStart('.')
                .ToLowerInvariant();

            return File(icon.Stream, contentType);
        }

        private string GetAbsoluteUri()
        {
            return $"{Request.Scheme}://{Request.Host}{Request.Path}";
        }

        private Task<ControlResponse> ProcessControlRequestInternalAsync(string id, Stream requestStream, IUpnpService service)
        {
            return service.ProcessControlRequestAsync(new ControlRequest(Request.Headers)
            {
                InputXml = requestStream,
                TargetServerUuId = id,
                RequestedUrl = GetAbsoluteUri()
            });
        }

        private EventSubscriptionResponse ProcessEventRequest(IEventManager eventManager)
        {
            var subscriptionId = Request.Headers["SID"];
            if (string.Equals(Request.Method, "subscribe", StringComparison.OrdinalIgnoreCase))
            {
                var notificationType = Request.Headers["NT"];
                var callback = Request.Headers["CALLBACK"];
                var timeoutString = Request.Headers["TIMEOUT"];

                if (string.IsNullOrEmpty(notificationType))
                {
                    return eventManager.RenewEventSubscription(
                        subscriptionId,
                        notificationType,
                        timeoutString,
                        callback);
                }

                return eventManager.CreateEventSubscription(notificationType, timeoutString, callback);
            }

            return eventManager.CancelEventSubscription(subscriptionId);
        }
    }
}
