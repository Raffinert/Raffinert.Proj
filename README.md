[![Stand With Ukraine](https://raw.githubusercontent.com/vshymanskyy/StandWithUkraine/main/banner2-direct.svg)](https://stand-with-ukraine.pp.ua)

# Raffinert.Proj
[![NuGet version (Raffinert.Proj)](https://img.shields.io/nuget/v/Raffinert.Proj.svg?style=flat-square)](https://www.nuget.org/packages/Raffinert.Proj/)

## Usage
Full examples see in [Integration Tests](https://github.com/Raffinert/Raffinert.Proj/blob/main/tests/Raffinert.Proj.IntegrationTests/ProjTests.cs)

### Basic Projection Example

Define a projection for a product:

```csharp
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
```

Use it in a LINQ query:

```csharp
var productsQuery = _context.Products.Select(new ProductProj());
var projectedProducts = await productsQuery.ToArrayAsync();
```

### Integration Tests

This project includes integration tests to verify the functionality of projections:

1. **Enumerable Projection**: Test linking two projections for `IEnumerable`.
2. **Queryable Projection**: Test linking two projections for `IQueryable`.
3. **Nested Select Projection**: Test nested projections within LINQ `Select` calls.

Run the tests using the following command:

```bash
dotnet test
```

### Example Test

```csharp
private class CategoryProj : Proj<Category, CategoryDto>
{
    public override Expression<Func<Category, CategoryDto>> GetExpression()
    {
        return category => new CategoryDto { Name = category.Name, IsFruit = category.Name == "Fruit" };
    }
}

[Fact]
public void QueryableLinkTwoProjectionsByMap()
{
    var categoryProj = new CategoryProj();
    var productProj = Proj<Product, ProductDto>.Create(p => new ProductDto
    {
        Id = p.Id,
        Name = p.Name,
        Price = p.Price,
        Category = categoryProj.Map(p.Category)
    });

    var projectedArray = _context.Products.Select(productProj).ToArray();

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
    }, projectedArray);
}
```

### Debugging

The `Proj<T>` class includes built-in debugging support with a custom debugger display, giving developers an immediate view of the underlying expression while debugging.

See also [Raffinert.Spec](https://github.com/Raffinert/Raffinert.Spec) library;

## Contributing

Contributions are welcome! Please follow these steps:

1. Fork the repository.
2. Create a new branch for your feature or bugfix.
3. Submit a pull request for review.

## License

This project is licensed under the MIT License.