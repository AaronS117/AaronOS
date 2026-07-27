using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AaronOS.Modules.Nutrition.Data;

public class IngredientConfiguration : IEntityTypeConfiguration<Ingredient>
{
    public void Configure(EntityTypeBuilder<Ingredient> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Name).IsRequired();
        builder.HasIndex(i => i.Name).IsUnique();
        builder.Property(i => i.CaloriesPer100g).HasPrecision(8, 2);
        builder.Property(i => i.ProteinPer100g).HasPrecision(8, 2);
        builder.Property(i => i.FatPer100g).HasPrecision(8, 2);
        builder.Property(i => i.CarbsPer100g).HasPrecision(8, 2);
        builder.Property(i => i.FiberPer100g).HasPrecision(8, 2);
        builder.Property(i => i.SodiumMgPer100g).HasPrecision(8, 2);
        builder.Property(i => i.CostPer100g).HasPrecision(8, 2);

        builder.HasMany(i => i.Tags)
            .WithMany(t => t.Ingredients)
            .UsingEntity(j => j.ToTable("IngredientTags"));
    }
}
