using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AaronOS.Modules.Medical.Data;

// All nine configurations live together: each is a handful of lines, and keeping them in one file
// makes the module's whole schema readable at a glance. ExternalId indexes are deliberately
// non-unique — the same source id can legitimately appear across different record types.

public class MedicalConditionConfiguration : IEntityTypeConfiguration<MedicalCondition>
{
    public void Configure(EntityTypeBuilder<MedicalCondition> builder)
    {
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).IsRequired();
        builder.HasIndex(c => c.ExternalId);
    }
}

public class ProviderConfiguration : IEntityTypeConfiguration<Provider>
{
    public void Configure(EntityTypeBuilder<Provider> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).IsRequired();
    }
}

public class MedicationConfiguration : IEntityTypeConfiguration<Medication>
{
    public void Configure(EntityTypeBuilder<Medication> builder)
    {
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Name).IsRequired();
        builder.HasIndex(m => m.ExternalId);
        builder.HasOne(m => m.Provider).WithMany().HasForeignKey(m => m.ProviderId);
    }
}

public class AllergyConfiguration : IEntityTypeConfiguration<Allergy>
{
    public void Configure(EntityTypeBuilder<Allergy> builder)
    {
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Substance).IsRequired();
        builder.HasIndex(a => a.ExternalId);
    }
}

public class ImmunizationConfiguration : IEntityTypeConfiguration<Immunization>
{
    public void Configure(EntityTypeBuilder<Immunization> builder)
    {
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Vaccine).IsRequired();
        builder.HasIndex(i => i.ExternalId);
    }
}

public class MedicalProcedureConfiguration : IEntityTypeConfiguration<MedicalProcedure>
{
    public void Configure(EntityTypeBuilder<MedicalProcedure> builder)
    {
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Name).IsRequired();
        builder.HasIndex(p => p.ExternalId);
        builder.HasOne(p => p.Provider).WithMany().HasForeignKey(p => p.ProviderId);
    }
}

public class MedicalVisitConfiguration : IEntityTypeConfiguration<MedicalVisit>
{
    public void Configure(EntityTypeBuilder<MedicalVisit> builder)
    {
        builder.HasKey(v => v.Id);
        builder.HasIndex(v => v.ExternalId);
        builder.HasOne(v => v.Provider).WithMany().HasForeignKey(v => v.ProviderId);
    }
}

public class LabResultConfiguration : IEntityTypeConfiguration<LabResult>
{
    public void Configure(EntityTypeBuilder<LabResult> builder)
    {
        builder.HasKey(l => l.Id);
        builder.Property(l => l.TestName).IsRequired();
        builder.Property(l => l.Value).HasPrecision(12, 4);
        builder.Property(l => l.ReferenceLow).HasPrecision(12, 4);
        builder.Property(l => l.ReferenceHigh).HasPrecision(12, 4);
        // The Labs page groups and charts by test name, so it is worth an index.
        builder.HasIndex(l => l.TestName);
        builder.HasIndex(l => l.ExternalId);
    }
}

public class MedicalDocumentConfiguration : IEntityTypeConfiguration<MedicalDocument>
{
    public void Configure(EntityTypeBuilder<MedicalDocument> builder)
    {
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Title).IsRequired();
        builder.Property(d => d.FilePath).IsRequired();
        builder.HasOne(d => d.Visit).WithMany().HasForeignKey(d => d.VisitId);
    }
}
