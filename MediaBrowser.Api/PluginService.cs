using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Common;
using MediaBrowser.Common.Net;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Common.Security;
using MediaBrowser.Common.Updates;
using MediaBrowser.Controller.Devices;
using MediaBrowser.Controller.Net;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;
using MediaBrowser.Model.Services;
using Microsoft.Extensions.Logging;

namespace MediaBrowser.Api
{
    /// <summary>
    /// Class Plugins
    /// </summary>
    [Route("/Plugins", "GET", Summary = "Gets a list of currently installed plugins")]
    [Authenticated]
    public class GetPlugins : IReturn<PluginInfo[]>
    {
        public bool? IsAppStoreEnabled { get; set; }
    }

    /// <summary>
    /// Class UninstallPlugin
    /// </summary>
    [Route("/Plugins/{Id}", "DELETE", Summary = "Uninstalls a plugin")]
    [Authenticated(Roles = "Admin")]
    public class UninstallPlugin : IReturnVoid
    {
        /// <summary>
        /// Gets or sets the id.
        /// </summary>
        /// <value>The id.</value>
        [ApiMember(Name = "Id", Description = "Plugin Id", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "DELETE")]
        public string Id { get; set; }
    }

    /// <summary>
    /// Class GetPluginConfiguration
    /// </summary>
    [Route("/Plugins/{Id}/Configuration", "GET", Summary = "Gets a plugin's configuration")]
    [Authenticated]
    public class GetPluginConfiguration
    {
        /// <summary>
        /// Gets or sets the id.
        /// </summary>
        /// <value>The id.</value>
        [ApiMember(Name = "Id", Description = "Plugin Id", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "GET")]
        public string Id { get; set; }
    }

    /// <summary>
    /// Class UpdatePluginConfiguration
    /// </summary>
    [Route("/Plugins/{Id}/Configuration", "POST", Summary = "Updates a plugin's configuration")]
    [Authenticated]
    public class UpdatePluginConfiguration : IRequiresRequestStream, IReturnVoid
    {
        /// <summary>
        /// Gets or sets the id.
        /// </summary>
        /// <value>The id.</value>
        [ApiMember(Name = "Id", Description = "Plugin Id", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "POST")]
        public string Id { get; set; }

        /// <summary>
        /// The raw Http Request Input Stream
        /// </summary>
        /// <value>The request stream.</value>
        public Stream RequestStream { get; set; }
    }

    /// <summary>
    /// Class GetPluginSecurityInfo
    /// </summary>
    [Route("/Plugins/SecurityInfo", "GET", Summary = "Gets plugin registration information", IsHidden = true)]
    [Authenticated]
    public class GetPluginSecurityInfo : IReturn<PluginSecurityInfo>
    {
    }

    /// <summary>
    /// Class UpdatePluginSecurityInfo
    /// </summary>
    [Route("/Plugins/SecurityInfo", "POST", Summary = "Updates plugin registration information", IsHidden = true)]
    [Authenticated(Roles = "Admin")]
    public class UpdatePluginSecurityInfo : PluginSecurityInfo, IReturnVoid
    {
    }

    [Route("/Plugins/RegistrationRecords/{Name}", "GET", Summary = "Gets registration status for a feature", IsHidden = true)]
    [Authenticated]
    public class GetRegistrationStatus
    {
        [ApiMember(Name = "Name", Description = "Feature Name", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "GET")]
        public string Name { get; set; }
    }

    [Route("/Registrations/{Name}", "GET", Summary = "Gets registration status for a feature", IsHidden = true)]
    [Authenticated]
    public class GetRegistration : IReturn<RegistrationInfo>
    {
        [ApiMember(Name = "Name", Description = "Feature Name", IsRequired = true, DataType = "string", ParameterType = "path", Verb = "GET")]
        public string Name { get; set; }
    }

    public class RegistrationInfo
    {
        public string Name { get; set; }
        public DateTime ExpirationDate { get; set; }
        public bool IsTrial { get; set; }
        public bool IsRegistered { get; set; }
    }

    [Route("/Appstore/Register", "POST", Summary = "Registers an appstore sale", IsHidden = true)]
    [Authenticated]
    public class RegisterAppstoreSale
    {
        [ApiMember(Name = "Parameters", Description = "Java representation of parameters to pass through to admin server", IsRequired = true, DataType = "string", ParameterType = "query", Verb = "POST")]
        public string Parameters { get; set; }
    }

    /// <summary>
    /// Class PluginsService
    /// </summary>
    public class PluginService : BaseApiService
    {
        /// <summary>
        /// The _json serializer
        /// </summary>
        private readonly IJsonSerializer _jsonSerializer;

        /// <summary>
        /// The _app host
        /// </summary>
        private readonly IApplicationHost _appHost;

        private readonly ISecurityManager _securityManager;

        private readonly IInstallationManager _installationManager;
        private readonly INetworkManager _network;
        private readonly IDeviceManager _deviceManager;

        public PluginService(IJsonSerializer jsonSerializer, IApplicationHost appHost, ISecurityManager securityManager, IInstallationManager installationManager, INetworkManager network, IDeviceManager deviceManager)
            : base()
        {
            if (jsonSerializer == null)
            {
                throw new ArgumentNullException(nameof(jsonSerializer));
            }

            _appHost = appHost;
            _securityManager = securityManager;
            _installationManager = installationManager;
            _network = network;
            _deviceManager = deviceManager;
            _jsonSerializer = jsonSerializer;
        }

        /// <summary>
        /// Gets the specified request.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>System.Object.</returns>
        public async Task<object> Get(GetRegistrationStatus request)
        {
            var result = await _securityManager.GetRegistrationStatus(request.Name).ConfigureAwait(false);

            return ToOptimizedResult(result);
        }

        public async Task<object> Get(GetRegistration request)
        {
            var result = await _securityManager.GetRegistrationStatus(request.Name).ConfigureAwait(false);

            var info = new RegistrationInfo
            {
                ExpirationDate = result.ExpirationDate,
                IsRegistered = result.IsRegistered,
                IsTrial = result.TrialVersion,
                Name = request.Name
            };

            return ToOptimizedResult(info);
        }

        /// <summary>
        /// Gets the specified request.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>System.Object.</returns>
        public async Task<object> Get(GetPlugins request)
        {
            var result = _appHost.Plugins.OrderBy(p => p.Name).Select(p => p.GetPluginInfo()).ToArray();
            var requireAppStoreEnabled = request.IsAppStoreEnabled.HasValue && request.IsAppStoreEnabled.Value;

            // Don't fail just on account of image url's
            try
            {
                var packages = (await _installationManager.GetAvailablePackagesWithoutRegistrationInfo(CancellationToken.None));

                foreach (var plugin in result)
                {
                    var pkg = packages.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.guid) && string.Equals(i.guid.Replace("-", string.Empty), plugin.Id.Replace("-", string.Empty), StringComparison.OrdinalIgnoreCase));

                    if (pkg != null)
                    {
                        plugin.ImageUrl = pkg.thumbImage;
                    }
                }

                if (requireAppStoreEnabled)
                {
                    result = result
                        .Where(plugin =>
                        {
                            var pkg = packages.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.guid) && new Guid(plugin.Id).Equals(new Guid(i.guid)));
                            return pkg != null && pkg.enableInAppStore;

                        })
                        .ToArray();
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, "Error getting plugin list");
                // Play it safe here
                if (requireAppStoreEnabled)
                {
                    result = new PluginInfo[] { };
                }
            }

            return ToOptimizedResult(result);
        }

        /// <summary>
        /// Gets the specified request.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>System.Object.</returns>
        public object Get(GetPluginConfiguration request)
        {
            var guid = new Guid(request.Id);
            var plugin = _appHost.Plugins.First(p => p.Id == guid) as IHasPluginConfiguration;

            return ToOptimizedResult(plugin.Configuration);
        }

        /// <summary>
        /// Gets the specified request.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns>System.Object.</returns>
        public async Task<object> Get(GetPluginSecurityInfo request)
        {
            var result = new PluginSecurityInfo
            {
                IsMBSupporter = await _securityManager.IsSupporter().ConfigureAwait(false),
                SupporterKey = _securityManager.SupporterKey
            };

            return ToOptimizedResult(result);
        }

        /// <summary>
        /// Post app store sale
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public Task Post(RegisterAppstoreSale request)
        {
            return _securityManager.RegisterAppStoreSale(request.Parameters);
        }

        /// <summary>
        /// Posts the specified request.
        /// </summary>
        /// <param name="request">The request.</param>
        public Task Post(UpdatePluginSecurityInfo request)
        {
            return _securityManager.UpdateSupporterKey(request.SupporterKey);
        }

        /// <summary>
        /// Posts the specified request.
        /// </summary>
        /// <param name="request">The request.</param>
        public async Task Post(UpdatePluginConfiguration request)
        {
            // We need to parse this manually because we told service stack not to with IRequiresRequestStream
            // https://code.google.com/p/servicestack/source/browse/trunk/Common/ServiceStack.Text/ServiceStack.Text/Controller/PathInfo.cs
            var id = new Guid(GetPathValue(1));

            var plugin = _appHost.Plugins.First(p => p.Id == id) as IHasPluginConfiguration;

            if (plugin == null)
            {
                throw new FileNotFoundException();
            }

            var configuration = (await _jsonSerializer.DeserializeFromStreamAsync(request.RequestStream, plugin.ConfigurationType).ConfigureAwait(false)) as BasePluginConfiguration;

            plugin.UpdateConfiguration(configuration);
        }

        /// <summary>
        /// Deletes the specified request.
        /// </summary>
        /// <param name="request">The request.</param>
        public void Delete(UninstallPlugin request)
        {
            var guid = new Guid(request.Id);
            var plugin = _appHost.Plugins.First(p => p.Id == guid);

            _installationManager.UninstallPlugin(plugin);
        }
    }
}
