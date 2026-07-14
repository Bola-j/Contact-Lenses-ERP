using Lensee.Modules.Catalog.Data;
using Lensee.Modules.Catalog.Services;
using Xunit;

namespace Lensee.Tests;

public sealed class SkuCodeGeneratorTests
{
    [Fact]
    public void Generate_UsesBrandCategoryPowerAndColor_ForLens()
    {
        var generator = new SkuCodeGenerator();
        var product = new Product
        {
            ProductType = CatalogValidation.Lens,
            Brand = new Brand { Name = "Lansee" },
            Category = new Category { Name = "Plain Medical" }
        };

        var code = generator.Generate(product, new SkuCodeInput("-", 1.25m, "Clear", null));

        Assert.Equal("LAN-PM-M125-CLEAR", code);
    }

    [Fact]
    public void Generate_UsesBrandCategoryAndSize_ForSolution()
    {
        var generator = new SkuCodeGenerator();
        var product = new Product
        {
            ProductType = CatalogValidation.Solution,
            Brand = new Brand { Name = "OptiCare" },
            Category = new Category { Name = "Preservation / Conservative Solution" }
        };

        var code = generator.Generate(product, new SkuCodeInput(null, null, null, "120ml"));

        Assert.Equal("OPT-PCS-120ML", code);
    }

    [Fact]
    public void Generate_UsesInitials_ForMultiWordBrand()
    {
        var generator = new SkuCodeGenerator();
        var product = new Product
        {
            ProductType = CatalogValidation.Lens,
            Brand = new Brand { Name = "Clear Vision" },
            Category = new Category { Name = "Colored Medical" }
        };

        var code = generator.Generate(product, new SkuCodeInput("+", 0m, "Honey", null));

        Assert.Equal("CV-CM-P0-HONEY", code);
    }

    [Fact]
    public void Generate_AppendsSize_ForLensWhenProvided()
    {
        var generator = new SkuCodeGenerator();
        var product = new Product
        {
            ProductType = CatalogValidation.Lens,
            Brand = new Brand { Name = "Lansee" },
            Category = new Category { Name = "Plain Medical" }
        };

        var code = generator.Generate(product, new SkuCodeInput("-", 1.25m, "Plain", "Box 3"));

        Assert.Equal("LAN-PM-M125-PLAIN-BOX3", code);
    }

    [Fact]
    public void Generate_KeepsDoubleWordColorsReadable_ForLens()
    {
        var generator = new SkuCodeGenerator();
        var product = new Product
        {
            ProductType = CatalogValidation.Lens,
            Brand = new Brand { Name = "Clear Vision" },
            Category = new Category { Name = "Colored Medical" }
        };

        var galaxyGray = generator.Generate(product, new SkuCodeInput("-", 0.50m, "Galaxy Gray", "Pack 2"));
        var selenaGray = generator.Generate(product, new SkuCodeInput("-", 0.50m, "Selena Gray", "Pack 2"));

        Assert.Equal("CV-CM-M05-GALAXYGRAY-PACK2", galaxyGray);
        Assert.Equal("CV-CM-M05-SELENAGRAY-PACK2", selenaGray);
    }
}
