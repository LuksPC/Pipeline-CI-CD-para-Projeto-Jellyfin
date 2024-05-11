using System;
using System.IO;
using System.Net;
using Jellyfin.Server.Helpers;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Controller.Extensions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Server.Extensions;

/// <summary>
/// Extensions for configuring the web host builder.
/// </summary>
public static class WebHostBuilderExtensions
{
    /// <summary>
    /// Configure the web host builder.
    /// </summary>
    /// <param name="builder">The builder to configure.</param>
    /// <param name="appHost">The application host.</param>
    /// <param name="startupConfig">The application configuration.</param>
    /// <param name="appPaths">The application paths.</param>
    /// <param name="logger">The logger.</param>
    /// <returns>The configured web host builder.</returns>
    public static IWebHostBuilder ConfigureWebHostBuilder(
        this IWebHostBuilder builder,
        CoreAppHost appHost,
        IConfiguration startupConfig,
        IApplicationPaths appPaths,
        ILogger logger)
    {
        return builder
            .UseKestrel((builderContext, options) =>
            {
                var addresses = appHost.NetManager.GetAllBindInterfaces(true);

                bool flagged = false;
                foreach (var netAdd in addresses)
                {
                    logger.LogInformation("Kestrel is listening on {Address}", IPAddress.IPv6Any.Equals(netAdd.Address) ? "All IPv6 addresses" : netAdd.Address);
                    options.Listen(netAdd.Address, appHost.HttpPort);
                    if (appHost.ListenWithHttps)
                    {
                        options.Listen(
                            netAdd.Address,
                            appHost.HttpsPort,
                            listenOptions => listenOptions.UseHttps(appHost.Certificate));
                    }
                    else if (builderContext.HostingEnvironment.IsDevelopment())
                    {
                        try
                        {
                            options.Listen(
                                netAdd.Address,
                                appHost.HttpsPort,
                                listenOptions => listenOptions.UseHttps());
                        }
                        catch (InvalidOperationException)
                        {
                            if (!flagged)
                            {
                                logger.LogWarning("Failed to listen to HTTPS using the ASP.NET Core HTTPS development certificate. Please ensure it has been installed and set as trusted");
                                flagged = true;
                            }
                        }
                    }
                }

                // Bind to unix socket (only on unix systems)
                if (startupConfig.UseUnixSocket() && Environment.OSVersion.Platform == PlatformID.Unix)
                {
                    var socketPath = StartupHelpers.GetUnixSocketPath(startupConfig, appPaths);

                    // Workaround for https://github.com/aspnet/AspNetCore/issues/14134
                    if (File.Exists(socketPath))
                    {
                        File.Delete(socketPath);
                    }

                    options.ListenUnixSocket(socketPath);
                    logger.LogInformation("Kestrel listening to unix socket {SocketPath}", socketPath);
                }

                // look for LISTEN_FDS and listen on those sockets
                KestrelServerOptionsSystemdExtensions.UseSystemd(options);
            })
            .UseStartup(_ => new Startup(appHost));
    }
}
