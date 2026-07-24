using LEB2SCRAPPER.Entity.Exceptions.Leb2Integration;
using LEB2SCRAPPER.Repository.Parsing;

namespace LEB2SCRAPPER.Tests.Repository;

public class Leb2RenderedPageParserTests
{
    [Fact]
    public void ParseSemesterIds_DeduplicatesRenderedLinks()
    {
        var parser = new Leb2RenderedPageParser();
        const string html = """
            <html>
              <body>
                <a href="/class?semester_id=10">First</a>
                <a href="/class?semester_id=11">Second</a>
                <a href="/class?semester_id=10">Duplicate</a>
              </body>
            </html>
            """;

        var semesterIds = parser.ParseSemesterIds(html);

        Assert.Equal([10, 11], semesterIds);
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
