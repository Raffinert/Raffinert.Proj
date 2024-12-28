using Microsoft.EntityFrameworkCore;
using Raffinert.Proj.IntegrationTests.Infrastructure;
using Raffinert.Proj.IntegrationTests.Model;
using System.Linq.Expressions;
using AgileObjects.ReadableExpressions;

namespace Raffinert.Proj.IntegrationTests;

public class ProjTests(ProductFilterFixture fixture) : IClassFixture<ProductFilterFixture>
{
    private readonly TestDbContext _context = fixture.Context;

    [Fact]
    public void EnumerableLinkTwoProjectionsByMap()
    {
        // Arrange
        var productProj = Proj<Product, ProductDto>.Create(p => new ProductDto
        {
            Id = p.Id,
            Name = p.Name,
            Price = p.Price,
            Category = new CategoryProj().Map(p.Category)
        });

        // Act
        var projectedEnumerable = _context.Products.Include(p => p.Category).ToArray().Select(productProj);
        var projectedProducts = projectedEnumerable.ToArray();

        // Assert
        Assert.Equal("""
                     p => new ProjTests.ProductDto
                     {
                         Id = p.Id,
                         Name = p.Name,
                         Price = p.Price,
                         Category = new ProjTests.CategoryProj().Map(p.Category)
                     }
                     """,
            productProj.GetExpression().ToReadableString());

        Assert.Equivalent(new[]
        {
            new ProductDto
            {
                Id = 1,
                Name = "Apple",
                Price = 10.0m,
                Category = new CategoryDto
                {
                    Name = "Fruit",
                    IsFruit = true
                }
            },
            new ProductDto
            {
                Id = 2,
                Name = "Banana",
                Price = 15.0m,
                Category = new CategoryDto
                {
                    Name = "Fruit",
                    IsFruit = true
                }
            },
            new ProductDto
            {
                Id = 3,
                Name = "Cherry",
                Price = 8.0m,
                Category = new CategoryDto
                {
                    Name = "Fruit",
                    IsFruit = true
                }
            }
        }, projectedProducts);
    }

    [Fact]
    public async Task QueryableLinkTwoProjectionsByMapIfNotNull()
    {
        // Arrange
        var categoryProj = new CategoryProj();
        var productProj = Proj<Product, ProductDto>.Create(p => new ProductDto
        {
            Id = p.Id,
            Name = p.Name,
            Price = p.Price,
            Category = categoryProj.MapIfNotNull(p.Category)!
        });

        // Act
        var productsQuery = _context.Products.Select(productProj);
        var projectedProducts = await productsQuery.ToArrayAsync();

        // Assert
        Assert.Equal("""
                     p => new ProjTests.ProductDto
                     {
                         Id = p.Id,
                         Name = p.Name,
                         Price = p.Price,
                         Category = (p.Category == null)
                             ? null
                             : new ProjTests.CategoryDto
                             {
                                 Name = p.Category.Name,
                                 IsFruit = p.Category.Name == "Fruit"
                             }
                     }
                     """,
            productProj.GetExpandedExpression().ToReadableString());

        Assert.Equivalent(new[]
        {
            new ProductDto
            {
                Id = 1,
                Name = "Apple",
                Price = 10.0m,
                Category = new CategoryDto
                {
                    Name = "Fruit",
                    IsFruit = true
                }
            },
            new ProductDto
            {
                Id = 2,
                Name = "Banana",
                Price = 15.0m,
                Category = new CategoryDto
                {
                    Name = "Fruit",
                    IsFruit = true
                }
            },
            new ProductDto
            {
                Id = 3,
                Name = "Cherry",
                Price = 8.0m,
                Category = new CategoryDto
                {
                    Name = "Fruit",
                    IsFruit = true
                }
            }
        }, projectedProducts);
    }

    [Fact]
    public async Task MapToExisting()
    {
        // Arrange
        var categoryProj = new CategoryProj();
        var productProj = Proj<Product, ProductDto>.Create(p => new ProductDto
        {
            Id = p.Id,
            Name = p.Name,
            Price = p.Price,
            Category = categoryProj.MapIfNotNull(p.Category)!
        });

        // Act
        var product = await _context.Products.Include(p => p.Category).FirstAsync(p => p.Id == 1);
        var existingNestedCategoryDto = new CategoryDto { Name = "N" };
        var existingProductDto = new ProductDto { Name = "", Category = existingNestedCategoryDto };
        productProj.MapToExisting(product, ref existingProductDto);

        // Assert
        Assert.Equal("""
                     (p, existing) =>
                     {
                         existing.Id = p.Id;
                         existing.Name = p.Name;
                         existing.Price = p.Price;
                     
                         if (p.Category == null)
                         {
                             existing.Category = null;
                         }
                         else
                         {
                             existing.Category.Name = p.Category.Name;
                             existing.Category.IsFruit = p.Category.Name == "Fruit";
                         }
                     }
                     """,
            productProj.GetMapToExistingExpression().ToReadableString());

        Assert.Equivalent(new ProductDto
        {
            Id = 1,
            Name = "Apple",
            Price = 10.0m,
            Category = new CategoryDto
            {
                Name = "Fruit",
                IsFruit = true
            }
        }, existingProductDto);

        Assert.Equivalent(new CategoryDto
        {
            Name = "Fruit",
            IsFruit = true
        }, existingNestedCategoryDto);
    }

    [Fact]
    public async Task MergeTwoProjections()
    {
        // Arrange
        var categoryProj = Proj<Category, FlatProductDto>.Create(category => new FlatProductDto
        {
            CategoryIsFruit = category.Name == "Fruit",
            CategoryName = category.Name
        });

        var productProj = Proj<Product, FlatProductDto>.Create(product => new FlatProductDto
        {
            Id = product.Id,
            Name = product.Name,
            Price = product.Price,
            CategoryName = ""
        });

        var mergedProj = productProj.MergeBindings(p => categoryProj.MapIfNotNull(p.Category)!);

        // Act
        var productsQuery = _context.Products.Select(mergedProj);
        var projectedProducts = await productsQuery.ToArrayAsync();

        // Assert
        Assert.Equal(mergedProj.GetExpression().ToString(), mergedProj.GetExpandedExpression().ToString());
        Assert.Equal("""
                     product => new ProjTests.FlatProductDto
                     {
                         Id = product.Id,
                         Name = product.Name,
                         Price = product.Price,
                         CategoryName = "",
                         CategoryIsFruit = (product.Category == null) ? default(bool) : product.Category.Name == "Fruit",
                         CategoryName = (product.Category == null) ? null : product.Category.Name
                     }
                     """,
            mergedProj.GetExpression().ToReadableString());

        Assert.Equivalent(new[]
        {
            new FlatProductDto
            {
                Id = 1,
                Name = "Apple",
                Price = 10.0m,
                CategoryName = "Fruit",
                CategoryIsFruit = true
            },
            new FlatProductDto
            {
                Id = 2,
                Name = "Banana",
                Price = 15.0m,
                CategoryName = "Fruit",
                CategoryIsFruit = true
            },
            new FlatProductDto
            {
                Id = 3,
                Name = "Cherry",
                Price = 8.0m,
                CategoryName = "Fruit",
                CategoryIsFruit = true
            }
        }, projectedProducts);
    }

    [Theory]
    [MemberData(nameof(GetProjectionCases))]
    public async Task QueryableConnectProjectionIntoNestedSelect(Proj<Product, ProductDto?> proj, string expectedExpressionString)
    {
        // Act
        var productsQuery = _context.Products.Select(proj);
        var projectedProducts = await productsQuery.ToArrayAsync();

        // Assert
        Assert.Equal(expectedExpressionString, proj.GetExpandedExpression().ToString());

        Assert.Equivalent(new[]
        {
            new
            {
                Id = 1,
                Name = "Apple",
                Price = 10.0m
            },
            new
            {
                Id = 1,
                Name = "Apple",
                Price = 10.0m
            },
            new
            {
                Id = 1,
                Name = "Apple",
                Price = 10.0m
            }
        }, projectedProducts);
    }

    public static IEnumerable<object[]> GetProjectionCases()
    {
        var prodProj = new ProductProj();

        return new List<object[]>
        {
            new object[] { Proj<Product, ProductDto?>.Create(p => p.Category.Products.Select(p1 => new ProductProj().Map(p1)).FirstOrDefault()), "p => p.Category.Products.Select(p1 => new ProductDto() {Id = p1.Id, Name = p1.Name, Price = p1.Price}).FirstOrDefault()" },
            new object[] { Proj<Product, ProductDto?>.Create(p => p.Category.Products.Select(new ProductProj().Map).FirstOrDefault()), "p => p.Category.Products.Select(p => new ProductDto() {Id = p.Id, Name = p.Name, Price = p.Price}).FirstOrDefault()"},
            new object[] { Proj<Product, ProductDto?>.Create(p => p.Category.Products.Select(p1 => prodProj.Map(p1)).FirstOrDefault()), "p => p.Category.Products.Select(p1 => new ProductDto() {Id = p1.Id, Name = p1.Name, Price = p1.Price}).FirstOrDefault()"},
            new object[] { Proj<Product, ProductDto?>.Create(p => p.Category.Products.Select(prodProj.Map).FirstOrDefault()), "p => p.Category.Products.Select(p => new ProductDto() {Id = p.Id, Name = p.Name, Price = p.Price}).FirstOrDefault()"},
            // new object[] { Proj<Product, ProductDto?>.Create(p => p.Category.Products.Select(Proj<Product, ProductDto>.Create(p1 => new ProductDto { Id = p1.Id, Name = p1.Name, Price = p1.Price }).Map).FirstOrDefault()), ""} -- this won't work for IQueryable
        };
    }

    private class ProductProj : Proj<Product, ProductDto>
    {
        public override Expression<Func<Product, ProductDto>> GetExpression()
        {
            return p => new ProductDto
            {
                Id = p.Id,
                Name = p.Name,
                Price = p.Price
            };
        }
    }

    private class CategoryProj : Proj<Category, CategoryDto>
    {
        public override Expression<Func<Category, CategoryDto>> GetExpression()
        {
            return category => new CategoryDto { Name = category.Name, IsFruit = category.Name == "Fruit" };
        }
    }

    public class ProductDto
    {
        public int Id { get; set; }
        public required string Name { get; set; }
        public decimal Price { get; set; }
        public CategoryDto Category { get; set; }
    }

    public class CategoryDto
    {
        public required string Name { get; set; }
        public bool IsFruit { get; set; }
    }

    public class FlatProductDto
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public decimal Price { get; set; }
        public string CategoryName { get; set; }
        public bool CategoryIsFruit { get; set; }
    }
}