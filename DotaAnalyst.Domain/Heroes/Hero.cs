namespace DotaAnalyst.Domain.Heroes;

public record Hero(int Id, string Name)
{
    // Older saved catalogs only know the name.
    public HeroDetails? Details { get; init; }
}
