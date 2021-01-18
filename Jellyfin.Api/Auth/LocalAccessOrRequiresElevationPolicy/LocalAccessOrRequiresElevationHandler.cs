using System.Threading.Tasks;
using Jellyfin.Api.Constants;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Api.Auth.LocalAccessOrRequiresElevationPolicy
{
    /// <summary>
    /// Local access or require elevated privileges handler.
    /// </summary>
    public class LocalAccessOrRequiresElevationHandler : BaseAuthorizationHandler<LocalAccessOrRequiresElevationRequirement>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="LocalAccessOrRequiresElevationHandler"/> class.
        /// </summary>
        /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
        /// <param name="networkManager">Instance of the <see cref="INetworkManager"/> interface.</param>
        /// <param name="httpContextAccessor">Instance of the <see cref="IHttpContextAccessor"/> interface.</param>
        /// <param name="sessionManager">Instance of the <see cref="ISessionManager"/> interface.</param>
        public LocalAccessOrRequiresElevationHandler(
            IUserManager userManager,
            INetworkManager networkManager,
            IHttpContextAccessor httpContextAccessor,
            ISessionManager sessionManager)
            : base(userManager, networkManager, httpContextAccessor, sessionManager)
        {
        }

        /// <inheritdoc />
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, LocalAccessOrRequiresElevationRequirement requirement)
        {
            var validated = ValidateClaims(context.User, localAccessOnly: true);
            if (validated || context.User.IsInRole(UserRoles.Administrator))
            {
                context.Succeed(requirement);
            }
            else
            {
                context.Fail();
            }

            return Task.CompletedTask;
        }
    }
}
