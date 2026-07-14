using Lensee.Modules.Catalog.Services;
using Xunit;

namespace Lensee.Tests;

public sealed class CatalogValidationTests
{
    [Fact]
    public void ValidateProduct_RequiresClinicalParams_ForLens()
    {
        var errors = CatalogValidation.ValidateProduct(new ProductValidationInput(
            "Monthly Lens",
            CatalogValidation.Lens,
            "Batch",
            "3 years",
            "Monthly",
            "6 months",
            1,
            "SinglePiece",
            null,
            null));

        Assert.Contains(nameof(ProductValidationInput.ClinicalParams), errors.Keys);
    }

    [Fact]
    public void ValidateProduct_DoesNotRequireClinicalParams_ForSolution()
    {
        var errors = CatalogValidation.ValidateProduct(new ProductValidationInput(
            "Lens Solution",
            CatalogValidation.Solution,
            "Product",
            "2 years",
            "Monthly",
            "3 months",
            1,
            "SinglePiece",
            null,
            """{"volumeMl":120}"""));

        Assert.DoesNotContain(nameof(ProductValidationInput.ClinicalParams), errors.Keys);
    }

    [Fact]
    public void ValidateProduct_RejectsInvalidJson()
    {
        var errors = CatalogValidation.ValidateProduct(new ProductValidationInput(
            "Blue Lens",
            CatalogValidation.Lens,
            "Batch",
            "3 years",
            "Monthly",
            "6 months",
            1,
            "SinglePiece",
            "{bad-json",
            null));

        Assert.Contains(nameof(ProductValidationInput.ClinicalParams), errors.Keys);
    }

    [Fact]
    public void ValidateSku_RequiresColor_ForLensSku()
    {
        var errors = CatalogValidation.ValidateSku(new SkuValidationInput(
            "+",
            1.25m,
            null,
            null),
            CatalogValidation.Lens);

        Assert.Contains(nameof(SkuValidationInput.ColorName), errors.Keys);
    }

    [Fact]
    public void ValidateSku_RequiresSize_ForSolutionSku()
    {
        var errors = CatalogValidation.ValidateSku(new SkuValidationInput(
            null,
            null,
            null,
            null),
            CatalogValidation.Solution);

        Assert.Contains(nameof(SkuValidationInput.Size), errors.Keys);
    }

    [Fact]
    public void ValidateProduct_AllowsPrdSellModes()
    {
        foreach (var sellMode in new[] { "SealedPackOnly", "SinglePiece", "Both" })
        {
            var errors = CatalogValidation.ValidateProduct(new ProductValidationInput(
                "Lens Solution",
                CatalogValidation.Solution,
                "Product",
                "2 years",
                "Monthly",
                "3 months",
                1,
                sellMode,
                null,
                """{"volumeMl":120}"""));

            Assert.DoesNotContain(nameof(ProductValidationInput.SellMode), errors.Keys);
        }
    }

    [Fact]
    public void ValidateProduct_RejectsNonPrdSellMode()
    {
        var errors = CatalogValidation.ValidateProduct(new ProductValidationInput(
            "Lens Solution",
            CatalogValidation.Solution,
            "Product",
            "2 years",
            "Monthly",
            "3 months",
            1,
            "Piece",
            null,
            """{"volumeMl":120}"""));

        Assert.Contains(nameof(ProductValidationInput.SellMode), errors.Keys);
    }

    [Fact]
    public void ValidateProduct_RejectsInvalidOpenedExpiryDuration()
    {
        var errors = CatalogValidation.ValidateProduct(new ProductValidationInput(
            "Lens Solution",
            CatalogValidation.Solution,
            "Product",
            "3 months",
            "Monthly",
            "six months",
            1,
            "SinglePiece",
            null,
            """{"volumeMl":120}"""));

        Assert.Contains(nameof(ProductValidationInput.OpenedExpiryDuration), errors.Keys);
    }

    [Fact]
    public void NormalizeOpenedDuration_StoresNumericInputAsMonths()
    {
        Assert.Equal("1 month", CatalogValidation.NormalizeOpenedDuration("1"));
        Assert.Equal("6 months", CatalogValidation.NormalizeOpenedDuration("6"));
        Assert.Equal("1 day", CatalogValidation.NormalizeOpenedDuration("1 day"));
        Assert.Equal("2 years", CatalogValidation.NormalizeOpenedDuration("2 years"));
    }
}
