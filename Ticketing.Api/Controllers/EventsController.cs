using Microsoft.AspNetCore.Mvc;
using Ticketing.Api.Contracts;
using Ticketing.Application.Abstractions.Messaging;
using Ticketing.Application.Common.Models;
using Ticketing.Application.Events;
using Ticketing.Application.Events.Commands;
using Ticketing.Application.Events.Queries;
using Ticketing.Application.Reservations;
using Ticketing.Application.Reservations.Commands;
using Ticketing.Application.Reservations.Queries;
using Ticketing.Domain.Events;

namespace Ticketing.Api.Controllers;

[ApiController]
[Route("api/events")]
[Produces("application/json")]
public sealed class EventsController(IDispatcher dispatcher) : ControllerBase
{
    /// <summary>Lists events ordered by start time.</summary>
    /// <param name="cancellationToken">Request cancellation token.</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Items per page (1-100).</param>
    /// <param name="search">Matches against name and venue.</param>
    /// <param name="status">Only return events with this status.</param>
    [HttpGet]
    [ProducesResponseType<PagedResult<EventDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<EventDto>>> List(
        CancellationToken cancellationToken,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] EventStatus? status = null
    ) =>
        await dispatcher.QueryAsync(
            new ListEventsQuery(page, pageSize, search, status),
            cancellationToken
        );

    /// <summary>Gets an event, including live seat availability.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType<EventDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EventDto>> GetById(
        Guid id,
        CancellationToken cancellationToken
    ) => await dispatcher.QueryAsync(new GetEventByIdQuery(id), cancellationToken);

    /// <summary>Creates a draft event.</summary>
    [HttpPost]
    [Consumes("application/json")]
    [ProducesResponseType<EventDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<EventDto>> Create(
        EventRequest request,
        CancellationToken cancellationToken
    )
    {
        var id = await dispatcher.SendAsync(
            new CreateEventCommand(
                request.Name,
                request.Description ?? string.Empty,
                request.Venue,
                request.StartsAt,
                request.Capacity
            ),
            cancellationToken
        );

        var created = await dispatcher.QueryAsync(new GetEventByIdQuery(id), cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id }, created);
    }

    /// <summary>Updates an event's details. Capacity cannot drop below seats already reserved.</summary>
    [HttpPut("{id:guid}")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update(
        Guid id,
        EventRequest request,
        CancellationToken cancellationToken
    )
    {
        await dispatcher.SendAsync(
            new UpdateEventCommand(
                id,
                request.Name,
                request.Description ?? string.Empty,
                request.Venue,
                request.StartsAt,
                request.Capacity
            ),
            cancellationToken
        );
        return NoContent();
    }

    /// <summary>Publishes a draft event so tickets can be reserved.</summary>
    [HttpPost("{id:guid}/publish")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Publish(Guid id, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new PublishEventCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>Cancels an event and all of its active reservations.</summary>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        await dispatcher.SendAsync(new CancelEventCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>Holds seats for a customer. The hold expires unless confirmed in time.</summary>
    [HttpPost("{id:guid}/reservations")]
    [Consumes("application/json")]
    [ProducesResponseType<ReservationDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReservationDto>> Reserve(
        Guid id,
        ReserveTicketsRequest request,
        CancellationToken cancellationToken
    )
    {
        var reservationId = await dispatcher.SendAsync(
            new ReserveTicketsCommand(id, request.CustomerEmail, request.Quantity),
            cancellationToken
        );

        var reservation = await dispatcher.QueryAsync(
            new GetReservationByIdQuery(reservationId),
            cancellationToken
        );
        return CreatedAtAction(
            nameof(ReservationsController.GetById),
            "Reservations",
            new { id = reservationId },
            reservation
        );
    }
}
