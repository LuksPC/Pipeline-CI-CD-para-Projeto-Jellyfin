using System.Threading.Tasks;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Api.Auth.DefaultAuthorizationPolicy
{
    /// <summary>
    /// Default authorization handler.
    /// </summary>
    public class DefaultAuthorizationHandler : BaseAuthorizationHandler<DefaultAuthorizationRequirement>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DefaultAuthorizationHandler"/> class.
        /// </summary>
        /// <param name="userManager">Instance of the <see cref="IUserManager"/> interface.</param>
        /// <param name="networkManager">Instance of the <see cref="INetworkManager"/> interface.</param>
        /// <param name="httpContextAccessor">Instance of the <see cref="IHttpContextAccessor"/> interface.</param>
        /// <param name="sessionManager">Instance of the <see cref="ISessionManager"/> interface.</param>
        public DefaultAuthorizationHandler(
            IUserManager userManager,
            INetworkManager networkManager,
            IHttpContextAccessor httpContextAccessor,
            ISessionManager sessionManager)
            : base(userManager, networkManager, httpContextAccessor, sessionManager)
        {
        }

        /// <inheritdoc />
        protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, DefaultAuthorizationRequirement requirement)
        {
            var validated = ValidateClaims(context.User);
            if (validated)
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
