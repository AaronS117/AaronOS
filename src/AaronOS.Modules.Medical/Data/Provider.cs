namespace AaronOS.Modules.Medical.Data;

public class Provider
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Specialty { get; set; }
    public string? Phone { get; set; }
    public string? Facility { get; set; }
    public string? Notes { get; set; }

    public string SpecialtyDisplay => string.IsNullOrWhiteSpace(Specialty) ? "—" : Specialty;
    public string PhoneDisplay => string.IsNullOrWhiteSpace(Phone) ? "—" : Phone;
    public string FacilityDisplay => string.IsNullOrWhiteSpace(Facility) ? "—" : Facility;
}
