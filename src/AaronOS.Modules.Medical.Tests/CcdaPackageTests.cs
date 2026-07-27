using System.IO.Compression;
using AaronOS.Modules.Medical.Import;
using AaronOS.Modules.Medical.Tests.Fixtures;

namespace AaronOS.Modules.Medical.Tests;

/// <summary>
/// Covers what a real MyChart download actually is: a zip in IHE XDM layout holding a folder of
/// C-CDA documents plus a metadata file, a stylesheet, an HTML copy and a PDF. Written after
/// discovering that one Froedtert download contained eight separate documents and four downloads
/// contained twenty-one — the original single-XML assumption would have read none of them.
/// </summary>
public class CcdaPackageTests : IDisposable
{
    private readonly List<string> _temp = [];

    private string TempPath(string extension)
    {
        var path = Path.Combine(Path.GetTempPath(), $"aaronos-pkg-{Guid.NewGuid():N}{extension}");
        _temp.Add(path);
        return path;
    }

    private string WriteXml(string xml)
    {
        var path = TempPath(".xml");
        File.WriteAllText(path, xml);
        return path;
    }

        /// <summary>Builds a zip shaped like Epic's export, including the sidecars that must be ignored.</summary>
    private string WriteXdmZip(int documentCount, bool includeSidecars = true)
    {
        var path = TempPath(".zip");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);

        for (var i = 1; i <= documentCount; i++)
        {
            var entry = archive.CreateEntry($"IHE_XDM/Aaron1/DOC{i:0000}.XML");
            using var writer = new StreamWriter(entry.Open());
            writer.Write(CcdaFixtures.Document(CcdaFixtures.ProblemsSection));
        }

        if (includeSidecars)
        {
            using (var w = new StreamWriter(archive.CreateEntry("IHE_XDM/Aaron1/METADATA.XML").Open()))
            {
                w.Write("<Manifest><Document id=\"1\"/></Manifest>");
            }
            using (var w = new StreamWriter(archive.CreateEntry("IHE_XDM/Aaron1/STYLE.XSL").Open()))
            {
                w.Write("<xsl:stylesheet xmlns:xsl=\"http://www.w3.org/1999/XSL/Transform\"/>");
            }
            using (var w = new StreamWriter(archive.CreateEntry("INDEX.HTM").Open()))
            {
                w.Write("<html><body>rendered copy</body></html>");
            }
            using (var w = new StreamWriter(archive.CreateEntry("1 of 1 - My Health Summary.PDF").Open()))
            {
                w.Write("%PDF-1.4 not really a pdf");
            }
        }

        return path;
    }

    [Fact]
    public void ReadsEveryDocumentFromAnXdmZip_AndIgnoresTheSidecars()
    {
        var zip = WriteXdmZip(documentCount: 8);

        var documents = CcdaPackage.ReadDocuments(zip);

        // METADATA.XML is also .xml, so a naive extension filter would have returned nine.
        Assert.Equal(8, documents.Count);
        Assert.All(documents, d => Assert.Contains("ClinicalDocument", d));
    }

    [Fact]
    public void ReadsAPlainXmlFileUnchanged()
    {
        var path = WriteXml(CcdaFixtures.Document(CcdaFixtures.ProblemsSection));

        var documents = CcdaPackage.ReadDocuments(path);

        Assert.Single(documents);
    }

    [Fact]
    public void DetectsAZipByItsBytes_NotItsExtension()
    {
        // Epic has been known to hand out .xml names for zipped payloads.
        var zip = WriteXdmZip(documentCount: 2);
        var renamed = Path.ChangeExtension(zip, ".xml");
        File.Move(zip, renamed);
        _temp.Add(renamed);

        Assert.Equal(2, CcdaPackage.ReadDocuments(renamed).Count);
    }

    [Fact]
    public void ThrowsAHelpfulError_WhenAZipHoldsNoRecords()
    {
        var path = TempPath(".zip");
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
        using (var w = new StreamWriter(archive.CreateEntry("1 of 1 - My Health Summary.PDF").Open()))
        {
            w.Write("%PDF-1.4");
        }

        var ex = Assert.Throws<FormatException>(() => CcdaPackage.ReadDocuments(path));
        Assert.Contains("health summary", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ParseMany_MergesEveryDocumentAndCountsThem()
    {
        var documents = CcdaPackage.ReadDocuments(WriteXdmZip(documentCount: 3));

        var parsed = CcdaParser.ParseMany(documents);

        Assert.Equal(3, parsed.DocumentCount);
        Assert.Equal(6, parsed.Conditions.Count); // 2 problems per document, de-duplicated later
        Assert.Equal(0, parsed.TotalSkipped);
    }

    [Fact]
    public void ParseMany_LeavesDeduplicationToThePlanner()
    {
        // The same records repeat across documents in a real export; the planner is the single place
        // that collapses them, so parsing must not silently drop any.
        var parsed = CcdaParser.ParseMany(CcdaPackage.ReadDocuments(WriteXdmZip(documentCount: 4)));

        var plan = ImportPlanner.BuildPlan(parsed, new ExistingKeys());

        Assert.Equal(8, parsed.Conditions.Count);
        Assert.Equal(2, plan.Rows.Count);   // 2 genuinely distinct problems
        Assert.Equal(2, plan.NewCount);
    }

    [Fact]
    public void ParseMany_SurvivesOneUnreadableDocumentAmongGoodOnes()
    {
        var documents = new List<string>
        {
            CcdaFixtures.Document(CcdaFixtures.ProblemsSection),
            "<html>not a record</html>",
            CcdaFixtures.Document(CcdaFixtures.ResultsSection)
        };

        var parsed = CcdaParser.ParseMany(documents);

        Assert.Equal(2, parsed.DocumentCount);
        Assert.Equal(2, parsed.Conditions.Count);
        Assert.Equal(2, parsed.Labs.Count);
        Assert.Contains("Document", parsed.SkippedBySection.Keys);
        Assert.NotEmpty(parsed.Warnings);
    }

    [Fact]
    public void ParseMany_ThrowsWhenNothingAtAllCouldBeRead()
    {
        Assert.Throws<FormatException>(() => CcdaParser.ParseMany(["<html/>", "<nope/>"]));
    }

    public void Dispose()
    {
        foreach (var path in _temp)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
                // A leftover temp file is not worth failing a test over.
            }
        }
    }
}
