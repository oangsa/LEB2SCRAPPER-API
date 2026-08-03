using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using LEB2SCRAPPER.Entity.Exceptions.Leb2Integration;
using LEB2SCRAPPER.Entity.Models.Class;
using LEB2SCRAPPER.Entity.Models.Semester;

namespace LEB2SCRAPPER.Repository.Parsing;

internal sealed class Leb2RenderedPageParser
{
    private readonly HtmlParser _htmlParser = new();

    public List<SemesterInfo> ParseSemesters(string pageSource)
    {
        var document = _htmlParser.ParseDocument(pageSource);
        var semesterLinks = document.QuerySelectorAll("a[href]");

        if (semesterLinks.Length == 0)
        {
            throw new StructuralParseException(
                "semesters.semester_links",
                "The semester links no longer match the expected structure.");
        }

        var semesters = new List<SemesterInfo>();
        var semesterNamesById = new Dictionary<int, string>();
        var candidateCount = 0;

        foreach (var link in semesterLinks)
        {
            var href = link.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href))
            {
                continue;
            }

            if (!TryGetSemesterId(href, out var parsedIdCandidate))
            {
                continue;
            }

            candidateCount++;
            var name = NormalizeSemesterName(link.TextContent);

            if (!parsedIdCandidate.HasValue
                || string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var parsedId = parsedIdCandidate.Value;

            if (semesterNamesById.TryGetValue(parsedId, out var existingName))
            {
                if (!string.Equals(existingName, name, StringComparison.Ordinal))
                {
                    throw new StructuralParseException(
                        "semesters.semester_link_conflict",
                        "LEB2 returned conflicting names for the same semester ID.");
                }

                continue;
            }

            semesterNamesById[parsedId] = name;
            semesters.Add(new SemesterInfo
            {
                Id = parsedId,
                Name = name
            });
        }

        if (candidateCount == 0)
        {
            throw new StructuralParseException(
                "semesters.semester_links",
                "The semester links no longer match the expected structure.");
        }

        if (semesters.Count == 0)
        {
            throw new StructuralParseException(
                "semesters.semester_link_values",
                "The semester links did not contain recognizable IDs.");
        }

        return semesters;
    }

    public bool HasUsableSemesterLink(string pageSource)
    {
        return _htmlParser.ParseDocument(pageSource)
            .QuerySelectorAll("a[href]")
            .Any(link => IsUsableSemesterLink(
                link.GetAttribute("href"),
                link.TextContent));
    }

    internal static bool TryGetSemesterId(string? href, out int? parsedId)
    {
        parsedId = null;

        if (string.IsNullOrWhiteSpace(href))
        {
            return false;
        }

        var fragmentIndex = href.IndexOf('#');
        var queryEnd = fragmentIndex >= 0 ? fragmentIndex : href.Length;
        var queryStart = href.IndexOf('?');
        if (queryStart < 0 || queryStart >= queryEnd)
        {
            return false;
        }

        var query = href[(queryStart + 1)..queryEnd];
        foreach (var pair in query.Split('&'))
        {
            var separatorIndex = pair.IndexOf('=');
            var encodedKey = separatorIndex >= 0
                ? pair[..separatorIndex]
                : pair;
            var encodedValue = separatorIndex >= 0
                ? pair[(separatorIndex + 1)..]
                : string.Empty;

            string key;
            try
            {
                key = Uri.UnescapeDataString(
                    encodedKey.Replace('+', ' '));
            }
            catch (UriFormatException)
            {
                continue;
            }

            if (!string.Equals(key, "semester_id", StringComparison.Ordinal))
            {
                continue;
            }

            string value;
            try
            {
                value = Uri.UnescapeDataString(
                    encodedValue.Replace('+', ' '));
            }
            catch (UriFormatException)
            {
                return true;
            }

            if (int.TryParse(
                    value,
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var valueAsInt)
                && valueAsInt > 0)
            {
                parsedId = valueAsInt;
            }

            return true;
        }

        return false;
    }

    internal static string NormalizeSemesterName(string? text)
    {
        return Regex.Replace(
                text ?? string.Empty,
                @"\s+",
                " ",
                RegexOptions.CultureInvariant)
            .Trim();
    }

    internal static bool IsUsableSemesterLink(string? href, string? text)
    {
        return TryGetSemesterId(href, out var parsedId)
            && parsedId.HasValue
            && !string.IsNullOrWhiteSpace(NormalizeSemesterName(text));
    }

    public List<ClassInfo> ParseClasses(string pageSource)
    {
        var document = _htmlParser.ParseDocument(pageSource);
        var publishedContainer = document.QuerySelector(
            "#classListMain .class-list__row.class-publish");

        if (publishedContainer is null)
        {
            throw new StructuralParseException(
                "classes.class_cards",
                "The class cards no longer match the expected structure.");
        }

        var classes = new List<ClassInfo>();

        foreach (var publishedItem in publishedContainer.Children)
        {
            var classCards = publishedItem.LocalName == "div"
                && publishedItem.ClassList.Contains("whole-card")
                ? publishedItem.Children
                    .Where(IsPublishedClassCard)
                    .ToList()
                : new List<IElement>();

            if (classCards.Count != 1)
            {
                throw new StructuralParseException(
                    "classes.class_card_pairing",
                    "LEB2 returned malformed published class card markup.");
            }

            var classCard = classCards.Single();
            var classCodes = classCard.QuerySelectorAll(
                "p[name='code'].card-title-class-list");
            var classIdWithName = classCard.GetAttribute("name");
            var idMatch = Regex.Match(
                classIdWithName ?? string.Empty,
                @"^card-(\d+)$");
            var className = classCodes.Length == 1
                ? classCodes[0].TextContent.Trim()
                : null;

            if (classCodes.Length != 1
                || string.IsNullOrWhiteSpace(classIdWithName)
                || !idMatch.Success
                || !int.TryParse(idMatch.Groups[1].Value, out var classId)
                || string.IsNullOrWhiteSpace(className))
            {
                throw new StructuralParseException(
                    "classes.class_card_values",
                    "The class cards did not contain recognizable class data.");
            }

            classes.Add(new ClassInfo
            {
                Id = classId,
                Name = className
            });
        }

        return classes;
    }

    private static bool IsPublishedClassCard(IElement element)
    {
        return element.LocalName == "div"
            && element.ClassList.Contains("class-card")
            && element.ClassList.Contains("card")
            && element.HasAttribute("name")
            && element.HasAttribute("data-url");
    }
}
