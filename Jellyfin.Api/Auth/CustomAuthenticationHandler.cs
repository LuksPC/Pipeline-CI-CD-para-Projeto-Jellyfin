#nullable enable

using System.Globalization;
using System.Net;
using System.Security.Authentication;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using Jellyfin.Api.Constants;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Jellyfin.Api.Auth
{
    /// <summary>
    /// Custom authentication handler wrapping the legacy authentication.
    /// </summary>
    public class CustomAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        private readonly IAuthService _authService;

        /// <summary>
        /// Initializes a new instance of the <see cref="CustomAuthenticationHandler" /> class.
        /// </summary>
        /// <param name="authService">The jellyfin authentication service.</param>
        /// <param name="options">Options monitor.</param>
        /// <param name="logger">The logger.</param>
        /// <param name="encoder">The url encoder.</param>
        /// <param name="clock">The system clock.</param>
        public CustomAuthenticationHandler(
            IAuthService authService,
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            ISystemClock clock) : base(options, logger, encoder, clock)
        {
            _authService = authService;
        }

        /// <inheritdoc />
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            try
            {
                var authorizationInfo = _authService.Authenticate(Request);
                if (authorizationInfo == null)
                {
                    return Task.FromResult(AuthenticateResult.NoResult());
                    // TODO return when legacy API is removed.
                    // Don't spam the log with "Invalid User"
                    // return Task.FromResult(AuthenticateResult.Fail("Invalid user"));
                }

                var claims = new[]
                {
                    new Claim(ClaimTypes.Name, authorizationInfo.User.Name),
                    new Claim(ClaimTypes.Role, value: authorizationInfo.User.Policy.IsAdministrator ? UserRoles.Administrator : UserRoles.User),
                    new Claim(InternalClaimTypes.UserId, authorizationInfo.UserId.ToString("N", CultureInfo.InvariantCulture)),
                    new Claim(InternalClaimTypes.DeviceId, authorizationInfo.DeviceId),
                    new Claim(InternalClaimTypes.Device, authorizationInfo.Device),
                    new Claim(InternalClaimTypes.Client, authorizationInfo.Client),
                    new Claim(InternalClaimTypes.Version, authorizationInfo.Version),
                    new Claim(InternalClaimTypes.Token, authorizationInfo.Token)
                };

                var identity = new ClaimsIdentity(claims, Scheme.Name);
                var principal = new ClaimsPrincipal(identity);
                var ticket = new AuthenticationTicket(principal, Scheme.Name);

                return Task.FromResult(AuthenticateResult.Success(ticket));
            }
            catch (AuthenticationException ex)
            {
                return Task.FromResult(AuthenticateResult.Fail(ex));
            }
            catch (SecurityException ex)
            {
                return Task.FromResult(AuthenticateResult.Fail(ex));
            }
        }
    }
}
