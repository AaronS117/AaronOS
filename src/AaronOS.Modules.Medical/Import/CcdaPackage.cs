using System.IO;
using System.IO.Compression;
using System.Xml.Linq;

namespace AaronOS.Modules.Medical.Import;

/// <summary>
/// Pulls the C-CDA documents out of whatever MyChart actually hands you.
///
/// A real Epic export is not one XML file. It is a zip in IHE XDM layout containing a whole folder
/// of documents — <c>IHE_XDM/&lt;name&gt;/DOC0001.XML</c> … <c>DOC000N.XML</c> — alongside a
/// METADATA.XML, an XSL stylesheet, a rendered HTML copy and a PDF. One Froedtert download held
/// eight separate C-CDA documents; four downloads held twenty-one between them.
///
/// Rather than hardcoding Epic's folder names, every .xml entry is opened and kept only if its root
/// element really is a ClinicalDocument. That excludes METADATA.XML and any future sidecar without
/// needing to know what it is called, and it means a plain .xml download still works unchanged.
/// </summary>
public static class CcdaPackage
{
    private static readonly XNamespace V = "urn:hl7-org:v3";

    /// <summary>Every C-CDA document found in the file, as XML text. A bare .xml file yields one.</summary>
    public static List<string> ReadDocuments(string path)
    {
        var documents = IsZip(path) ? ReadFromZip(path) : [File.ReadAllText(path)];

        if (documents.Count == 0)
        {
            throw new FormatException(
                "No health records were found in that file. If it came from MyChart, choose the " +
                "health summary download rather than the PDF copy.");
        }

        return documents;
    }

    /// <summary>Sniffs the actual bytes rather than trusting the extension.</summary>
    private static bool IsZip(string path)
    {
        using var stream = File.OpenRead(path);
        return stream.Length >= 2 && stream.ReadByte() == 'P' && stream.ReadByte() == 'K';
    }

    private static List<string> ReadFromZip(string path)
    {
        var documents = new List<string>();

        using var archive = ZipFile.OpenRead(path);
        foreach (var entry in archive.Entries
            .Where(e => e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .OrderBy(e => e.FullName, StringComparer.OrdinalIgnoreCase))
        {
            string text;
            try
            {
                using var reader = new StreamReader(entry.Open());
                text = reader.ReadToEnd();
            }
            catch (Exception)
            {
                continue; // an unreadable entry is not a reason to abandon the rest
            }

            if (LooksLikeClinicalDocument(text))
            {
                documents.Add(text);
            }
        }

        return documents;
    }

    private static bool LooksLikeClinicalDocument(string xml)
    {
        try
        {
            return XDocument.Parse(xml).Root?.Name == V + "ClinicalDocument";
        }
        catch (Exception)
        {
            return false;
        }
    }
}
