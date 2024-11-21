using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Raffinert.Proj.IntegrationTests.Infrastructure;
using Raffinert.Proj.IntegrationTests.Model;

namespace Raffinert.Proj.IntegrationTests;

public class ProjTests(ProductFilterFixture fixture) : IClassFixture<ProductFilterFixture>
{
    private readonly TestDbContext _context = fixture.Context;

    [Fact]
    public void EnumerableLinkTwoProjectionsByMap()
    {
        // Arrange
        var categoryProj = new CategoryProj();
        var productProj = Proj<Product, ProductDto>.Create(p => new ProductDto
        {
            Id = p.Id,
            Name = p.Name,
            Price = p.Price,
            Category = categoryProj.Map(p.Category)
        });

        // Act
        var projectedEnumerable = _context.Products.Include(p => p.Category).ToArray().Select(productProj);
        var projectedProducts = projectedEnumerable.ToArray();

        // Assert
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
    public async Task QueryableLinkTwoProjectionsByMap()
    {
        // Arrange
        var categoryProj = new CategoryProj();
        var productProj = Proj<Product, ProductDto>.Create(p => new ProductDto
        {
            Id = p.Id,
            Name = p.Name,
            Price = p.Price,
            Category = categoryProj.Map(p.Category)
        });

        // Act
        var productsQuery = _context.Products.Select(productProj);
        var projectedProducts = await productsQuery.ToArrayAsync();

        // Assert
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
    public async Task QueryableConnectProjectionIntoNestedSelect()
    {
        // Arrange
        var prodProj1 = new ProductProj();

        // This will work
        //var proj = Proj<Product, ProductDto?>.Create(p => p.Category.Products.Select(p1 => new ProductProj().Map(p1)).FirstOrDefault());
        //var proj = Proj<Product, ProductDto?>.Create(p => p.Category.Products.Select(new ProductProj().Map).FirstOrDefault());
        //var proj = Proj<Product, ProductDto?>.Create(p => p.Category.Products.Select(p1 => prodProj1.Map(p1)).FirstOrDefault());
        var proj = Proj<Product, ProductDto?>.Create(p => p.Category.Products.Select(prodProj1.Map).FirstOrDefault());

        // This won't work
        //var proj = Proj<Product, ProductDto?>.Create(p => p.Category.Products.Select(Proj<Product, ProductDto>.Create(p1 => new ProductDto
        //{
        //    Id = p1.Id,
        //    Name = p1.Name,
        //    Price = p1.Price
        //}).Map).FirstOrDefault());

        // Act
        var productsQuery = _context.Products.Select(proj);
        var projectedProducts = await productsQuery.ToArrayAsync();

        // Assert
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
}