﻿using System;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Model.System;

namespace Jellyfin.Server.Migrations.Routines;

/// <summary>
/// Migration to add the default cast receivers to the system config.
/// </summary>
public class AddDefaultCastReceivers : IPostStartupMigrationRoutine
{
    private readonly IServerConfigurationManager _serverConfigurationManager;

    /// <summary>
    /// Initializes a new instance of the <see cref="AddDefaultCastReceivers"/> class.
    /// </summary>
    /// <param name="serverConfigurationManager">Instance of the <see cref="IServerConfigurationManager"/> interface.</param>
    public AddDefaultCastReceivers(IServerConfigurationManager serverConfigurationManager)
    {
        _serverConfigurationManager = serverConfigurationManager;
    }

    /// <inheritdoc />
    public Guid Id => new("34A1A1C4-5572-418E-A2F8-32CDFE2668E8");

    /// <inheritdoc />
    public string Name => "AddDefaultCastReceivers";

    /// <inheritdoc />
    public string Timestamp => "1600000000";

    /// <inheritdoc />
    public bool PerformOnNewInstall => true;

    /// <inheritdoc />
    public void Perform()
    {
        // Only add if receiver list is empty.
        if (_serverConfigurationManager.Configuration.CastReceiverApplications.Length == 0)
        {
            _serverConfigurationManager.Configuration.CastReceiverApplications = new CastReceiverApplication[]
            {
                new()
                {
                    Id = "F007D354",
                    Name = "Stable"
                },
                new()
                {
                    Id = "6F511C87",
                    Name = "Unstable"
                }
            };

            _serverConfigurationManager.SaveConfiguration();
        }
    }
}
