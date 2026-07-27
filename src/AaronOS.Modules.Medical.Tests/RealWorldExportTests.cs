using AaronOS.Modules.Medical.Import;
using AaronOS.Modules.Medical.Tests.Fixtures;

namespace AaronOS.Modules.Medical.Tests;

/// <summary>
/// Regression cover for behaviour discovered only by running the parser over four real Froedtert
/// exports (21 documents, 1,266 parsed records). Every case here was a visible defect in the review
/// table first: absence statements imported as records, the same childhood vaccination listed five
/// times, and vaccine names that had swallowed their administration dates.
/// </summary>
public class RealWorldExportTests
{
    /// <summary>Problems section carrying Epic's "no known problems" assertion instead of a diagnosis.</summary>
    private const string NoKnownProblemsSection = """
        <section>
          <templateId root="2.16.840.1.113883.10.20.22.2.5.1"/>
          <code code="11450-4" codeSystem="2.16.840.1.113883.6.1"/>
          <title>Active Problems</title>
          <entry>
            <act classCode="ACT" moodCode="EVN">
              <id root="1.2.3" extension="none-1"/>
              <entryRelationship typeCode="SUBJ">
                <observation classCode="OBS" moodCode="EVN">
                  <value xsi:type="CD" code="160245001" displayName="No known active problems"/>
                </observation>
              </entryRelationship>
            </act>
          </entry>
        </section>
        """;

    private const string NoKnownAllergiesSection = """
        <section>
          <templateId root="2.16.840.1.113883.10.20.22.2.6.1"/>
          <code code="48765-2" codeSystem="2.16.840.1.113883.6.1"/>
          <title>Allergies</title>
          <entry>
            <act classCode="ACT" moodCode="EVN">
              <entryRelationship typeCode="SUBJ">
                <observation classCode="OBS" moodCode="EVN">
                  <id root="1.2.3" extension="noalg-1"/>
                  <participant typeCode="CSM">
                    <participantRole classCode="MANU">
                      <playingEntity classCode="MMAT">
                        <code displayName="No known active allergies"/>
                      </playingEntity>
                    </participantRole>
                  </participant>
                </observation>
              </entryRelationship>
            </act>
          </entry>
        </section>
        """;

    /// <summary>
    /// Two immunization entries for the same real vaccination, as two health systems describe it: one
    /// appends "(Given …)", the other a bare date list, and the ids differ because each system minted
    /// its own. Names also differ by trailing whitespace.
    /// </summary>
    private const string MessyImmunizationsSection = """
        <section>
          <templateId root="2.16.840.1.113883.10.20.22.2.2.1"/>
          <code code="11369-6" codeSystem="2.16.840.1.113883.6.1"/>
          <title>Immunizations</title>
          <text>
            <table><tbody>
              <tr><td ID="imm1">DTAP (Infanrix) (Given 3/4/2003, 1/24/2000)</td></tr>
              <tr><td ID="imm2">DTAP (Infanrix)   03/04/2003 , 01/24/2000 </td></tr>
            </tbody></table>
          </text>
          <entry>
            <substanceAdministration classCode="SBADM" moodCode="EVN">
              <id root="1.2.3" extension="froedtert-imm-1"/>
              <consumable><manufacturedProduct classCode="MANU"><manufacturedMaterial>
                <code nullFlavor="UNK"/>
              </manufacturedMaterial></manufacturedProduct></consumable>
              <text><reference value="#imm1"/></text>
            </substanceAdministration>
          </entry>
          <entry>
            <substanceAdministration classCode="SBADM" moodCode="EVN">
              <id root="9.8.7" extension="aurora-imm-99"/>
              <consumable><manufacturedProduct classCode="MANU"><manufacturedMaterial>
                <code nullFlavor="UNK"/>
              </manufacturedMaterial></manufacturedProduct></consumable>
              <text><reference value="#imm2"/></text>
            </substanceAdministration>
          </entry>
        </section>
        """;

    [Fact]
    public void AbsenceStatements_AreNotImportedAsRecords()
    {
        var doc = CcdaParser.Parse(CcdaFixtures.Document(NoKnownProblemsSection, NoKnownAllergiesSection));

        // An allergy list whose first row says "No known active allergies" is worse than an empty one.
        Assert.Empty(doc.Conditions);
        Assert.Empty(doc.Allergies);
        Assert.Equal(0, doc.TotalSkipped);       // nothing failed to parse
        Assert.Equal(2, doc.TotalAbsenceStatements);
    }

    [Fact]
    public void AbsenceStatements_AreCountedNotHidden()
    {
        var doc = CcdaParser.Parse(CcdaFixtures.Document(NoKnownAllergiesSection));

        Assert.Equal(1, doc.AbsenceStatements["Allergies"]);
    }

    [Fact]
    public void RealDiagnosesAreStillImportedAlongsideAbsenceStatements()
    {
        var doc = CcdaParser.Parse(CcdaFixtures.Document(
            NoKnownProblemsSection, CcdaFixtures.ProblemsSection));

        // The genuine Problems section must not be shadowed by the negation one.
        Assert.NotEmpty(doc.Conditions);
        Assert.DoesNotContain(doc.Conditions, c => c.Name.StartsWith("No known", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void VaccineNames_LoseTheirAppendedAdministrationDates()
    {
        var doc = CcdaParser.Parse(CcdaFixtures.Document(MessyImmunizationsSection));

        Assert.All(doc.Immunizations, i =>
        {
            Assert.DoesNotContain("Given", i.Vaccine);
            Assert.DoesNotContain("/", i.Vaccine);
        });
        Assert.All(doc.Immunizations, i => Assert.Equal("DTAP (Infanrix)", i.Vaccine));
    }

    [Fact]
    public void TheSameVaccinationFromTwoSystems_CollapsesToOneRow()
    {
        // Both entries describe one real vaccination but carry different ids, one per health system.
        // Keying de-duplication on the id left five copies of a childhood shot in the review table.
        var doc = CcdaParser.Parse(CcdaFixtures.Document(MessyImmunizationsSection));
        Assert.Equal(2, doc.Immunizations.Count);

        var plan = ImportPlanner.BuildPlan(doc, new ExistingKeys());

        Assert.Single(plan.Rows);
        Assert.Equal(1, plan.NewCount);
    }

    [Fact]
    public void ARecordArrivingUnderANewId_IsRecognisedByItsNaturalKey()
    {
        // Second import from a different health system: unseen id, but the same real-world record.
        var existing = new ExistingKeys();
        existing.Immunizations.Add(ImportPlanner.NaturalKey("DTAP (Infanrix)", null));

        var doc = CcdaParser.Parse(CcdaFixtures.Document(MessyImmunizationsSection));
        var plan = ImportPlanner.BuildPlan(doc, existing);

        Assert.Equal(ImportStatus.AlreadyImported, Assert.Single(plan.Rows).Status);
        Assert.Equal(0, plan.NewCount);
    }

    [Fact]
    public void WhitespaceVariantsOfTheSameNameAreOneRecord()
    {
        var plan = ImportPlanner.BuildPlan(
            new CcdaDocument
            {
                Immunizations =
                [
                    new ParsedImmunization("Influenza Vaccine, Multidose Vial", null, "a"),
                    new ParsedImmunization("Influenza Vaccine, Multidose Vial", null, "b")
                ]
            },
            new ExistingKeys());

        Assert.Single(plan.Rows);
    }

    [Fact]
    public void GenuinelyDifferentDosesOfTheSameVaccineStaySeparate()
    {
        // A five-dose childhood series is five records, not one — de-duplication must not over-collapse.
        var plan = ImportPlanner.BuildPlan(
            new CcdaDocument
            {
                Immunizations =
                [
                    new ParsedImmunization("DTAP", new DateOnly(1999, 1, 13), "a"),
                    new ParsedImmunization("DTAP", new DateOnly(1999, 3, 4), "b"),
                    new ParsedImmunization("DTAP", new DateOnly(2000, 1, 24), "c")
                ]
            },
            new ExistingKeys());

        Assert.Equal(3, plan.Rows.Count);
        Assert.Equal(3, plan.NewCount);
    }
}
