using System.Net;
using AwesomeAssertions;
using EwaysVNext.Modules.Sourcing.Adapter.Outbound.Crawling;
using EwaysVNext.Modules.Sourcing.Application.Discovery;
using Xunit;

namespace EwaysVNext.UnitTests.Sourcing;

/// <summary>
/// The crawler's three obligations: where it may go, how fast, and what it reads.
/// </summary>
/// <remarks>
/// These are the tests that stop the import feature being a liability. An administrator can type
/// any URL into a job, so without the address check "import this catalogue" is also "fetch the
/// cloud metadata service"; and a crawler that reads robots.txt without honouring it is worse
/// than one that never asked, because it has recorded that it knew.
/// </remarks>
public sealed class CrawlerSafetyTests
{
    [Theory]
    [InlineData("127.0.0.1")]
    [InlineData("10.0.0.5")]
    [InlineData("172.16.3.1")]
    [InlineData("192.168.1.1")]
    [InlineData("169.254.169.254")]
    [InlineData("100.64.0.1")]
    [InlineData("0.0.0.0")]
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fc00::1")]
    public void Nothing_Inside_The_Network_Is_Fetchable(string address) =>
        // 169.254.169.254 is the one that matters most: it is the cloud metadata endpoint, and
        // reaching it hands over this machine's credentials.
        SafeHttpHandler.IsPrivate(IPAddress.Parse(address)).Should().BeTrue();

    [Theory]
    [InlineData("1.1.1.1")]
    [InlineData("93.184.216.34")]
    [InlineData("2606:4700:4700::1111")]
    public void Public_Addresses_Are_Fetchable(string address) =>
        SafeHttpHandler.IsPrivate(IPAddress.Parse(address)).Should().BeFalse();

    [Fact]
    public void An_IPv4_Address_Hidden_In_IPv6_Is_Still_Private() =>
        // ::ffff:10.0.0.5 is 10.0.0.5. Checking the family without unmapping would miss it.
        SafeHttpHandler.IsPrivate(IPAddress.Parse("::ffff:10.0.0.5")).Should().BeTrue();

    [Fact]
    public void A_Missing_Robots_File_Means_No_Restrictions() =>
        // Refusing to crawl would be inventing a prohibition the site never made.
        RobotsTxt.Parse(null, "EwaysVNextBot").IsAllowed("/anything").Should().BeTrue();

    [Fact]
    public void A_Blanket_Disallow_Is_Honoured()
    {
        var robots = RobotsTxt.Parse("User-agent: *\nDisallow: /", "EwaysVNextBot");

        robots.IsAllowed("/products/1").Should().BeFalse();
    }

    [Fact]
    public void An_Empty_Disallow_Means_Nothing_Is_Disallowed() =>
        // The standard's way of saying "crawl freely", and reading it as "disallow /" would stop
        // every crawl of a site that welcomed one.
        RobotsTxt.Parse("User-agent: *\nDisallow:", "EwaysVNextBot")
            .IsAllowed("/products/1").Should().BeTrue();

    [Fact]
    public void A_Rule_Naming_This_Crawler_Wins_Over_The_Wildcard()
    {
        var robots = RobotsTxt.Parse(
            "User-agent: *\nDisallow: /\n\nUser-agent: EwaysVNextBot\nDisallow: /private\n",
            "EwaysVNextBot");

        robots.IsAllowed("/products/1").Should().BeTrue("the site named us specifically");
        robots.IsAllowed("/private/x").Should().BeFalse();
    }

    [Fact]
    public void A_Longer_Allow_Carves_A_Directory_Out_Of_A_Prohibition()
    {
        var robots = RobotsTxt.Parse(
            "User-agent: *\nDisallow: /catalogue\nAllow: /catalogue/public\n", "EwaysVNextBot");

        robots.IsAllowed("/catalogue/private").Should().BeFalse();
        robots.IsAllowed("/catalogue/public/1").Should().BeTrue();
    }

    [Fact]
    public void A_Crawl_Delay_Is_Read() =>
        RobotsTxt.Parse("User-agent: *\nCrawl-delay: 5\n", "EwaysVNextBot")
            .CrawlDelay.Should().Be(TimeSpan.FromSeconds(5));

    [Fact]
    public void Comments_Are_Not_Rules() =>
        RobotsTxt.Parse("# Disallow: /\nUser-agent: *\nDisallow: /x\n", "EwaysVNextBot")
            .IsAllowed("/anything").Should().BeTrue();

    [Fact]
    public void One_Host_Is_Not_Hit_Faster_Than_It_Asked()
    {
        var gate = new PolitenessGate();
        var now = new DateTimeOffset(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);
        var delay = TimeSpan.FromSeconds(1);

        gate.Reserve("a.example", delay, now).Should().Be(TimeSpan.Zero, "nothing has been asked yet");
        gate.Reserve("a.example", delay, now).Should().Be(delay);
        gate.Reserve("a.example", delay, now).Should().Be(TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Politeness_To_One_Host_Does_Not_Slow_Another()
    {
        var gate = new PolitenessGate();
        var now = new DateTimeOffset(2026, 11, 8, 9, 0, 0, TimeSpan.Zero);

        gate.Reserve("a.example", TimeSpan.FromSeconds(5), now);

        // Crawling two shops at once is not rude to either.
        gate.Reserve("b.example", TimeSpan.FromSeconds(5), now).Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void A_Product_Page_Is_Read_From_What_The_Shop_Declared()
    {
        const string Html = """
            <html><head>
            <script type="application/ld+json">
            {"@context":"https://schema.org","@type":"Product","name":"Steel Kettle",
             "brand":{"@type":"Brand","name":"Acme"},"gtin13":"4006381333931",
             "offers":{"@type":"Offer","price":"19.99","priceCurrency":"SGD"}}
            </script>
            </head><body>Something the shop never meant as product data</body></html>
            """;

        var products = StructuredDataExtractor.Extract(Html);

        var product = products.Should().ContainSingle().Subject;
        product.Method.Should().Be(ExtractionMethod.JsonLd);
        product.Document["name"]!.ToString().Should().Be("Steel Kettle");
        product.Original.Should().NotBeNull("the page's own words are what settle a dispute");
    }

    [Fact]
    public void A_Product_Inside_A_Graph_Is_Found()
    {
        const string Html = """
            <script type="application/ld+json">
            {"@context":"https://schema.org","@graph":[
              {"@type":"Organization","name":"The Shop"},
              {"@type":"BreadcrumbList"},
              {"@type":"Product","name":"Glass Kettle"}]}
            </script>
            """;

        // Real pages wrap the product with the organisation and the breadcrumb; looking only at
        // the top level finds nothing on those pages.
        StructuredDataExtractor.Extract(Html).Should().ContainSingle()
            .Which.Document["name"]!.ToString().Should().Be("Glass Kettle");
    }

    [Fact]
    public void A_Page_With_No_Declared_Product_Yields_Nothing()
    {
        const string Html = "<html><body><h1>Kettles</h1><p>$19.99</p></body></html>";

        // Scraping the visible text would produce a catalogue that breaks on a theme change.
        StructuredDataExtractor.Extract(Html).Should().BeEmpty();
    }

    [Fact]
    public void Broken_Json_On_One_Page_Does_Not_Stop_The_Crawl()
    {
        const string Html = """
            <script type="application/ld+json">{ this is not json </script>
            <script type="application/ld+json">{"@type":"Product","name":"Fine"}</script>
            """;

        StructuredDataExtractor.Extract(Html).Should().ContainSingle()
            .Which.Document["name"]!.ToString().Should().Be("Fine");
    }

    [Fact]
    public void OpenGraph_Is_Used_Only_When_There_Is_No_Json_Ld()
    {
        const string Html = """
            <meta property="og:type" content="product">
            <meta property="og:title" content="Kettle From OpenGraph">
            <meta content="24.50" property="product:price:amount">
            <meta property="product:price:currency" content="SGD">
            """;

        var product = StructuredDataExtractor.Extract(Html).Should().ContainSingle().Subject;

        product.Method.Should().Be(ExtractionMethod.OpenGraph);
        product.Document["name"]!.ToString().Should().Be("Kettle From OpenGraph");

        // Attribute order is not fixed in HTML, and plenty of templates write content first.
        product.Document["offers"]!["price"]!.ToString().Should().Be("24.50");
    }

    [Fact]
    public void A_Non_Product_OpenGraph_Page_Is_Not_A_Product() =>
        StructuredDataExtractor.Extract(
            """<meta property="og:type" content="article"><meta property="og:title" content="News">""")
            .Should().BeEmpty();

    [Fact]
    public void Two_Links_To_One_Page_Are_One_Url() =>
        UrlForm.Canonicalize("https://A.example:443/p/1?b=2&a=1&utm_source=x#reviews")
            .Should().Be(UrlForm.Canonicalize("https://a.example/p/1?a=1&b=2"));
}
