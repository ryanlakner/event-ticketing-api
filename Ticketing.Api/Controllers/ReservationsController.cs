using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ticketing.Application.Abstractions.Messaging;
using Ticketing.Application.Common.Security;
using Ticketing.Application.Reservations;
using Ticketing.Application.Reservations.Commands;
using Ticketing.Application.Reservations.Queries;

namespace Ticketing.Api.Controllers;

[ApiController]
[Route("api/reservations")]
[Produces("application/json")]
public sealed class ReservationsController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Gets a reservation. Visible to its customer and the event's organizer.</summary>
    [HttpGet("{id:guid}")]
    [Authorize(Roles = $"{Roles.Customer},{Roles.Organizer}")]
    [ProducesResponseType<ReservationDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReservationDto>> GetById(
        Guid id,
        CancellationToken cancellationToken
    ) => await dispatcher.QueryAsync(new GetReservationByIdQuery(id), cancellationToken);

    /// <summary>Confirms the caller's pending reservation before its hold expires.</summary>
    [HttpPost("{id:guid}/confirm")]
    [Authorize(Roles = Roles.Customer)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Confirm(Guid id, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new ConfirmReservationCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>Cancels a reservation and releases its seats. Allowed for its customer and the event's organizer.</summary>
    [HttpPost("{id:guid}/cancel")]
    [Authorize(Roles = $"{Roles.Customer},{Roles.Organizer}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new CancelReservationCommand(id), cancellationToken);
        return NoContent();
    }
}
