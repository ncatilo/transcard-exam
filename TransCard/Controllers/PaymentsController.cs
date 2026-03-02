using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransCard.Handlers;
using TransCard.Handlers.Commands;
using TransCard.Handlers.Queries;
using TransCard.Models.Requests;
using TransCard.Models.Responses;

namespace TransCard.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
[Produces("application/json")]
public class PaymentsController(
    ICommandHandler<ProcessPaymentCommand, ProcessPaymentResult> processPaymentHandler,
    IQueryHandler<GetPaymentByIdQuery, PaymentResponse?> getPaymentHandler,
    IQueryHandler<GetPaymentEventsQuery, IReadOnlyList<PaymentEventResponse>> getPaymentEventsHandler,
    ILogger<PaymentsController> logger) : ControllerBase
{
    private static readonly HashSet<string> ValidCurrencies = new(StringComparer.OrdinalIgnoreCase)
    {
        "USD", "EUR", "GBP", "JPY", "CAD", "AUD", "CHF", "CNY", "SEK", "NZD"
    };

    /// <summary>
    /// Process a payment. Uses ReferenceId as an idempotency key.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(PaymentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(PaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ProcessPayment([FromBody] ProcessPaymentRequest request)
    {
        if (!ValidCurrencies.Contains(request.Currency))
        {
            return BadRequest(new ApiErrorResponse
            {
                Type = "ValidationError",
                Title = "Invalid currency code.",
                Status = 400,
                Detail = $"'{request.Currency}' is not a supported currency. Supported: {string.Join(", ", ValidCurrencies.Order())}",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        var command = new ProcessPaymentCommand(request.Amount, request.Currency, request.ReferenceId);
        var result = await processPaymentHandler.HandleAsync(command);

        if (result.IsExistingPayment)
        {
            logger.LogInformation("Returning cached payment for ReferenceId {ReferenceId}",
                request.ReferenceId);
            return Ok(result.Response);
        }

        return CreatedAtAction(nameof(GetPayment), new { id = result.Response.Id }, result.Response);
    }

    /// <summary>
    /// Retrieve a payment by its ID.
    /// </summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPayment(Guid id)
    {
        var response = await getPaymentHandler.HandleAsync(new GetPaymentByIdQuery(id));

        if (response is null)
        {
            return NotFound(new ApiErrorResponse
            {
                Type = "NotFound",
                Title = "Payment not found.",
                Status = 404,
                Detail = $"No payment found with ID '{id}'.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        return Ok(response);
    }

    /// <summary>
    /// Retrieve the event history (audit trail) for a payment.
    /// </summary>
    [HttpGet("{id:guid}/events")]
    [ProducesResponseType(typeof(IReadOnlyList<PaymentEventResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiErrorResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetPaymentEvents(Guid id)
    {
        var payment = await getPaymentHandler.HandleAsync(new GetPaymentByIdQuery(id));

        if (payment is null)
        {
            return NotFound(new ApiErrorResponse
            {
                Type = "NotFound",
                Title = "Payment not found.",
                Status = 404,
                Detail = $"No payment found with ID '{id}'.",
                TraceId = HttpContext.TraceIdentifier
            });
        }

        var events = await getPaymentEventsHandler.HandleAsync(new GetPaymentEventsQuery(id));
        return Ok(events);
    }
}
