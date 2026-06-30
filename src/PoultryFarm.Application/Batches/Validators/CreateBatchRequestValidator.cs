using FluentValidation;
using PoultryFarm.Application.Batches.DTOs;

namespace PoultryFarm.Application.Batches.Validators;

public sealed class CreateBatchRequestValidator : AbstractValidator<CreateBatchRequest>
{
    public CreateBatchRequestValidator()
    {
        RuleFor(x => x.CompanyId).NotEmpty();
        RuleFor(x => x.Breed).NotEmpty().MaximumLength(100);
        RuleFor(x => x.ExpectedSaleDate).GreaterThanOrEqualTo(x => x.ArrivalDate);
        RuleFor(x => x.Variants).NotEmpty();
        RuleForEach(x => x.Variants).ChildRules(v =>
        {
            v.RuleFor(x => x.InitialCount).GreaterThan(0);
            v.RuleFor(x => x.Notes).MaximumLength(255);
        });
    }
}
