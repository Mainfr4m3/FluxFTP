using System.IO;
using System.Xml.Linq;

namespace IoFtp.Desktop.Services;

internal static class CaptionBookmarkImporter
{
    public static IReadOnlyList<SiteBookmark> Import(string path, string siteName)
    {
        var document = XDocument.Load(path, LoadOptions.None);
        if (document.Root is null) throw new InvalidDataException("The XML file has no root element.");
        var result = new List<SiteBookmark>();
        foreach (var element in document.Root.DescendantsAndSelf())
        {
            var caption = Value(element, "CAPTION").Trim();
            var remote = Value(element, "REMOTE", "REMOTEPATH", "REMOTE_PATH").Trim().Replace('\\', '/');
            if (caption.Length == 0 || remote.Length == 0) continue;
            if (!remote.StartsWith('/')) remote = "/" + remote;
            result.Add(new(caption, remote, siteName));
        }
        return result.DistinctBy(item => $"{item.Name}\0{item.Path}", StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string Value(XElement element, params string[] names)
    {
        foreach (var name in names)
        {
            var attribute = element.Attributes().FirstOrDefault(item =>
                item.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (attribute is not null) return attribute.Value;
            var child = element.Elements().FirstOrDefault(item =>
                item.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (child is not null) return child.Value;
        }
        return "";
    }
}
