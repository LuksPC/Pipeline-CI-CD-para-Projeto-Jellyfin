﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Data.Events.Users;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Session;
using Rebus.Handlers;

namespace Jellyfin.Server.Implementations.Events.Consumers.Users
{
    /// <summary>
    /// Notifies the user's sessions when a user is deleted.
    /// </summary>
    public class UserDeletedNotifier : IHandleMessages<UserDeletedEventArgs>
    {
        private readonly ISessionManager _sessionManager;

        /// <summary>
        /// Initializes a new instance of the <see cref="UserDeletedNotifier"/> class.
        /// </summary>
        /// <param name="sessionManager">The session manager.</param>
        public UserDeletedNotifier(ISessionManager sessionManager)
        {
            _sessionManager = sessionManager;
        }

        /// <inheritdoc />
        public async Task Handle(UserDeletedEventArgs message)
        {
            await _sessionManager.SendMessageToUserSessions(
                new List<Guid> { message.Argument.Id },
                SessionMessageType.UserDeleted,
                message.Argument.Id.ToString("N", CultureInfo.InvariantCulture),
                CancellationToken.None).ConfigureAwait(false);
        }
    }
}
