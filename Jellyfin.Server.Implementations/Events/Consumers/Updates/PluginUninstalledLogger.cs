﻿using System;
using System.Globalization;
using System.Threading.Tasks;
using Jellyfin.Data.Entities;
using MediaBrowser.Controller.Events.Updates;
using MediaBrowser.Model.Activity;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.Notifications;
using Rebus.Handlers;

namespace Jellyfin.Server.Implementations.Events.Consumers.Updates
{
    /// <summary>
    /// Creates an entry in the activity log when a plugin is uninstalled.
    /// </summary>
    public class PluginUninstalledLogger : IHandleMessages<PluginUninstalledEventArgs>
    {
        private readonly ILocalizationManager _localizationManager;
        private readonly IActivityManager _activityManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="PluginUninstalledLogger"/> class.
        /// </summary>
        /// <param name="localizationManager">The localization manager.</param>
        /// <param name="activityManager">The activity manager.</param>
        public PluginUninstalledLogger(ILocalizationManager localizationManager, IActivityManager activityManager)
        {
            _localizationManager = localizationManager;
            _activityManager = activityManager;
        }

        /// <inheritdoc />
        public async Task Handle(PluginUninstalledEventArgs e)
        {
            await _activityManager.CreateAsync(new ActivityLog(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        _localizationManager.GetLocalizedString("PluginUninstalledWithName"),
                        e.Argument.Name),
                    NotificationType.PluginUninstalled.ToString(),
                    Guid.Empty))
                .ConfigureAwait(false);
        }
    }
}
