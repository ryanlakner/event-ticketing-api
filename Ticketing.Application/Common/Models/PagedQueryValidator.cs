using FluentValidation;

namespace Ticketing.Application.Common.Models;

/// <summary>Paging rules shared by every list query; include it from the query's own validator.</summary>
internal sealed class PagedQueryValidator : AbstractValidator<IPagedQuery>
{
    public const int MaxPageSize = 100;

    public PagedQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, MaxPageSize);
    }
}
