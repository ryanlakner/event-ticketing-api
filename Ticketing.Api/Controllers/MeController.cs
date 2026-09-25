using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Ticketing.Application.Abstractions.Messaging;
using Ticketing.Application.Common.Models;
using Ticketing.Application.Common.Security;
using Ticketing.Application.Events;
using Ticketing.Application.Events.Queries;
using Ticketing.Application.Reservations;
using Ticketing.Application.Reservations.Queries;
using Ticketing.Domain.Events;
using Ticketing.Domain.Reservations;

namespace Ticketing.Api.Controllers;

/// <summary>
/// The signed-in caller's own resources. The user always comes from the access token, never
/// from the URL, so there is no ID to tamper with.
/// </summary>
[ApiController]
[Route("api/me")]
[Produces("application/json")]
public sealed class MeController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Lists events the caller organizes, drafts included, ordered by start time.</summary>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Items per page (1-100).</param>
    /// <param name="status">Only return events with this status.</param>
    [HttpGet("events")]
    [Authorize(Roles = Roles.Organizer)]
    [ProducesResponseType<PagedResult<EventDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<EventDto>>> ListMyEvents(
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] EventStatus? status = null
    ) =>
        await dispatcher.QueryAsync(
            new ListMyEventsQuery(page, pageSize, status),
            cancellationToken
        );

    /// <summary>Lists the caller's reservations, soonest event first.</summary>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Items per page (1-100).</param>
    /// <param name="status">Only return reservations with this status.</param>
    [HttpGet("reservations")]
    [Authorize(Roles = Roles.Customer)]
    [ProducesResponseType<PagedResult<ReservationDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ReservationDto>>> ListMyReservations(
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] ReservationStatus? status = null
    ) =>
        await dispatcher.QueryAsync(
            new ListMyReservationsQuery(page, pageSize, status),
            cancellationToken
        );
}
