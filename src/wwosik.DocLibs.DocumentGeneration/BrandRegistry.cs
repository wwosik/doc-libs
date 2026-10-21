namespace wwosik.DocLibs.DocumentGeneration;

/// <summary>
/// The brands front matter may select by name: <see cref="Brand.Neutral"/>, the brand of the caller's template, and
/// the caller's list. Front matter only ever picks from these — it never names a file.
/// </summary>
internal sealed class BrandRegistry
{
    private readonly Dictionary<string, Brand> brands;

    private BrandRegistry(Dictionary<string, Brand> brands)
    {
        this.brands = brands;
    }

    /// <summary>The names for an error message: <c>'neutral' or 'acme'</c>.</summary>
    public string Expected
    {
        get
        {
            var names = this.brands.Values.Select(b => b.Name).Order(StringComparer.OrdinalIgnoreCase).Select(n => $"'{n}'").ToList();
            return names.Count == 1 ? names[0] : string.Join(", ", names[..^1]) + " or " + names[^1];
        }
    }

    /// <exception cref="ArgumentException">Two different brands share a name, or one has an invalid name.</exception>
    public static BrandRegistry Of(Brand? current, IReadOnlyList<Brand>? registered)
    {
        var brands = new Dictionary<string, Brand>(StringComparer.OrdinalIgnoreCase) { [Brand.Neutral.Name] = Brand.Neutral };
        foreach (var brand in (current is null ? [] : new[] { current }).Concat(registered ?? []))
        {
            ArgumentNullException.ThrowIfNull(brand, nameof(registered));
            if (!ResolvedBrand.IsValidName(brand.Name))
            {
                throw new ArgumentException($"'{brand.Name}' is not a valid brand name; use letters, digits and '-'.", nameof(registered));
            }

            if (brands.TryGetValue(brand.Name, out var known) && !known.Equals(brand))
            {
                throw new ArgumentException($"Two different brands are named '{brand.Name}'.", nameof(registered));
            }

            brands[brand.Name] = brand;
        }

        return new BrandRegistry(brands);
    }

    public Brand? Find(string name) => this.brands.GetValueOrDefault(name);
}
