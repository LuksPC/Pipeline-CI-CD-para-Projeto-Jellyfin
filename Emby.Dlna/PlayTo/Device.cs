#pragma warning disable CS1591
#nullable enable

namespace Emby.Dlna.PlayTo
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Linq;
    using System.Net;
    using System.Net.Http;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using System.Xml;
    using System.Xml.Linq;
    using Emby.Dlna.Common;
    using Emby.Dlna.Server;
    using Emby.Dlna.Ssdp;
    using MediaBrowser.Common.Net;
    using MediaBrowser.Model.Net;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Defines the <see cref="Device" />.
    /// </summary>
    public class Device : IDisposable
    {
        /// <summary>
        /// Defines the USERAGENT.
        /// </summary>
        private const string USERAGENT = "Microsoft-Windows/6.2 UPnP/1.0 Microsoft-DLNA DLNADOC/1.50";

        /// <summary>
        /// Defines the FriendlyName.
        /// </summary>
        private const string FriendlyName = "Jellyfin";

        /// <summary>
        /// Constants used in SendCommand.
        /// </summary>
        private const int TransportCommandsAV = 1;
        private const int TransportCommandsRender = 2;

        private const int Now = 1;
        private const int Never = 0;
        private const int Normal = -1;

        private static readonly CultureInfo _usCulture = new CultureInfo("en-US");

        private readonly IHttpClient _httpClient;
        private readonly ILogger _logger;
        private readonly PlayToManager _playToManager;
        private readonly object _timerLock = new object();
        private readonly object _queueLock = new object();

        /// <summary>
        /// Device's volume boundary values.
        /// </summary>
        private readonly ValueRange _volRange = new ValueRange();

        /// <summary>
        /// Holds the URL for the Jellyfin web server.
        /// </summary>
        private readonly string _jellyfinUrl;

        /// <summary>
        /// Outbound events processing queue.
        /// </summary>
        private readonly List<KeyValuePair<string, object>> _queue = new List<KeyValuePair<string, object>>();

        /// <summary>
        /// Host network response roundtime time.
        /// </summary>
        private TimeSpan _transportOffset = TimeSpan.Zero;

        /// <summary>
        /// Holds the current playback position.
        /// </summary>
        private TimeSpan _position = TimeSpan.Zero;

        private bool _disposed;
        private Timer? _timer;

        /// <summary>
        /// Connection failure retry counter.
        /// </summary>
        private int _connectFailureCount;

        /// <summary>
        /// Sound level prior to it being muted.
        /// </summary>
        private int _muteVol;

        /// <summary>
        /// True if this player is using subscription events.
        /// </summary>
        private bool _subscribed;

        /// <summary>
        /// Unique id used in subscription callbacks.
        /// </summary>
        private string? _sessionId;

        /// <summary>
        /// Transport service subscription SID value.
        /// </summary>
        private string? _transportSid;

        /// <summary>
        /// Render service subscription SID value.
        /// </summary>
        private string? _renderSid;

        /// <summary>
        /// Used by the volume control to stop DOS on volume queries.
        /// </summary>
        private int _volume;

        /// <summary>
        /// Hosts the last time we polled for the requests.
        /// </summary>
        private DateTime _lastVolumeRefresh;
        private DateTime _lastTransportRefresh;
        private DateTime _lastMetaRefresh;
        private DateTime _lastPositionRequest;

        /// <summary>
        /// Contains the item currently playing.
        /// </summary>
        private string _mediaPlaying = string.Empty;

        /// <summary>
        /// Initializes a new instance of the <see cref="Device"/> class.
        /// </summary>
        /// <param name="playToManager">Our playToManager<see cref="PlayToManager"/>.</param>
        /// <param name="deviceProperties">The deviceProperties<see cref="DeviceInfo"/>.</param>
        /// <param name="httpClient">Our httpClient<see cref="IHttpClient"/>.</param>
        /// <param name="logger">The logger<see cref="ILogger"/>.</param>
        /// <param name="webUrl">The webUrl.</param>
        public Device(PlayToManager playToManager, DeviceInfo deviceProperties, IHttpClient httpClient, ILogger logger, string webUrl)
        {
            Properties = deviceProperties;
            _httpClient = httpClient;
            _logger = logger;
            _jellyfinUrl = webUrl;
            _playToManager = playToManager;

            TransportState = TransportState.NO_MEDIA_PRESENT;
        }

        /// <summary>
        /// Events called when playback starts.
        /// </summary>
        public event EventHandler<PlaybackStartEventArgs>? PlaybackStart;

        /// <summary>
        /// Events called during playback.
        /// </summary>
        public event EventHandler<PlaybackProgressEventArgs>? PlaybackProgress;

        /// <summary>
        /// Events called when playback stops.
        /// </summary>
        public event EventHandler<PlaybackStoppedEventArgs>? PlaybackStopped;

        /// <summary>
        /// Events called when the media changes.
        /// </summary>
        public event EventHandler<MediaChangedEventArgs>? MediaChanged;

        /// <summary>
        /// Gets the device's properties.
        /// </summary>
        public DeviceInfo Properties { get; }

        /// <summary>
        /// Gets a value indicating whether the sound is muted.
        /// </summary>
        public bool IsMuted { get; private set; }

        /// <summary>
        /// Gets the current media information.
        /// </summary>
        public uBaseObject? CurrentMediaInfo { get; private set; }

        /// <summary>
        /// Gets or sets the Volume.
        /// </summary>
        public int Volume
        {
            get
            {
                if (!_subscribed)
                {
                    try
                    {
                        RefreshVolumeIfNeeded().GetAwaiter().GetResult();
                    }
                    catch (ObjectDisposedException)
                    {
                        // Ignore.
                    }
                    catch (HttpException ex)
                    {
                        _logger.LogError(ex, "{0} : Error getting device volume.", Properties.Name);
                    }
                }

                int calculateVolume = (int)Math.Round(100 / _volRange.Range * _volume);

                _logger.LogError("{0} : Returning a volume setting of {1}.", Properties.Name, calculateVolume);
                return calculateVolume;
            }

            set
            {
                if (value >= 0 && value <= 100)
                {
                    // Make ratio adjustments as not all devices have volume level 100. (User range => Device range.)
                    int newValue = (int)Math.Round(_volRange.Range / 100 * value);
                    if (newValue != _volume)
                    {
                        QueueEvent("SetVolume", _volume);
                    }
                }
            }
        }

        /// <summary>
        /// Gets the Duration.
        /// </summary>
        public TimeSpan? Duration { get; internal set; }

        /// <summary>
        /// Gets or sets the Position.
        /// </summary>
        public TimeSpan Position
        {
            get
            {
                return _position.Add(_transportOffset);
            }

            set
            {
                _position = value;
            }
        }

        /// <summary>
        /// Gets the TransportState.
        /// </summary>
        public TransportState TransportState { get; private set; }

        /// <summary>
        /// Gets a value indicating whether IsPlaying.
        /// </summary>
        public bool IsPlaying => TransportState == TransportState.PLAYING;

        /// <summary>
        /// Gets a value indicating whether IsPaused.
        /// </summary>
        public bool IsPaused => TransportState == TransportState.PAUSED || TransportState == TransportState.PAUSED_PLAYBACK;

        /// <summary>
        /// Gets a value indicating whether IsStopped.
        /// </summary>
        public bool IsStopped => TransportState == TransportState.STOPPED;

        /// <summary>
        /// Gets or sets the OnDeviceUnavailable.
        /// </summary>
        public Action? OnDeviceUnavailable { get; set; }

        /// <summary>
        /// Gets or sets the AvCommands.
        /// </summary>
        private TransportCommands? AvCommands { get; set; }

        /// <summary>
        /// Gets or sets the RendererCommands.
        /// </summary>
        private TransportCommands? RendererCommands { get; set; }

        /// <summary>
        /// The CreateuPnpDeviceAsync.
        /// </summary>
        /// <param name="playToManager">The playToManager<see cref="PlayToManager"/>.</param>
        /// <param name="url">The url<see cref="Uri"/>.</param>
        /// <param name="httpClient">The httpClient<see cref="IHttpClient"/>.</param>
        /// <param name="logger">The logger<see cref="ILogger"/>.</param>
        /// <param name="serverUrl">The serverUrl.</param>
        /// <returns>The <see cref="Task"/>.</returns>
        public static async Task<Device?> CreateuPnpDeviceAsync(
            PlayToManager playToManager,
            Uri url,
            IHttpClient httpClient,
            ILogger logger,
            string serverUrl)
        {
            if (url == null)
            {
                throw new ArgumentNullException(nameof(url));
            }

            if (httpClient == null)
            {
                throw new ArgumentNullException(nameof(httpClient));
            }

            try
            {
                var document = await GetDataAsync(httpClient, url.ToString(), logger).ConfigureAwait(false);
                var data = ParseResponse(document, "device");
                if (data == null)
                {
                    return null;
                }

                var friendlyNames = new List<string>();

                if (data.TryGetValue("friendlyName", out string value))
                {
                    friendlyNames.Add(value);
                }

                if (data.TryGetValue("roomName", out value))
                {
                    friendlyNames.Add(value);
                }

                var deviceProperties = new DeviceInfo()
                {
                    Name = string.Join(" ", friendlyNames),
                    BaseUrl = string.Format(CultureInfo.InvariantCulture, "http://{0}:{1}", url.Host, url.Port)
                };

#pragma warning disable CS8602 // Dereference of a possibly null reference : data is null, if document is null. Compiler doesn't pick this up.
                var icon = document.Descendants(uPnpNamespaces.ud.GetName("icon")).FirstOrDefault();
#pragma warning restore CS8602 // Dereference of a possibly null reference.
                if (icon != null)
                {
                    var width = icon.GetDescendantValue(uPnpNamespaces.ud.GetName("width"));
                    var height = icon.GetDescendantValue(uPnpNamespaces.ud.GetName("height"));
                    if (!int.TryParse(width, NumberStyles.Integer, _usCulture, out int widthValue))
                    {
                        logger.LogDebug("{0} : Unable to parse icon width {1}.", deviceProperties.Name, width);
                        widthValue = 32;
                    }

                    if (!int.TryParse(height, NumberStyles.Integer, _usCulture, out int heightValue))
                    {
                        logger.LogDebug("{0} : Unable to parse icon width {1}.", deviceProperties.Name, width);
                        heightValue = 32;
                    }

                    deviceProperties.Icon = new DeviceIcon
                    {
                        Depth = icon.GetDescendantValue(uPnpNamespaces.ud.GetName("depth")),
                        MimeType = icon.GetDescendantValue(uPnpNamespaces.ud.GetName("mimetype")),
                        Url = icon.GetDescendantValue(uPnpNamespaces.ud.GetName("url")),
                        Height = heightValue,
                        Width = widthValue
                    };
                }

                foreach (var services in document.Descendants(uPnpNamespaces.ud.GetName("serviceList")))
                {
                    if (services == null)
                    {
                        continue;
                    }

                    var servicesList = services.Descendants(uPnpNamespaces.ud.GetName("service"));
                    if (servicesList == null)
                    {
                        continue;
                    }

                    foreach (var element in servicesList)
                    {
                        var service = Create(element);

                        if (service != null)
                        {
                            deviceProperties.Services.Add(service);
                        }
                    }
                }

                if (data.TryGetValue("modelName", out value))
                {
                    deviceProperties.ModelName = value;
                }

                if (data.TryGetValue("modelNumber", out value))
                {
                    deviceProperties.ModelNumber = value;
                }

                if (data.TryGetValue("UDN", out value))
                {
                    deviceProperties.UUID = value;
                }

                if (data.TryGetValue("manufacturer", out value))
                {
                    deviceProperties.Manufacturer = value;
                }

                if (data.TryGetValue("manufacturerURL", out value))
                {
                    deviceProperties.ManufacturerUrl = value;
                }

                if (data.TryGetValue("presentationURL", out value))
                {
                    deviceProperties.PresentationUrl = value;
                }

                if (data.TryGetValue("modelURL", out value))
                {
                    deviceProperties.ModelUrl = value;
                }

                if (data.TryGetValue("serialNumber", out value))
                {
                    deviceProperties.SerialNumber = value;
                }

                if (data.TryGetValue("modelDescription", out value))
                {
                    deviceProperties.ModelDescription = value;
                }

                return new Device(playToManager, deviceProperties, httpClient, logger, serverUrl);
            }
#pragma warning disable CA1031 // Do not catch general exception types : Don't let our errors affect our owners.
            catch
#pragma warning restore CA1031 // Do not catch general exception types
            {
                return null;
            }
        }

        /// <summary>
        /// Starts the monitoring of the device.
        /// </summary>
        /// <returns>Task.</returns>
        public async Task DeviceInitialise()
        {
            if (_timer == null)
            {
                _lastVolumeRefresh = DateTime.UtcNow;
                _lastPositionRequest = DateTime.UtcNow.AddSeconds(-5);
                _lastTransportRefresh = _lastPositionRequest;
                _lastMetaRefresh = _lastPositionRequest;

                // Make sure that the device doesn't have a range on the volume controls.
                try
                {
                    await GetStateVariableRange(GetServiceRenderingControl(), "Volume").ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (HttpException)
                {
                    // Ignore.
                }

                try
                {
                    await SubscribeAsync().ConfigureAwait(false);
                    // Update the position, volume and subscript for events.
                    await GetPositionRequest().ConfigureAwait(false);
                    await GetVolume().ConfigureAwait(false);
                    await GetMute().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (HttpException)
                {
                    // Ignore.
                }

                _logger.LogDebug("{0} : Starting timer.", Properties.Name);
                _timer = new Timer(TimerCallback, null, 500, Timeout.Infinite);

                // Start the user command queue processor.
                await ProcessQueue().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Called when the device becomes unavailable.
        /// </summary>
        /// <returns>Task.</returns>
        public async Task DeviceUnavailable()
        {
            if (_subscribed)
            {
                _logger.LogDebug("{0} : Killing the timer.", Properties.Name);

                await UnSubscribeAsync().ConfigureAwait(false);

                lock (_timerLock)
                {
                    _timer?.Dispose();
                    _timer = null;
                }
            }
        }

        /// <summary>
        /// Decreases the volume.
        /// </summary>
        /// <returns>Task.</returns>
        public Task VolumeDown()
        {
            QueueEvent("SetVolume", Math.Max(_volume - _volRange.FivePoints, _volRange.Min));
            return Task.CompletedTask;
        }

        /// <summary>
        /// Increases the volume.
        /// </summary>
        /// <returns>Task.</returns>
        public Task VolumeUp()
        {
            QueueEvent("SetVolume", Math.Min(_volume + _volRange.FivePoints, _volRange.Max));
            return Task.CompletedTask;
        }

        /// <summary>
        /// Toggles Mute.
        /// </summary>
        /// <returns>Task.</returns>
        public Task ToggleMute()
        {
            AddOrCancelIfQueued("ToggleMute");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Starts playback.
        /// </summary>
        /// <returns>Task.</returns>
        public Task Play()
        {
            QueueEvent("Play");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Stops playback.
        /// </summary>
        /// <returns>Task.</returns>
        public Task Stop()
        {
            QueueEvent("Stop");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Pauses playback.
        /// </summary>
        /// <returns>Task.</returns>
        public Task Pause()
        {
            QueueEvent("Pause");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Mutes the sound.
        /// </summary>
        /// <returns>Task.</returns>
        public Task Mute()
        {
            QueueEvent("Mute");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Resumes the sound.
        /// </summary>
        /// <returns>Task.</returns>
        public Task Unmute()
        {
            QueueEvent("Unmute");
            return Task.CompletedTask;
        }

        /// <summary>
        /// Moves playback to a specific point.
        /// </summary>
        /// <param name="value">The point at which playback will resume.</param>
        /// <returns>Task.</returns>
        public Task Seek(TimeSpan value)
        {
            QueueEvent("Seek", value);
            return Task.CompletedTask;
        }

        /// <summary>
        /// Specifies new media to play.
        /// </summary>
        /// <param name="url">Url of media.</param>
        /// <param name="headers">Headers.</param>
        /// <param name="metadata">Media metadata.</param>
        /// <returns>Task.</returns>
        public Task SetAvTransport(string url, string headers, string metadata)
        {
            QueueEvent("Queue", new MediaData { Url = url, Headers = headers, Metadata = metadata });
            return Task.CompletedTask;
        }

        /// <summary>
        /// Disposes this object.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Returns a string representation of this object.
        /// </summary>
        /// <returns>The .</returns>
        public override string ToString()
        {
            return string.Format(CultureInfo.InvariantCulture, "{0} - {1}", Properties.Name, Properties.BaseUrl);
        }

        /// <summary>
        /// Diposes this object.
        /// </summary>
        /// <param name="disposing">The disposing<see cref="bool"/>.</param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            if (disposing)
            {
                _timer?.Dispose();

                if (_playToManager != null)
                {
                    _playToManager.DLNAEvents -= ProcessSubscriptionEvent;
                }
            }

            _disposed = true;
        }

        /// <summary>
        /// Parses a response into a dictionary, taking care of NOT IMPLEMENTED and null responses.
        /// </summary>
        /// <param name="document">Response to parse.</param>
        /// <param name="action">Action to extract.</param>
        /// <returns>Dictionary contains the arguments and values.</returns>
        private static Dictionary<string, string> ParseResponse(XDocument? document, string action)
        {
            Dictionary<string, string> result = new Dictionary<string, string>();

            if (document != null)
            {
                var nodes = document.Descendants()
                    .Where(p => p.Name.LocalName == action);
                if (nodes != null)
                {
                    foreach (var node in nodes)
                    {
                        if (node.HasElements)
                        {
                            foreach (var childNode in node.Elements())
                            {
                                string? value = childNode.Value;
                                if (string.IsNullOrWhiteSpace(value))
                                {
                                    // Some responses are stores in the val property, and not in the element.
                                    value = childNode.Attribute("val")?.Value;
                                }

                                if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "NOT_IMPLEMENTED", StringComparison.OrdinalIgnoreCase))
                                {
                                    value = string.Empty;
                                }

                                result.Add(childNode.Name.LocalName, value);
                            }
                        }
                        else
                        {
                            string value = node.Value;

                            if (string.IsNullOrWhiteSpace(value) && string.Equals(value, "NOT_IMPLEMENTED", StringComparison.OrdinalIgnoreCase))
                            {
                                value = string.Empty;
                            }

                            result.Add(node.Name.LocalName, value);
                        }
                    }
                }
            }

            return result;
        }

        private static XElement? ParseNodeResponse(string xml)
        {
            // Handle different variations sent back by devices.
            try
            {
                return XElement.Parse(xml);
            }
            catch (XmlException)
            {
                // Wasn't this flavour.
            }

            // First try to add a root node with a dlna namespace.
            try
            {
                return XElement.Parse("<data xmlns:dlna=\"urn:schemas-dlna-org:device-1-0\">" + xml + "</data>")
                                .Descendants()
                                .First();
            }
            catch (XmlException)
            {
                // Wasn't this flavour.
            }

            // Some devices send back invalid XML.
            try
            {
                return XElement.Parse(xml.Replace("&", "&amp;", StringComparison.OrdinalIgnoreCase));
            }
            catch (XmlException)
            {
                // Wasn't this flavour.
            }

            return null;
        }

        private static string NormalizeUrl(string baseUrl, string url, bool dmr = false)
        {
            // If it's already a complete url, don't stick anything onto the front of it
            if (url.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            if (dmr && !url.Contains("/", StringComparison.OrdinalIgnoreCase))
            {
                url = "/dmr/" + url;
            }
            else if (!url.StartsWith("/", StringComparison.Ordinal))
            {
                url = "/" + url;
            }

            return baseUrl + url;
        }

        /// <summary>
        /// Creates a uBaseObject from the information provided.
        /// </summary>
        /// <param name="container">The container.</param>
        /// <param name="trackUri">The trackUri.</param>
        /// <returns>The <see cref="uBaseObject"/>.</returns>
        private static uBaseObject CreateUBaseObject(XElement? container, string trackUri)
        {
            if (container == null)
            {
                throw new ArgumentNullException(nameof(container));
            }

            var url = container.GetValue(uPnpNamespaces.Res);

            if (string.IsNullOrWhiteSpace(url))
            {
                url = trackUri;
            }

            var resElement = container.Element(uPnpNamespaces.Res);
            var protocolInfo = new string[4];

            if (resElement != null)
            {
                var info = resElement.Attribute(uPnpNamespaces.ProtocolInfo);

                if (info != null && !string.IsNullOrWhiteSpace(info.Value))
                {
                    protocolInfo = info.Value.Split(':');
                }
            }

            return new uBaseObject
            {
                Id = container.GetAttributeValue(uPnpNamespaces.Id),
                ParentId = container.GetAttributeValue(uPnpNamespaces.ParentId),
                Title = container.GetValue(uPnpNamespaces.title),
                IconUrl = container.GetValue(uPnpNamespaces.Artwork),
                SecondText = string.Empty,
                Url = url,
                ProtocolInfo = protocolInfo,
                MetaData = container.ToString()
            };
        }

        /// <summary>
        /// Creates a DeviceService from an XML element.
        /// </summary>
        /// <param name="element">The element.</param>
        /// <returns>The <see cref="DeviceService"/>.</returns>
        private static DeviceService Create(XElement element)
        {
            var type = element.GetDescendantValue(uPnpNamespaces.ud.GetName("serviceType"));
            var id = element.GetDescendantValue(uPnpNamespaces.ud.GetName("serviceId"));
            var scpdUrl = element.GetDescendantValue(uPnpNamespaces.ud.GetName("SCPDURL"));
            var controlURL = element.GetDescendantValue(uPnpNamespaces.ud.GetName("controlURL"));
            var eventSubURL = element.GetDescendantValue(uPnpNamespaces.ud.GetName("eventSubURL"));

            return new DeviceService
            {
                ControlUrl = controlURL,
                EventSubUrl = eventSubURL,
                ScpdUrl = scpdUrl,
                ServiceId = id,
                ServiceType = type
            };
        }

        /// <summary>
        /// Gets service information from the DLNA clients.
        /// </summary>
        /// <param name="httpClient">The HttpClient to use. <see cref="IHttpClient"/>.</param>
        /// <param name="url">The destination URL..</param>
        /// <param name="logger">The logger to use.<see cref="ILogger"/>.</param>
        /// <returns>The <see cref="Task{XDocument}"/>.</returns>
        private static async Task<XDocument?> GetDataAsync(IHttpClient httpClient, string url, ILogger logger)
        {
            var options = new HttpRequestOptions
            {
                Url = url,
                UserAgent = USERAGENT,
                LogErrorResponseBody = true,
                BufferContent = false,
                CancellationToken = default
            };

            options.RequestHeaders["FriendlyName.DLNA.ORG"] = FriendlyName;
            try
            {
                logger?.LogDebug("GetDataAsync: Communicating with {0}", url);
                using var response = await httpClient.SendAsync(options, HttpMethod.Get).ConfigureAwait(false);
                using var stream = response.Content;
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return XDocument.Parse(
                    await reader.ReadToEndAsync().ConfigureAwait(false),
                    LoadOptions.PreserveWhitespace);
            }
            catch (HttpRequestException ex)
            {
                logger?.LogDebug("GetDataAsync: Failed with {0}", ex.Message);
                throw;
            }
            catch (Exception ex)
            {
                // Show stack trace on other errors.
                logger?.LogDebug(ex, "GetDataAsync: Failed.");
                throw;
            }
        }

        /// <summary>
        /// Enables the volume bar hovered over effect.
        /// </summary>
        /// <returns>Task.</returns>
        private async Task RefreshVolumeIfNeeded()
        {
            try
            {
                await GetVolume().ConfigureAwait(false);
                await GetMute().ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                // Ignore.
            }
        }

        /// <summary>
        /// Restart the polling timer.
        /// </summary>
        /// <param name="when">When to restart the timer. Less than 0 = never, 0 = instantly, greater than 0 in 1 second.</param>
        private void RestartTimer(int when)
        {
            lock (_timerLock)
            {
                if (_disposed)
                {
                    return;
                }

                int delay = when == Never ? Timeout.Infinite : when == Now ? 100 : 10000;
                _timer?.Change(delay, Timeout.Infinite);
            }
        }

        /// <summary>
        /// Adds an event to the user control queue.
        /// Later identical events overwrite earlier ones.
        /// </summary>
        /// <param name="command">Command to queue.</param>
        /// <param name="value">Command parameter.</param>
        private void QueueEvent(string command, object? value = null)
        {
            lock (_queueLock)
            {
                // Does this action exist in the queue ?
                int index = _queue.FindIndex(item => string.Equals(item.Key, command, StringComparison.OrdinalIgnoreCase));

                if (index != -1)
                {
                    _logger.LogDebug("{0} : Replacing user event: {1} {2}", Properties.Name, command, value);
                    _queue.RemoveAt(index);
                }
                else
                {
                    _logger.LogDebug("{0} : Queuing user event: {1} {2}", Properties.Name, command, value);
                }

                _queue.Add(new KeyValuePair<string, object>(command, value ?? 0));
            }
        }

        /// <summary>
        /// Removes an event from the queue if it exists, or adds it if it doesn't.
        /// </summary>
        private void AddOrCancelIfQueued(string command)
        {
            lock (_queueLock)
            {
                int index = _queue.FindIndex(item => string.Equals(item.Key, command, StringComparison.OrdinalIgnoreCase));

                if (index != -1)
                {
                    _logger.LogDebug("{0} : Cancelling user event: {1}", Properties.Name, command);
                    _queue.RemoveAt(index);
                }
                else
                {
                    _logger.LogDebug("{0} : Queuing user event: {1}", Properties.Name, command);
                    _queue.Add(new KeyValuePair<string, object>(command, 0));
                }
            }
        }

        private async Task ProcessQueue()
        {
            // Infinite loop until dispose.
            while (!_disposed)
            {
                // Process items in the queue.
                while (_queue.Count > 0)
                {
                    // Ensure we are still subscribed.
                    await SubscribeAsync().ConfigureAwait(false);

                    KeyValuePair<string, object> action;

                    lock (_queueLock)
                    {
                        action = _queue[0];
                        _queue.RemoveAt(0);
                    }

                    try
                    {
                        _logger.LogDebug("{0} : Attempting action : {1}", Properties.Name, action.Key);

                        switch (action.Key)
                        {
                            case "SetVolume":
                                {
                                    await SendVolumeRequest((int)action.Value).ConfigureAwait(false);

                                    break;
                                }

                            case "ToggleMute":
                                {
                                    if ((int)action.Value == 1)
                                    {
                                        var success = await SendMuteRequest(false).ConfigureAwait(true);
                                        if (!success)
                                        {
                                            var sendVolume = _muteVol <= 0 ?
                                                (int)Math.Round((double)(_volRange.Max - _volRange.Min) / 100 * 20) // 20% of maximum.
                                                : _muteVol;
                                            await SendVolumeRequest(sendVolume).ConfigureAwait(false);
                                        }
                                    }
                                    else
                                    {
                                        var success = await SendMuteRequest(true).ConfigureAwait(true);
                                        if (!success)
                                        {
                                            await SendVolumeRequest(0).ConfigureAwait(false);
                                        }
                                    }

                                    break;
                                }

                            case "Play":
                                {
                                    await SendPlayRequest().ConfigureAwait(false);
                                    break;
                                }

                            case "Stop":
                                {
                                    await SendStopRequest().ConfigureAwait(false);
                                    break;
                                }

                            case "Pause":
                                {
                                    await SendPauseRequest().ConfigureAwait(false);
                                    break;
                                }

                            case "Mute":
                                {
                                    var success = await SendMuteRequest(true).ConfigureAwait(true);
                                    if (!success)
                                    {
                                        await SendVolumeRequest(0).ConfigureAwait(false);
                                    }
                                }

                                break;

                            case "Unmute":
                                {
                                    var success = await SendMuteRequest(false).ConfigureAwait(true);
                                    if (!success)
                                    {
                                        var sendVolume = _muteVol <= 0 ?
                                                (int)Math.Round((double)(_volRange.Max - _volRange.Min) / 100 * 20) // 20% of maximum.
                                                : _muteVol;
                                        await SendVolumeRequest(sendVolume).ConfigureAwait(false);
                                    }

                                    break;
                                }

                            case "Seek":
                                {
                                    await SendSeekRequest((TimeSpan)action.Value).ConfigureAwait(false);
                                    break;
                                }

                            case "Queue":
                                {
                                    var settings = (MediaData)action.Value;
                                    bool success = true;

                                    if (IsPlaying)
                                    {
                                        // Has user requested a media change?
                                        if (_mediaPlaying != settings.Url)
                                        {
                                            _logger.LogDebug("{0} : Stopping current playback for transition.", Properties.Name);
                                            success = await SendStopRequest().ConfigureAwait(false);

                                            if (success)
                                            {
                                                // Save current progress.
                                                TransportState = TransportState.TRANSITIONING;
                                                UpdateMediaInfo(null);
                                            }

                                            success = true; // Attempt to load up next track. May work on some systems.
                                        }
                                        else
                                        {
                                            // Restart from the beginning.
                                            _logger.LogDebug("{0} : Resetting playback position.", Properties.Name);
                                            success = await SendSeekRequest(TimeSpan.Zero).ConfigureAwait(false);
                                            if (success)
                                            {
                                                // Save progress and restart time.
                                                UpdateMediaInfo(CurrentMediaInfo);
                                                RestartTimer(Normal);
                                                // We're finished. Nothing further to do.
                                                break;
                                            }
                                        }
                                    }

                                    if (success)
                                    {
                                        await SendMediaRequest(settings).ConfigureAwait(false);
                                    }

                                    break;
                                }
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }
                    catch (HttpException)
                    {
                        // Ignore.
                    }
                }

                await Task.Delay(1000).ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Sends a command to the DLNA device.
        /// </summary>
        /// <param name="baseUrl">baseUrl to use..</param>
        /// <param name="service">Service to use.<see cref="DeviceService"/>.</param>
        /// <param name="command">Command to send..</param>
        /// <param name="postData">Information to post..</param>
        /// <param name="header">Headers to include..</param>
        /// <param name="cancellationToken">The cancellationToken.</param>
        /// <returns>The <see cref="Task"/>.</returns>
        private async Task<XDocument?> SendCommandAsync(
            string baseUrl,
            DeviceService service,
            string command,
            string postData,
            string? header = null,
            CancellationToken cancellationToken = default)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(string.Empty);
            }

            var url = NormalizeUrl(baseUrl, service.ControlUrl);
            try
            {
                var options = new HttpRequestOptions
                {
                    Url = url,
                    UserAgent = USERAGENT,
                    LogErrorResponseBody = true,
                    BufferContent = false,
                    CancellationToken = cancellationToken
                };

                options.RequestHeaders["SOAPAction"] = $"\"{service.ServiceType}#{command}\"";
                options.RequestHeaders["Pragma"] = "no-cache";
                options.RequestHeaders["FriendlyName.DLNA.ORG"] = FriendlyName;

                if (!string.IsNullOrEmpty(header))
                {
                    options.RequestHeaders["contentFeatures.dlna.org"] = header;
                }

                options.RequestContentType = "text/xml";
                options.RequestContent = postData;

                var stopWatch = new Stopwatch();
                stopWatch.Start();
                HttpResponseInfo response = await _httpClient.Post(options).ConfigureAwait(true);

                // Get the response.
                using var stream = response.Content;
                using var reader = new StreamReader(stream, Encoding.UTF8);
                var result = XDocument.Parse(await reader.ReadToEndAsync().ConfigureAwait(false), LoadOptions.PreserveWhitespace);
                stopWatch.Stop();

                // Calculate just under half of the round trip time so we can make the position slide more accurate.
                _transportOffset = stopWatch.Elapsed.Divide(1.8);

                return result;
            }
            catch (HttpException ex)
            {
                _logger.LogError("{0} : SendCommandAsync failed with {1} to {2} ", Properties.Name, ex.ToString(), url);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError("{0} : SendCommandAsync failed with {1} to {2} ", Properties.Name, ex.ToString(), url);
            }

            return null;
        }

        /// <summary>
        /// Sends a command to the device and waits for a response.
        /// </summary>
        /// <param name="transportCommandsType">The transport type.</param>
        /// <param name="actionCommand">The actionCommand.</param>
        /// <param name="name">The name.</param>
        /// <param name="commandParameter">The commandParameter.</param>
        /// <param name="dictionary">The dictionary.</param>
        /// <param name="header">The header.</param>
        /// <returns>The <see cref="Task{XDocument}"/>.</returns>
        private async Task<XDocument?> SendCommandResponseRequired(
            int transportCommandsType,
            string actionCommand,
            object? name = null,
            string? commandParameter = null,
            Dictionary<string, string>? dictionary = null,
            string? header = null)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(string.Empty);
            }

            TransportCommands? commands;
            ServiceAction? command;
            DeviceService? service;
            string? postData = string.Empty;

            if (transportCommandsType == TransportCommandsRender)
            {
                service = GetServiceRenderingControl();

                RendererCommands ??= await GetProtocolAsync(service).ConfigureAwait(false);
                if (RendererCommands == null)
                {
                    _logger.LogError("{0} : GetRenderingProtocolAsync returned null.", Properties.Name);
                    return null;
                }

                command = RendererCommands.ServiceActions.FirstOrDefault(c => c.Name == actionCommand);
                if (service == null || command == null)
                {
                    _logger.LogError("{0} : Command or service returned null.", Properties.Name);
                    return null;
                }

                commands = RendererCommands;
            }
            else
            {
                service = GetAvTransportService();
                AvCommands ??= await GetProtocolAsync(service).ConfigureAwait(false);
                if (AvCommands == null)
                {
                    _logger.LogError("{0} : GetAVProtocolAsync returned null.", Properties.Name);
                    return null;
                }

                command = AvCommands.ServiceActions.FirstOrDefault(c => c.Name == actionCommand);
                if (service == null || command == null)
                {
                    _logger.LogWarning("Command or service returned null.");
                    return null;
                }

                commands = AvCommands;
            }

            if (commandParameter != null)
            {
                postData = commands.BuildPost(command, service.ServiceType, name, commandParameter);
            }
            else if (dictionary != null)
            {
                postData = commands.BuildPost(command, service.ServiceType, name, dictionary);
            }
            else if (name != null)
            {
                postData = commands.BuildPost(command, service.ServiceType, name);
            }
            else
            {
                postData = commands.BuildPost(command, service.ServiceType);
            }

            return await SendCommandAsync(Properties.BaseUrl, service, command.Name, postData, header).ConfigureAwait(false);
        }

        /// <summary>
        /// Sends a command to the device, verifies receipt, and does return a response.
        /// </summary>
        /// <param name="transportCommandsType">The transport commands type.</param>
        /// <param name="actionCommand">The action command to use.</param>
        /// <param name="name">The name.</param>
        /// <param name="commandParameter">The command parameter.</param>
        /// <param name="dictionary">The dictionary.</param>
        /// <param name="header">The header.</param>
        /// <returns>Returns success of the task.</returns>
        private async Task<bool> SendCommand(
            int transportCommandsType,
            string actionCommand,
            object? name = null,
            string? commandParameter = null,
            Dictionary<string, string>? dictionary = null,
            string? header = null)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(string.Empty);
            }

            var result = await SendCommandResponseRequired(transportCommandsType, actionCommand, name, commandParameter, dictionary, header).ConfigureAwait(false);
            var response = ParseResponse(result?.Document, actionCommand + "Response");

            if (response.TryGetValue(actionCommand + "Response", out string _))
            {
                return true;
            }

            _logger.LogWarning("{0} Sending {1} Failed!", Properties.Name, actionCommand);
            return false;
        }

        /// <summary>
        /// Retrieves the DNLA device description and parses the state variable info.
        /// </summary>
        /// <param name="renderService">Service to use.</param>
        /// <param name="wanted">State variable to return.</param>
        private async Task GetStateVariableRange(DeviceService renderService, string wanted)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(string.Empty);
            }

            try
            {
                var response = await _httpClient.Get(new HttpRequestOptions
                {
                    Url = NormalizeUrl(Properties.BaseUrl, renderService.ScpdUrl),
                    BufferContent = false
                }).ConfigureAwait(false);

                using var reader = new StreamReader(response, Encoding.UTF8);
                {
                    string xmlstring = await reader.ReadToEndAsync().ConfigureAwait(false);

                    // Use xpath to get to /stateVariable/allowedValueRange/minimum|maximum where /stateVariable/Name == wanted.
                    var dlnaDescripton = new XmlDocument();
                    dlnaDescripton.LoadXml(xmlstring);
                    XmlNamespaceManager xmlns = new XmlNamespaceManager(dlnaDescripton.NameTable);
                    xmlns.AddNamespace("ns", "urn:schemas-upnp-org:service-1-0");

                    XmlNode? minimum = dlnaDescripton.SelectSingleNode(
                        "//ns:stateVariable[ns:name/text()='"
                        + wanted
                        + "']/ns:allowedValueRange/ns:minimum/text()", xmlns);

                    XmlNode? maximum = dlnaDescripton.SelectSingleNode(
                        "//ns:stateVariable[ns:name/text()='"
                        + wanted
                        + "']/ns:allowedValueRange/ns:maximum/text()", xmlns);

                    // Populate the return value with what we have. Don't worry about null values.
                    if (minimum.Value != null && maximum.Value != null)
                    {
                        _volRange.Min = int.Parse(minimum.Value, _usCulture);
                        _volRange.Max = int.Parse(maximum.Value, _usCulture);
                        _volRange.Range = _volRange.Max - _volRange.Min;
                        _volRange.FivePoints = (int)Math.Round(_volRange.Range / 100 * 5);
                    }
                }
            }
            catch (XmlException)
            {
                _logger.LogError("{0} : Badly formed description document received XML", Properties.Name);
            }
            catch (HttpRequestException)
            {
                _logger.LogError("{0} : Unable to retrieve ssdp description.", Properties.Name);
            }
        }

        /// <summary>
        /// Checks to see if DLNA subscriptions are implemented, and if so subscribes to changes.
        /// </summary>
        /// <param name="service">The service<see cref="DeviceService"/>.</param>
        /// <param name="sid">The SID for renewal, or null for subscription.</param>
        /// <returns>Task.</returns>
        private async Task<string> SubscribeInternalAsync(DeviceService service, string? sid)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(string.Empty);
            }

            if (service.EventSubUrl != null)
            {
                var options = new HttpRequestOptions
                {
                    Url = NormalizeUrl(Properties.BaseUrl, service.EventSubUrl),
                    UserAgent = USERAGENT,
                    LogErrorResponseBody = true,
                    BufferContent = false,
                };

                // Renewal or subscription?
                if (string.IsNullOrEmpty(sid))
                {
                    if (string.IsNullOrEmpty(_sessionId))
                    {
                        // If we haven't got a GUID yet - get one.
                        _sessionId = Guid.NewGuid().ToString();
                    }

                    // Create a unique callback url based up our GUID.
                    options.RequestHeaders["CALLBACK"] = $"<{_jellyfinUrl}/Dlna/Eventing/{_sessionId}>";
                }
                else
                {
                    // Resubscription id.
                    options.RequestHeaders["SID"] = "uuid:{sid}";
                }

                options.RequestHeaders["NT"] = "upnp:event";
                options.RequestHeaders["TIMEOUT"] = "Second-60";
                // TODO: check what happens at timeout.

                try
                {
                    // Send the subscription message to the client.
                    using var response = await _httpClient.SendAsync(options, new HttpMethod("SUBSCRIBE")).ConfigureAwait(false);
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        if (!_subscribed)
                        {
                            return response.Headers.GetValues("SID").FirstOrDefault();
                        }
                    }
                }
                catch (HttpException ex)
                {
                    _logger.LogError(ex, "{0} : SUBSCRIBE failed.", Properties.Name);
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Attempts to subscribe to the multiple services of a DLNA client.
        /// </summary>
        /// <returns>The <see cref="Task"/>.</returns>
        private async Task SubscribeAsync()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(string.Empty);
            }

            if (!_subscribed)
            {
                try
                {
                    // Start listening to DLNA events that come via the url through the PlayToManger.
                    _playToManager.DLNAEvents += ProcessSubscriptionEvent;

                    // Subscribe to both AvTransport and RenderControl events.
                    _transportSid = await SubscribeInternalAsync(GetAvTransportService(), _transportSid).ConfigureAwait(false);
                    _logger.LogDebug("AVTransport SID {0}.", _transportSid);

                    _renderSid = await SubscribeInternalAsync(GetServiceRenderingControl(), _renderSid).ConfigureAwait(false);
                    _logger.LogDebug("RenderControl SID {0}.", _renderSid);

                    _subscribed = true;
                }
                catch (ObjectDisposedException)
                {
                    // Ignore.
                }
            }
        }

        /// <summary>
        /// Resubscribe to DLNA evemts.
        /// Use in the event trigger, as an async wrapper.
        /// </summary>
        /// <returns>The <see cref="Task"/>.</returns>
        private async Task ResubscribeToEvents()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(string.Empty);
            }

            await SubscribeAsync().ConfigureAwait(false);
        }

        /// <summary>
        /// Attempts to unsubscribe from a DLNA client.
        /// </summary>
        /// <param name="service">The service<see cref="DeviceService"/>.</param>
        /// <param name="sid">The sid.</param>
        /// <returns>Returns success of the task.</returns>
        private async Task<bool> UnSubscribeInternalAsync(DeviceService service, string? sid)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(string.Empty);
            }

            if (service?.EventSubUrl != null || string.IsNullOrEmpty(sid))
            {
                var options = new HttpRequestOptions
                {
#pragma warning disable CS8602 // Dereference of a possibly null reference. Erroneous warning: service cannot be null here.
                    Url = NormalizeUrl(Properties.BaseUrl, service.EventSubUrl),
#pragma warning restore CS8602 // Dereference of a possibly null reference.
                    UserAgent = USERAGENT,
                    LogErrorResponseBody = true,
                    BufferContent = false,
                };

                options.RequestHeaders["SID"] = "uuid: {sid}";
                try
                {
                    using var response = await _httpClient.SendAsync(options, new HttpMethod("UNSUBSCRIBE")).ConfigureAwait(false);
                    if (response.StatusCode == HttpStatusCode.OK)
                    {
                        _logger.LogDebug("UNSUBSCRIBE succeeded.");
                        return true;
                    }
                }
                catch (HttpException ex)
                {
                    _logger.LogError(ex, "UNSUBSCRIBE failed.");
                }
            }

            return false;
        }

        /// <summary>
        /// Attempts to unsubscribe from the multiple services of the DLNA client.
        /// </summary>
        /// <returns>The <see cref="Task"/>.</returns>
        private async Task UnSubscribeAsync()
        {
            if (_subscribed)
            {
                try
                {
                    // stop processing events.
                    _playToManager.DLNAEvents -= ProcessSubscriptionEvent;

                    var success = await UnSubscribeInternalAsync(GetAvTransportService(), _transportSid).ConfigureAwait(false);
                    if (success)
                    {
                        // Keep Sid in case the user interacts with this device.
                        _transportSid = string.Empty;
                    }

                    success = await UnSubscribeInternalAsync(GetServiceRenderingControl(), _renderSid).ConfigureAwait(false);
                    if (success)
                    {
                        _renderSid = string.Empty;
                    }

                    // TODO: should we attempt to unsubscribe again if they fail?

                    _subscribed = false;
                }
                catch (ObjectDisposedException)
                {
                    // Ignore.
                }
            }
        }

        /// <summary>
        /// This method gets called with the information the DLNA clients have passed through eventing.
        /// </summary>
        /// <param name="sender">PlayToController object.</param>
        /// <param name="args">Arguments passed from DLNA player.</param>
        private async void ProcessSubscriptionEvent(object sender, DlnaEventArgs args)
        {
            if (args.Id == _sessionId)
            {
                try
                {
                    XDocument response;
                    try
                    {
                        response = XDocument.Parse(args.Response);
                    }
                    catch (XmlException ex)
                    {
                        _logger.LogWarning("{0} : {1} : {2}", Properties.Name, ex.Message, args.Response);
                        return;
                    }

                    if (response != null)
                    {
                        var reply = ParseResponse(response, "InstanceID");
                        _logger.LogDebug("{0} : Processing a subscription event.", Properties.Name);

                        if (!reply.TryGetValue("AVTransportURI", out string value))
                        {
                            // Render events.
                            if (reply.TryGetValue("Mute", out value)
                                && int.TryParse(value, out int mute))
                            {
                                _logger.LogDebug("Muted: {0}", mute);
                                IsMuted = mute == 1;
                            }

                            if (reply.TryGetValue("Volume", out value)
                                && int.TryParse(value, out int volume))
                            {
                                _logger.LogDebug("Volume: {0}", volume);
                                _volume = volume;
                            }

                            if (reply.TryGetValue("TransportState", out value)
                                && Enum.TryParse(value, true, out TransportState ts))
                            {
                                _logger.LogDebug("{0} : TransportState: {1}", Properties.Name, ts);

                                // Mustn't process our own change playback event.
                                if (ts != TransportState && TransportState != TransportState.TRANSITIONING)
                                {
                                    TransportState = ts;

                                    if (ts == TransportState.STOPPED)
                                    {
                                        _lastTransportRefresh = DateTime.UtcNow;
                                        UpdateMediaInfo(null);
                                        RestartTimer(Normal);
                                    }
                                }
                            }
                        }
                        else // AVTransport events.
                        {
                            // If the position isn't in this update, try to get it.
                            if (!reply.TryGetValue("RelativeTimePosition", out value))
                            {
                                _logger.LogDebug("{0} : Updating position as not included.", Properties.Name);
                                // Try and get the latest position update
                                await GetPositionRequest().ConfigureAwait(false);
                            }
                            else if (TimeSpan.TryParse(value, _usCulture, out TimeSpan rel))
                            {
                                _logger.LogDebug("RelativeTimePosition: {0}", rel);
                                Position = rel;
                                _lastPositionRequest = DateTime.UtcNow;
                            }

                            if (reply.TryGetValue("CurrentTrackDuration", out value)
                                && TimeSpan.TryParse(value, _usCulture, out TimeSpan dur))
                            {
                                _logger.LogDebug("CurrentTrackDuration: {0}", dur);
                                Duration = dur;
                            }

                            // See if we can update out media.
                            if (reply.TryGetValue("CurrentTrackMetaData", out value)
                                && !string.IsNullOrEmpty(value))
                            {
                                XElement? uPnpResponse = ParseNodeResponse(System.Web.HttpUtility.HtmlDecode(value));
                                var e = uPnpResponse?.Element(uPnpNamespaces.items);

                                if (reply.TryGetValue("CurrentTrackURI", out value)
                                    && !string.IsNullOrEmpty(value))
                                {
                                    uBaseObject? uTrack = CreateUBaseObject(e, System.Web.HttpUtility.HtmlDecode(value));

                                    if (uTrack != null)
                                    {
                                        _lastMetaRefresh = DateTime.UtcNow;
                                        UpdateMediaInfo(uTrack);
                                        RestartTimer(Normal);
                                    }
                                }
                            }
                        }

                        _ = ResubscribeToEvents();
                    }
                    else
                    {
                        _logger.LogDebug("{0} : Received blank event data : ", Properties.Name);
                    }
                }
                catch (ObjectDisposedException)
                {
                    // Ignore.
                }
                catch (HttpException ex)
                {
                    _logger.LogError("{0} : Unable to parse event response.", Properties.Name);
                    _logger.LogDebug(ex, "{0} : Received: ", Properties.Name, args.Response);
                }
            }
        }

        /// <summary>
        /// Timer Callback function that polls the DLNA status.
        /// </summary>
        /// <param name="sender">The sender.</param>
        private async void TimerCallback(object sender)
        {
            if (_disposed)
            {
                return;
            }

            _logger.LogDebug("{0} : Timer firing.", Properties.Name);
            try
            {
                var transportState = await GetTransportStatus().ConfigureAwait(false);

                if (transportState == TransportState.ERROR)
                {
                    _logger.LogError("{0} : Unable to get TransportState.", Properties.Name);
                    // Assume it's a one off.
                    RestartTimer(Normal);
                }
                else
                {
                    TransportState = transportState;

                    if (transportState != TransportState.ERROR)
                    {
                        // If we're not playing anything make sure we don't get data more
                        // often than neccessary to keep the Session alive.
                        if (transportState == TransportState.STOPPED)
                        {
                            UpdateMediaInfo(null);
                            RestartTimer(Never);
                        }
                        else
                        {
                            var result = await SendCommandResponseRequired(
                                TransportCommandsAV,
                                "GetPositionInfo").ConfigureAwait(false);
                            var response = ParseResponse(result?.Document, "GetPositionInfoResponse");
                            if (response.Count == 0)
                            {
                                RestartTimer(Normal);
                                return;
                            }

                            if (response.TryGetValue("TrackDuration", out string duration) && TimeSpan.TryParse(duration, _usCulture, out TimeSpan dur))
                            {
                                Duration = dur;
                            }

                            if (response.TryGetValue("RelTime", out string position) && TimeSpan.TryParse(position, _usCulture, out TimeSpan rel))
                            {
                                Position = rel;
                            }

                            // Get current media info.
                            uBaseObject? currentObject = null;
                            if (!response.TryGetValue("TrackMetaData", out string track) || string.IsNullOrEmpty(track))
                            {
                                // If track is null, some vendors do this, use GetMediaInfo instead
                                currentObject = await GetMediaInfo().ConfigureAwait(false);
                            }

                            XElement? uPnpResponse = ParseNodeResponse(track);
                            if (uPnpResponse == null)
                            {
                                _logger.LogError("Failed to parse xml: \n {Xml}", track);
                                currentObject = await GetMediaInfo().ConfigureAwait(false);
                            }

                            if (currentObject == null)
                            {
                                var e = uPnpResponse?.Element(uPnpNamespaces.items);
                                if (response.TryGetValue("TrackURI", out string trackUri))
                                {
                                    currentObject = CreateUBaseObject(e, trackUri);
                                }
                            }

                            if (currentObject != null)
                            {
                                UpdateMediaInfo(currentObject);
                            }

                            RestartTimer(Normal);
                        }
                    }

                    _connectFailureCount = 0;
                }
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            catch (HttpException ex)
            {
                if (_disposed)
                {
                    return;
                }

                _logger.LogError(ex, "{0} : Error updating device info.", Properties.Name);
                if (_connectFailureCount++ >= 3)
                {
                    _logger.LogDebug("{0} : Disposing device due to loss of connection.", Properties.Name);
                    OnDeviceUnavailable?.Invoke();
                    return;
                }

                RestartTimer(Normal);
            }
        }

        private async Task<bool> SendVolumeRequest(int value)
        {
            var result = false;
            if (_volume != value)
            {
                // Adjust for items that don't have a volume range 0..100.
                result = await SendCommand(TransportCommandsRender, "SetVolume", value).ConfigureAwait(false);
                if (result)
                {
                    _volume = value;
                }
            }

            return result;
        }

        /// <summary>
        /// Requests the volume setting from the client.
        /// </summary>
        /// <returns>Task.</returns>
        private async Task<bool> GetVolume()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(string.Empty);
            }

            if (_lastVolumeRefresh.AddSeconds(5) <= _lastVolumeRefresh)
            {
                return true;
            }

            string volume = string.Empty;
            try
            {
                var result = await SendCommandResponseRequired(TransportCommandsRender, "GetVolume").ConfigureAwait(false);
                var response = ParseResponse(result?.Document, "GetVolumeResponse");

                if (response.TryGetValue("CurrentVolume", out volume))
                {
                    if (int.TryParse(volume, out int value))
                    {
                        _volume = value;
                        if (_volume > 0)
                        {
                            _muteVol = _volume;
                        }

                        _lastVolumeRefresh = DateTime.UtcNow;
                    }

                    return true;
                }

                _logger.LogWarning("{0} : GetVolume Failed.", Properties.Name);
            }
            catch (ObjectDisposedException)
            {
                // Ignore.
            }
            catch (FormatException)
            {
                _logger.LogError("{0} : Error parsing GetVolume {1}.", Properties.Name, volume);
            }

            return false;
        }

        private async Task<bool> SendPauseRequest()
        {
            var result = false;
            if (!IsPaused)
            {
                result = await SendCommand(TransportCommandsAV, "Pause", 1).ConfigureAwait(false);
                if (result)
                {
                    // Stop user from issuing multiple commands.
                    TransportState = TransportState.PAUSED;
                    RestartTimer(Now);
                }
            }

            return result;
        }

        private async Task<bool> SendPlayRequest()
        {
            var result = false;
            if (!IsPlaying)
            {
                result = await SendCommand(TransportCommandsAV, "Play", 1).ConfigureAwait(false);
                if (result)
                {
                    // Stop user from issuing multiple commands.
                    TransportState = TransportState.PLAYING;
                    RestartTimer(Now);
                }
            }

            return result;
        }

        private async Task<bool> SendStopRequest()
        {
            var result = false;
            if (IsPlaying || IsPaused)
            {
                result = await SendCommand(TransportCommandsAV, "Stop", 1).ConfigureAwait(false);
                if (result)
                {
                    // Stop user from issuing multiple commands.
                    TransportState = TransportState.STOPPED;
                    RestartTimer(Now);
                }
            }

            return result;
        }

        /// <summary>
        /// Returns information associated with the current transport state of the specified instance.
        /// </summary>
        /// <returns>Task.</returns>
        private async Task<TransportState> GetTransportStatus()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(string.Empty);
            }

            if (_lastTransportRefresh.AddSeconds(5) >= DateTime.UtcNow)
            {
                return TransportState;
            }

            var result = await SendCommandResponseRequired(TransportCommandsAV, "GetTransportInfo").ConfigureAwait(false);
            var response = ParseResponse(result?.Document, "GetTransportInfoResponse");

            if (response.TryGetValue("CurrentTransportState", out string transportState))
            {
                if (Enum.TryParse(transportState, true, out TransportState state))
                {
                    _lastTransportRefresh = DateTime.UtcNow;
                    return state;
                }
            }

            _logger.LogWarning("GetTransportInfo failed.");
            return TransportState.ERROR;
        }

        private async Task<bool> SendMuteRequest(bool value)
        {
            var result = false;

            if (IsMuted != value)
            {
                result = await SendCommand(TransportCommandsRender, "SetMute", value ? 1 : 0).ConfigureAwait(false);
            }

            return result;
        }

        /// <summary>
        /// Gets the mute setting from the client.
        /// </summary>
        /// <returns>Task.</returns>
        private async Task<bool> GetMute()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(string.Empty);
            }

            if (_lastVolumeRefresh.AddSeconds(5) <= _lastVolumeRefresh)
            {
                return true;
            }

            var result = await SendCommandResponseRequired(TransportCommandsRender, "GetMute").ConfigureAwait(false);
            var response = ParseResponse(result?.Document, "GetMuteResponse");

            if (response.TryGetValue("CurrentMute", out string muted))
            {
                IsMuted = string.Equals(muted, "1", StringComparison.OrdinalIgnoreCase);
                return true;
            }

            _logger.LogWarning("{0} : GetMute failed.", Properties.Name);
            return false;
        }

        private async Task<bool> SendMediaRequest(MediaData settings)
        {
            var result = false;

            if (!string.IsNullOrEmpty(settings.Url))
            {
                settings.Url = settings.Url.Replace("&", "&amp;", StringComparison.OrdinalIgnoreCase);
                _logger.LogDebug(
                    "{0} : {1} - SetAvTransport Uri: {2} DlnaHeaders: {3}",
                    Properties.Name,
                    settings.Metadata,
                    settings.Url,
                    settings.Headers);

                var dictionary = new Dictionary<string, string>
                {
                    { "CurrentURI", settings.Url },
                    { "CurrentURIMetaData", DescriptionXmlBuilder.Escape(settings.Metadata) }
                };

                result = await SendCommand(
                    TransportCommandsAV,
                    "SetAVTransportURI",
                    settings.Url,
                    dictionary: dictionary,
                    header: settings.Headers).ConfigureAwait(false);

                if (result)
                {
                    await Task.Delay(50).ConfigureAwait(false);

                    result = await SendPlayRequest().ConfigureAwait(false);
                    if (result)
                    {
                        // Update what is playing.
                        _mediaPlaying = settings.Url;
                        RestartTimer(Now);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// The GetMediaInfo.
        /// </summary>
        /// <returns>The <see cref="Task{uBaseObject}"/>.</returns>
        private async Task<uBaseObject?> GetMediaInfo()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(string.Empty);
            }

            if (_lastMetaRefresh.AddSeconds(5) >= DateTime.UtcNow)
            {
                return CurrentMediaInfo;
            }

            var result = await SendCommandResponseRequired(TransportCommandsAV, "GetMediaInfo").ConfigureAwait(false);
            if (result != null)
            {
                var track = result.Document.Descendants("CurrentURIMetaData").FirstOrDefault();
                var e = track?.Element(uPnpNamespaces.items) ?? track;
                if (!string.IsNullOrWhiteSpace(e?.Value))
                {
                    _lastMetaRefresh = DateTime.UtcNow;
                    RestartTimer(Normal);
                    return UpnpContainer.Create(e);
                }

                track = result.Document.Descendants("CurrentURI").FirstOrDefault();
                e = track?.Element(uPnpNamespaces.items) ?? track;
                if (!string.IsNullOrWhiteSpace(e?.Value))
                {
                    _lastMetaRefresh = DateTime.UtcNow;
                    RestartTimer(Normal);
                    return new uBaseObject
                    {
                        Url = e.Value
                    };
                }
            }
            else
            {
                _logger.LogWarning("{0} : GetMediaInfo failed.", Properties.Name);
            }

            return null;
        }

        private async Task<bool> SendSeekRequest(TimeSpan value)
        {
            var result = false;
            if (IsPlaying || IsPaused)
            {
                result = await SendCommand(
                    TransportCommandsAV,
                    "Seek",
                    string.Format(CultureInfo.InvariantCulture, "{0:hh}:{0:mm}:{0:ss}", value),
                    commandParameter: "REL_TIME").ConfigureAwait(false);

                if (result)
                {
                    Position = value;
                    RestartTimer(Now);
                }
            }

            return result;
        }

        private async Task<bool> GetPositionRequest()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(string.Empty);
            }

            if (_lastPositionRequest.AddSeconds(5) >= DateTime.UtcNow)
            {
                return true;
            }

            // Update position information.
            try
            {
                var result = await SendCommandResponseRequired(TransportCommandsAV, "GetPositionInfo").ConfigureAwait(false);
                var position = ParseResponse(result?.Document, "GetPositionInfoResponse");
                if (position.Count != 0)
                {
                    if (position.TryGetValue("TrackDuration", out string value) && TimeSpan.TryParse(value, _usCulture, out TimeSpan d))
                    {
                        Duration = d;
                    }

                    if (position.TryGetValue("RelTime", out value) && TimeSpan.TryParse(value, _usCulture, out TimeSpan r))
                    {
                        Position = r;
                        _lastPositionRequest = DateTime.Now;
                    }

                    RestartTimer(Normal);
                    return true;
                }
            }
            catch (ObjectDisposedException)
            {
                // Ignore.
            }

            return false;
        }

        /// <summary>
        /// Retreives SSDP protocol information.
        /// </summary>
        /// <param name="services">The service to extract.</param>
        /// <returns>The <see cref="Task{TransportCommands}"/>.</returns>
        private async Task<TransportCommands?> GetProtocolAsync(DeviceService services)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(string.Empty);
            }

            if (services == null)
            {
                return null;
            }

            string url = NormalizeUrl(Properties.BaseUrl, services.ScpdUrl, true);

            var document = await GetDataAsync(_httpClient, url, _logger).ConfigureAwait(false);
            return TransportCommands.Create(document);
        }

        /// <summary>
        /// Updates the media info, firing events.
        /// </summary>
        /// <param name="mediaInfo">The mediaInfo<see cref="uBaseObject"/>.</param>
        private void UpdateMediaInfo(uBaseObject? mediaInfo)
        {
            var previousMediaInfo = CurrentMediaInfo;
            CurrentMediaInfo = mediaInfo;
            try
            {
                if (mediaInfo != null)
                {
                    if (previousMediaInfo == null)
                    {
                        if (TransportState != TransportState.STOPPED && !string.IsNullOrWhiteSpace(mediaInfo.Url))
                        {
                            _logger.LogDebug("{0} : Firing playback started event.", Properties.Name);
                            PlaybackStart?.Invoke(this, new PlaybackStartEventArgs
                            {
                                MediaInfo = mediaInfo
                            });
                        }
                    }
                    else if (mediaInfo.Equals(previousMediaInfo))
                    {
                        if (!string.IsNullOrWhiteSpace(mediaInfo?.Url))
                        {
                            _logger.LogDebug("{0} : Firing playback progress event.", Properties.Name);
                            PlaybackProgress?.Invoke(this, new PlaybackProgressEventArgs
                            {
                                MediaInfo = mediaInfo
                            });
                        }
                    }
                    else
                    {
                        _logger.LogDebug("{0} : Firing media change event.", Properties.Name);
                        MediaChanged?.Invoke(this, new MediaChangedEventArgs
                        {
                            OldMediaInfo = previousMediaInfo,
                            NewMediaInfo = mediaInfo
                        });
                    }
                }
                else if (previousMediaInfo != null)
                {
                    _logger.LogDebug("{0} : Firing playback stopped event.", Properties.Name);
                    PlaybackStopped?.Invoke(this, new PlaybackStoppedEventArgs
                    {
                        MediaInfo = previousMediaInfo
                    });
                }
            }
#pragma warning disable CA1031 // Do not catch general exception types : Don't let errors in the events affect us.
            catch (Exception ex)
#pragma warning restore CA1031 // Do not catch general exception types
            {
                _logger.LogError(ex, "{0} : UpdateMediaInfo errored.", Properties.Name);
            }
        }

        /// <summary>
        /// Returns the ServiceRenderingControl element of the device.
        /// </summary>
        /// <returns>The ServiceRenderingControl <see cref="DeviceService"/>.</returns>
        private DeviceService GetServiceRenderingControl()
        {
            var services = Properties.Services;

            return services.FirstOrDefault(s => string.Equals(s.ServiceType, "urn:schemas-upnp-org:service:RenderingControl:1", StringComparison.OrdinalIgnoreCase)) ??
                services.FirstOrDefault(s => (s.ServiceType ?? string.Empty).StartsWith("urn:schemas-upnp-org:service:RenderingControl", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Returns the AvTransportService element of the device.
        /// </summary>
        /// <returns>The AvTransportService <see cref="DeviceService"/>.</returns>
        private DeviceService GetAvTransportService()
        {
            var services = Properties.Services;

            return services.FirstOrDefault(s => string.Equals(s.ServiceType, "urn:schemas-upnp-org:service:AVTransport:1", StringComparison.OrdinalIgnoreCase)) ??
                services.FirstOrDefault(s => (s.ServiceType ?? string.Empty).StartsWith("urn:schemas-upnp-org:service:AVTransport", StringComparison.OrdinalIgnoreCase));
        }

#pragma warning disable SA1401 // Fields should be private
#pragma warning disable IDE1006 // Naming Styles
        internal class ValueRange
        {
            internal double FivePoints = 0;
            internal double Range = 0;
            internal int Min = 0;
            internal int Max = 100;
        }

        internal class MediaData
        {
            internal string Url = string.Empty;
            internal string Metadata = string.Empty;
            internal string Headers = string.Empty;
        }
    }
#pragma warning restore SA1401 // Fields should be private
#pragma warning restore IDE1006 // Naming Styles
}
