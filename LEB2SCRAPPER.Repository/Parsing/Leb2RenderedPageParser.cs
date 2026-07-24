using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using LEB2SCRAPPER.Entity.Exceptions.Leb2Integration;
using LEB2SCRAPPER.Entity.Models.Class;

namespace LEB2SCRAPPER.Repository.Parsing;

internal sealed class Leb2RenderedPageParser
{
    private readonly HtmlParser _htmlParser = new();

    public List<int> ParseSemesterIds(string pageSource)
    {
        var document = _htmlParser.ParseDocument(pageSource);
        var semesterLinks = document.QuerySelectorAll("a[href*='semester_id=']");

        if (semesterLinks.Length == 0)
        {
            throw new StructuralParseException(
                "semesters.semester_links",
                "The semester links no longer match the expected structure.");
        }

        var semesterIds = semesterLinks
            .Select(link => link.GetAttribute("href"))
            .Where(href => !string.IsNullOrEmpty(href))
            .Select(href => Regex.Match(href!, @"semester_id=(\d+)"))
            .Where(match => match.Success)
            .Select(match => int.TryParse(match.Groups[1].Value, out var semesterId)
                ? semesterId
                : 0)
            .Where(semesterId => semesterId > 0)
            .Distinct()
            .ToList();

        if (semesterIds.Count == 0)
        {
            throw new StructuralParseException(
                "semesters.semester_link_values",
                "The semester links did not contain recognizable IDs.");
        }

        return semesterIds;
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
