using Microsoft.AspNetCore.Mvc;
using TransCard.Models.Requests;
using TransCard.Models.Responses;
using TransCard.Services;

namespace TransCard.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(ITokenService tokenService) : ControllerBase
{
    /// <summary>
    /// Generate a JWT token for API access.
    /// </summary>
    [HttpPost("token")]
    [ProducesResponseType(typeof(TokenResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status401Unauthorized)]
    public IActionResult GetToken([FromBody] TokenRequest request)
    {
        var result = tokenService.GenerateToken(request.ClientId, request.ClientSecret);

        if (result is null)
        {
            return Unauthorized(new ApiErrorResponse
            {
                Type = "AuthenticationError",
                Title = "Invalid credentials.",
                Status = 401,
                Detail = "The provided client ID or secret is invalid.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        return Ok(result);
    }
}
