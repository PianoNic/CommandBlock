using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Toamaisutaa.AspNetCore;
using CommandBlock.Application.Dtos.App;

namespace CommandBlock.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AppController(IToamaisutaaClientConfigurationProvider clientConfiguration) : ControllerBase
    {
        // /application.properties at the repo root is the single source of truth for the app version;
        // src/Directory.Build.props feeds it into AssemblyInformationalVersion at build time. SourceLink
        // may append "+<commit>"; strip it so the SPA shows a clean semver.
        private static readonly string AppVersion =
            typeof(AppController).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?.Split('+')[0]
            ?? "0.0.0";

        /// <summary>What the SPA reads before sign-in: where to log in, and which build it is talking to.
        /// Toamaisutaa resolves the redirect URIs (configured value, then the public URL, then the
        /// request's own origin).</summary>
        [AllowAnonymous]
        [HttpGet]
        [ProducesResponseType(typeof(AppDto), StatusCodes.Status200OK)]
        public IActionResult Get()
        {
            var auth = clientConfiguration.GetConfiguration(HttpContext);
            return Ok(new AppDto
            {
                Authority = auth.Authority,
                ClientId = auth.ClientId,
                RedirectUri = auth.RedirectUri,
                PostLogoutRedirectUri = auth.PostLogoutRedirectUri,
                Scope = auth.Scope,
                Version = AppVersion,
            });
        }
    }
}
