using LEB2SCRAPPER.Entity.Exceptions.Leb2Integration;
using LEB2SCRAPPER.Entity.Models.Semester;
using LEB2SCRAPPER.Repository.Parsing;

namespace LEB2SCRAPPER.Tests.Repository;

public class Leb2RenderedPageParserTests
{
    [Theory]
    [InlineData("/class?semester_id=46", true)]
    [InlineData("/class?semester_id=46&tab=all", true)]
    [InlineData("https://app.leb2.org/class?semester_id=46", true)]
    [InlineData("/class?other_semester_id=46", false)]
    [InlineData("/class?redirect=/foo?semester_id=46", false)]
    [InlineData("/class#?semester_id=46", false)]
    public void TryGetSemesterId_RequiresExactQueryParameter(
        string href,
        bool expected)
    {
        var recognized = Leb2RenderedPageParser.TryGetSemesterId(
            href,
            out var parsedId);

        Assert.Equal(expected, recognized);

        if (expected)
        {
            Assert.Equal(46, parsedId);
        }
        else
        {
            Assert.Null(parsedId);
        }
    }

    [Theory]
    [InlineData("/class?semester_id=46", "", false)]
    [InlineData("/class?semester_id=46", "   \t\r\n", false)]
    [InlineData("/class?semester_id=46", "  1/2026 \t", true)]
    [InlineData("/class?other_semester_id=46", "1/2026", false)]
    public void IsUsableSemesterLink_RequiresRecognizableIdAndName(
        string href,
        string text,
        bool expected)
    {
        Assert.Equal(
            expected,
            Leb2RenderedPageParser.IsUsableSemesterLink(href, text));
    }

    [Theory]
    [InlineData("<a href=\"/class\">Home</a>", false)]
    [InlineData("<a href=\"/class?semester_id=46\"></a>", false)]
    [InlineData("<a href=\"/class?semester_id=46\">1/2026</a>", true)]
    [InlineData(
        "<div style=\"display:none\"><a href=\"/class?semester_id=46\">1/2026</a></div>",
        true)]
    public void HasUsableSemesterLink_MatchesWhatParseSemestersAccepts(
        string body,
        bool expected)
    {
        var parser = new Leb2RenderedPageParser();
        var html = $"<html><body>{body}</body></html>";

        Assert.Equal(expected, parser.HasUsableSemesterLink(html));

        if (expected)
        {
            Assert.Single(parser.ParseSemesters(html));
        }
    }

    [Fact]
    public void ParseSemesters_MapsAbsoluteHrefAndVisibleName()
    {
        var parser = new Leb2RenderedPageParser();
        const string html = """
            <html>
              <body>
                <a href="https://app.leb2.org/class?other_semester_id=99&amp;semester_id=46&amp;tab=all">
                  1/2026
                </a>
              </body>
            </html>
            """;

        var semester = Assert.Single(parser.ParseSemesters(html));

        Assert.Equal(46, semester.Id);
        Assert.Equal("1/2026", semester.Name);
    }

    [Fact]
    public void ParseSemesters_NormalizesRelativeLinkTextAndPreservesOrder()
    {
        var parser = new Leb2RenderedPageParser();
        const string html = """
            <a href="/class?tab=all&amp;semester_id=10">  2/2026  </a>
            <a href="/class?semester_id=11">3/2026</a>
            <a href="/class?semester_id=10">  2/2026 </a>
            """;

        var semesters = parser.ParseSemesters(html);

        Assert.Equal([10, 11], semesters.Select(semester => semester.Id));
        Assert.Equal(["2/2026", "3/2026"], semesters.Select(semester => semester.Name));
    }

    [Fact]
    public void ParseSemesters_ConflictingDuplicateNamesFailStructurally()
    {
        var parser = new Leb2RenderedPageParser();
        const string html = """
            <a href="/class?semester_id=10">2/2026</a>
            <a href="/class?semester_id=10">3/2026</a>
            """;

        var exception = Assert.Throws<StructuralParseException>(
            () => parser.ParseSemesters(html));

        Assert.Equal("semesters.semester_link_conflict", exception.FailureShape);
    }

    [Fact]
    public void ParseSemesters_WithOnlyMalformedCandidatesFailsWithoutInvalidOutput()
    {
        var parser = new Leb2RenderedPageParser();
        const string html = """
            <a href="/class?semester_id=">Missing</a>
            <a href="/class?semester_id=abc">Not numeric</a>
            <a href="/class?semester_id=0">Zero</a>
            <a href="/class?semester_id=12">   </a>
            <a href="/class?other_semester_id=13">Other parameter</a>
            """;

        var exception = Assert.Throws<StructuralParseException>(
            () => parser.ParseSemesters(html));

        Assert.Equal("semesters.semester_link_values", exception.FailureShape);
    }

    [Fact]
    public void ParseSemesters_IgnoresFragmentAndNestedQuerySemesterIds()
    {
        var parser = new Leb2RenderedPageParser();
        const string html = """
            <a href="/class#?semester_id=46">Fragment candidate</a>
            <a href="/class?redirect=/other?semester_id=47">Nested query candidate</a>
            """;

        var exception = Assert.Throws<StructuralParseException>(
            () => parser.ParseSemesters(html));

        Assert.Equal("semesters.semester_links", exception.FailureShape);
    }

    [Fact]
    public void ParseClasses_MapsPublishedCardsAndAllowsEmptySection()
    {
        var parser = new Leb2RenderedPageParser();
        const string html = """
            <html>
              <body>
                <main id="classListMain">
                  <section class="class-list__row class-publish">
                    <div class="whole-card">
                      <div class="class-card card" name="card-101" data-url="/class/101">
                        <p name="code" class="card-title-class-list">CPE 101</p>
                      </div>
                    </div>
                    <div class="whole-card">
                      <div class="class-card card" name="card-202" data-url="/class/202">
                        <p name="code" class="card-title-class-list">CPE 202</p>
                      </div>
                    </div>
                  </section>
                </main>
              </body>
            </html>
            """;
        const string emptyHtml = """
            <main id="classListMain">
              <section class="class-list__row class-publish"></section>
            </main>
            """;

        var classes = parser.ParseClasses(html);
        var emptyClasses = parser.ParseClasses(emptyHtml);

        Assert.Equal([101, 202], classes.Select(classInfo => classInfo.Id));
        Assert.Equal(["CPE 101", "CPE 202"], classes.Select(classInfo => classInfo.Name));
        Assert.Empty(emptyClasses);
    }

    [Fact]
    public void ParseClasses_WithMalformedCard_PreservesStructuralFailureShape()
    {
        var parser = new Leb2RenderedPageParser();
        const string html = """
            <main id="classListMain">
              <section class="class-list__row class-publish">
                <div class="whole-card">
                  <div class="class-card card" name="unexpected" data-url="/class">
                    <p name="code" class="card-title-class-list">CPE 101</p>
                  </div>
                </div>
              </section>
            </main>
            """;

        var exception = Assert.Throws<StructuralParseException>(
            () => parser.ParseClasses(html));

        Assert.Equal("classes.class_card_values", exception.FailureShape);
    }
}
