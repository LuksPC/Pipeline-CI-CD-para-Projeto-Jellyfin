﻿using System;
using System.Globalization;
using System.Threading.Tasks;
using Jellyfin.Data.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Activity;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Globalization;
using MediaBrowser.Model.Notifications;
using Microsoft.Extensions.Logging;
using Rebus.Handlers;

namespace Jellyfin.Server.Implementations.Events.Consumers.Session
{
    /// <summary>
    /// Creates an activity log entry whenever a user stops playback.
    /// </summary>
    public class PlaybackStopLogger : IHandleMessages<PlaybackStopEventArgs>
    {
        private readonly ILogger<PlaybackStopLogger> _logger;
        private readonly ILocalizationManager _localizationManager;
        private readonly IActivityManager _activityManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="PlaybackStopLogger"/> class.
        /// </summary>
        /// <param name="logger">The logger.</param>
        /// <param name="localizationManager">The localization manager.</param>
        /// <param name="activityManager">The activity manager.</param>
        public PlaybackStopLogger(ILogger<PlaybackStopLogger> logger, ILocalizationManager localizationManager, IActivityManager activityManager)
        {
            _logger = logger;
            _localizationManager = localizationManager;
            _activityManager = activityManager;
        }

        /// <inheritdoc />
        public async Task Handle(PlaybackStopEventArgs message)
        {
            var item = message.MediaInfo;

            if (item == null)
            {
                _logger.LogWarning("PlaybackStopped reported with null media info.");
                return;
            }

            if (message.IsThemeMedia)
            {
                // Don't report theme song or local trailer playback
                return;
            }

            if (message.Users.Count == 0)
            {
                return;
            }

            var user = message.Users[0];

            var notificationType = GetPlaybackStoppedNotificationType(item.MediaType);
            if (notificationType == null)
            {
                return;
            }

            await _activityManager.CreateAsync(new ActivityLog(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        _localizationManager.GetLocalizedString("UserStoppedPlayingItemWithValues"),
                        user.Username,
                        GetItemName(item),
                        message.DeviceName),
                    notificationType,
                    user.Id))
                .ConfigureAwait(false);
        }

        private static string GetItemName(BaseItemDto item)
        {
            var name = item.Name;

            if (!string.IsNullOrEmpty(item.SeriesName))
            {
                name = item.SeriesName + " - " + name;
            }

            if (item.Artists != null && item.Artists.Count > 0)
            {
                name = item.Artists[0] + " - " + name;
            }

            return name;
        }

        private static string? GetPlaybackStoppedNotificationType(string mediaType)
        {
            if (string.Equals(mediaType, MediaType.Audio, StringComparison.OrdinalIgnoreCase))
            {
                return NotificationType.AudioPlaybackStopped.ToString();
            }

            if (string.Equals(mediaType, MediaType.Video, StringComparison.OrdinalIgnoreCase))
            {
                return NotificationType.VideoPlaybackStopped.ToString();
            }

            return null;
        }
    }
}
