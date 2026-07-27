namespace AaronOS.Modules.Medical.Tests.Fixtures;

/// <summary>
/// C-CDA R2.1 section fragments authored from the specification. These are what let the parser be
/// developed and refined without the user's real MyChart export, and what will catch a regression
/// when it is later refined against that export.
///
/// Each fixture deliberately exercises a shape real documents use and a naive parser gets wrong:
/// values carried in the narrative rather than a coded attribute, two effectiveTime elements on one
/// medication, severity distinguished from reaction only by its code, and body weight sitting in
/// vital signs waiting to be wrongly imported.
/// </summary>
public static class CcdaFixtures
{
    /// <summary>Wraps section XML in the minimal ClinicalDocument envelope a parser must walk.</summary>
    public static string Document(params string[] sections)
    {
        var components = string.Join("\n", sections.Select(s => $"<component>{s}</component>"));
        return $"""
            <ClinicalDocument xmlns="urn:hl7-org:v3" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance">
              <recordTarget>
                <patientRole>
                  <patient><name><given>Test</given><family>Patient</family></name></patient>
                </patientRole>
              </recordTarget>
              <component>
                <structuredBody>
                  {components}
                </structuredBody>
              </component>
            </ClinicalDocument>
            """;
    }

    /// <summary>
    /// Two problems. The first names itself via value/@displayName. The second has no displayName at
    /// all and must be resolved through the narrative reference — and carries a high date, which is
    /// what marks a condition resolved.
    /// </summary>
    public const string ProblemsSection = """
        <section>
          <templateId root="2.16.840.1.113883.10.20.22.2.5.1"/>
          <code code="11450-4" codeSystem="2.16.840.1.113883.6.1"/>
          <title>Problems</title>
          <text>
            <table><tbody>
              <tr><td ID="prob1">Seasonal allergic rhinitis</td></tr>
            </tbody></table>
          </text>
          <entry>
            <act classCode="ACT" moodCode="EVN">
              <id root="1.2.3" extension="cond-1"/>
              <entryRelationship typeCode="SUBJ">
                <observation classCode="OBS" moodCode="EVN">
                  <code code="55607006" codeSystem="2.16.840.1.113883.6.96"/>
                  <effectiveTime><low value="20200115"/></effectiveTime>
                  <value xsi:type="CD" code="59621000" displayName="Essential hypertension"/>
                </observation>
              </entryRelationship>
            </act>
          </entry>
          <entry>
            <act classCode="ACT" moodCode="EVN">
              <id root="1.2.3" extension="cond-2"/>
              <entryRelationship typeCode="SUBJ">
                <observation classCode="OBS" moodCode="EVN">
                  <code code="55607006" codeSystem="2.16.840.1.113883.6.96"/>
                  <effectiveTime><low value="20180301"/><high value="20190601"/></effectiveTime>
                  <value xsi:type="CD" code="195967001"/>
                  <text><reference value="#prob1"/></text>
                </observation>
              </entryRelationship>
            </act>
          </entry>
        </section>
        """;

    /// <summary>
    /// One medication with two effectiveTime elements — IVL_TS for the date range and PIVL_TS for the
    /// frequency. Reading the wrong one yields nonsense dates.
    /// </summary>
    public const string MedicationsSection = """
        <section>
          <templateId root="2.16.840.1.113883.10.20.22.2.1.1"/>
          <code code="10160-0" codeSystem="2.16.840.1.113883.6.1"/>
          <title>Medications</title>
          <text><table><tbody><tr><td ID="med1">Lisinopril</td></tr></tbody></table></text>
          <entry>
            <substanceAdministration classCode="SBADM" moodCode="EVN">
              <id root="1.2.3" extension="med-1"/>
              <effectiveTime xsi:type="IVL_TS">
                <low value="20240101"/>
                <high value="20250101"/>
              </effectiveTime>
              <effectiveTime xsi:type="PIVL_TS" operator="A">
                <period value="1" unit="d"/>
              </effectiveTime>
              <routeCode code="C38288" displayName="Oral"/>
              <doseQuantity value="10" unit="mg"/>
              <consumable>
                <manufacturedProduct classCode="MANU">
                  <manufacturedMaterial>
                    <code code="314076" codeSystem="2.16.840.1.113883.6.88" displayName="Lisinopril 10 MG"/>
                  </manufacturedMaterial>
                </manufacturedProduct>
              </consumable>
            </substanceAdministration>
          </entry>
        </section>
        """;

    /// <summary>
    /// One allergy. Substance sits in participant/playingEntity, while reaction and severity are both
    /// nested observations distinguishable only by the severity one being coded SEV.
    /// </summary>
    public const string AllergiesSection = """
        <section>
          <templateId root="2.16.840.1.113883.10.20.22.2.6.1"/>
          <code code="48765-2" codeSystem="2.16.840.1.113883.6.1"/>
          <title>Allergies</title>
          <entry>
            <act classCode="ACT" moodCode="EVN">
              <id root="1.2.3" extension="alg-act-1"/>
              <entryRelationship typeCode="SUBJ">
                <observation classCode="OBS" moodCode="EVN">
                  <id root="1.2.3" extension="alg-1"/>
                  <value xsi:type="CD" code="419511003" displayName="Propensity to adverse reactions to drug"/>
                  <participant typeCode="CSM">
                    <participantRole classCode="MANU">
                      <playingEntity classCode="MMAT">
                        <code code="7980" codeSystem="2.16.840.1.113883.6.88" displayName="Penicillin G"/>
                      </playingEntity>
                    </participantRole>
                  </participant>
                  <entryRelationship typeCode="MFST">
                    <observation classCode="OBS" moodCode="EVN">
                      <code code="ASSERTION" codeSystem="2.16.840.1.113883.5.4"/>
                      <value xsi:type="CD" code="247472004" displayName="Hives"/>
                    </observation>
                  </entryRelationship>
                  <entryRelationship typeCode="SUBJ">
                    <observation classCode="OBS" moodCode="EVN">
                      <code code="SEV" codeSystem="2.16.840.1.113883.5.4"/>
                      <value xsi:type="CD" code="6736007" displayName="Moderate"/>
                    </observation>
                  </entryRelationship>
                </observation>
              </entryRelationship>
            </act>
          </entry>
        </section>
        """;

    public const string ImmunizationsSection = """
        <section>
          <templateId root="2.16.840.1.113883.10.20.22.2.2.1"/>
          <code code="11369-6" codeSystem="2.16.840.1.113883.6.1"/>
          <title>Immunizations</title>
          <entry>
            <substanceAdministration classCode="SBADM" moodCode="EVN">
              <id root="1.2.3" extension="imm-1"/>
              <effectiveTime value="20210310"/>
              <consumable>
                <manufacturedProduct classCode="MANU">
                  <manufacturedMaterial>
                    <code code="88" codeSystem="2.16.840.1.113883.12.292" displayName="Influenza vaccine"/>
                  </manufacturedMaterial>
                </manufacturedProduct>
              </consumable>
            </substanceAdministration>
          </entry>
        </section>
        """;

    /// <summary>
    /// A numeric result with a two-sided reference range, and a textual one ("Negative") with no
    /// range — both shapes appear in real lab panels.
    /// </summary>
    public const string ResultsSection = """
        <section>
          <templateId root="2.16.840.1.113883.10.20.22.2.3.1"/>
          <code code="30954-2" codeSystem="2.16.840.1.113883.6.1"/>
          <title>Results</title>
          <entry>
            <organizer classCode="BATTERY" moodCode="EVN">
              <code code="58410-2" displayName="CBC panel"/>
              <component>
                <observation classCode="OBS" moodCode="EVN">
                  <id root="1.2.3" extension="lab-1"/>
                  <code code="718-7" codeSystem="2.16.840.1.113883.6.1" displayName="Hemoglobin"/>
                  <effectiveTime value="20260101"/>
                  <value xsi:type="PQ" value="14.2" unit="g/dL"/>
                  <referenceRange>
                    <observationRange>
                      <value xsi:type="IVL_PQ">
                        <low value="13.5" unit="g/dL"/>
                        <high value="17.5" unit="g/dL"/>
                      </value>
                    </observationRange>
                  </referenceRange>
                </observation>
              </component>
              <component>
                <observation classCode="OBS" moodCode="EVN">
                  <id root="1.2.3" extension="lab-2"/>
                  <code code="5195-3" codeSystem="2.16.840.1.113883.6.1" displayName="Hepatitis B surface antigen"/>
                  <effectiveTime value="20260101"/>
                  <value xsi:type="ST">Negative</value>
                </observation>
              </component>
            </organizer>
          </entry>
        </section>
        """;

    public const string ProceduresSection = """
        <section>
          <templateId root="2.16.840.1.113883.10.20.22.2.7.1"/>
          <code code="47519-4" codeSystem="2.16.840.1.113883.6.1"/>
          <title>Procedures</title>
          <entry>
            <procedure classCode="PROC" moodCode="EVN">
              <id root="1.2.3" extension="proc-1"/>
              <code code="80146002" codeSystem="2.16.840.1.113883.6.96" displayName="Appendectomy"/>
              <effectiveTime value="20180412"/>
            </procedure>
          </entry>
        </section>
        """;

    public const string EncountersSection = """
        <section>
          <templateId root="2.16.840.1.113883.10.20.22.2.22.1"/>
          <code code="46240-8" codeSystem="2.16.840.1.113883.6.1"/>
          <title>Encounters</title>
          <entry>
            <encounter classCode="ENC" moodCode="EVN">
              <id root="1.2.3" extension="enc-1"/>
              <code code="99213" codeSystem="2.16.840.1.113883.6.12" displayName="Office visit"/>
              <effectiveTime><low value="20260115"/></effectiveTime>
              <participant typeCode="LOC">
                <participantRole classCode="SDLOC">
                  <playingEntity classCode="PLC">
                    <name>Springfield Clinic</name>
                  </playingEntity>
                </participantRole>
              </participant>
            </encounter>
          </entry>
        </section>
        """;

    /// <summary>
    /// Systolic, heart rate, and body weight. Body weight is here specifically so a test can prove it
    /// is excluded — Body Measurements owns that number.
    /// </summary>
    public const string VitalSignsSection = """
        <section>
          <templateId root="2.16.840.1.113883.10.20.22.2.4.1"/>
          <code code="8716-3" codeSystem="2.16.840.1.113883.6.1"/>
          <title>Vital signs</title>
          <entry>
            <organizer classCode="CLUSTER" moodCode="EVN">
              <code code="46680005" displayName="Vital signs"/>
              <component>
                <observation classCode="OBS" moodCode="EVN">
                  <id root="1.2.3" extension="vit-1"/>
                  <code code="8480-6" codeSystem="2.16.840.1.113883.6.1" displayName="Systolic blood pressure"/>
                  <effectiveTime value="20260115"/>
                  <value xsi:type="PQ" value="128" unit="mm[Hg]"/>
                </observation>
              </component>
              <component>
                <observation classCode="OBS" moodCode="EVN">
                  <id root="1.2.3" extension="vit-2"/>
                  <code code="8867-4" codeSystem="2.16.840.1.113883.6.1" displayName="Heart rate"/>
                  <effectiveTime value="20260115"/>
                  <value xsi:type="PQ" value="72" unit="/min"/>
                </observation>
              </component>
              <component>
                <observation classCode="OBS" moodCode="EVN">
                  <id root="1.2.3" extension="vit-3"/>
                  <code code="29463-7" codeSystem="2.16.840.1.113883.6.1" displayName="Body weight"/>
                  <effectiveTime value="20260115"/>
                  <value xsi:type="PQ" value="240" unit="[lb_av]"/>
                </observation>
              </component>
            </organizer>
          </entry>
        </section>
        """;

    /// <summary>A problems section whose single entry has no value and no resolvable narrative, so no
    /// name can be derived. Must be counted as skipped rather than crashing or vanishing.</summary>
    public const string MalformedProblemsSection = """
        <section>
          <templateId root="2.16.840.1.113883.10.20.22.2.5.1"/>
          <code code="11450-4" codeSystem="2.16.840.1.113883.6.1"/>
          <title>Problems</title>
          <entry>
            <act classCode="ACT" moodCode="EVN">
              <id root="1.2.3" extension="cond-bad"/>
              <entryRelationship typeCode="SUBJ">
                <observation classCode="OBS" moodCode="EVN">
                  <effectiveTime><low value="20200101"/></effectiveTime>
                  <value xsi:type="CD" nullFlavor="UNK"/>
                </observation>
              </entryRelationship>
            </act>
          </entry>
        </section>
        """;

    /// <summary>Problems section identified only by its LOINC code — some producers omit or version
    /// the templateId, and the parser must still find it.</summary>
    public const string ProblemsSectionWithoutTemplateId = """
        <section>
          <code code="11450-4" codeSystem="2.16.840.1.113883.6.1"/>
          <title>Problems</title>
          <entry>
            <act classCode="ACT" moodCode="EVN">
              <id root="1.2.3" extension="cond-loinc"/>
              <entryRelationship typeCode="SUBJ">
                <observation classCode="OBS" moodCode="EVN">
                  <effectiveTime><low value="20220505"/></effectiveTime>
                  <value xsi:type="CD" code="73211009" displayName="Diabetes mellitus"/>
                </observation>
              </entryRelationship>
            </act>
          </entry>
        </section>
        """;
}
