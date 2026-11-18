using System.Text;
using Server.Infrastructure.Coordinator.LinkPreviews;
using Xunit;

namespace Server.Infrastructure.Tests.Coordinator.LinkPreviews;

public class PageMetadataTests
{
    [Fact]
    public void OpenGraph_IsReadFirst()
    {
        var page = PageMetadata.Parse("""
            <html><head>
            <title>Fallback</title>
            <meta property="og:site_name" content="YouTube">
            <meta property="og:title" content="A &amp; B" />
            <meta content='Some
               description' property='og:description'>
            <meta property="og:image" content="https://i.example/x.jpg">
            <meta property="og:image:width" content="1280"><meta property="og:image:height" content="720">
            <meta name="twitter:card" content="summary_large_image">
            </head><body><meta property="og:title" content="Not in the head"></body></html>
            """);

        Assert.Equal("YouTube", page.SiteName);
        Assert.Equal("A & B", page.Title);
        Assert.Equal("Some description", page.Description);
        Assert.Equal("https://i.example/x.jpg", page.ImageUrl);
        Assert.Equal(1280, page.ImageWidth);
        Assert.Equal("summary_large_image", page.Card);
    }

    [Fact]
    public void WithoutOpenGraph_TheTitleAndDescriptionAreUsed()
    {
        var page = PageMetadata.Parse("""
            <head><!-- <meta property="og:title" content="commented out"> -->
            <script>var s = '<meta property="og:title" content="in a script">';</script>
            <title>
              Plain   title
            </title><meta name="description" content="Plain text"></head>
            """);

        Assert.Equal("Plain title", page.Title);
        Assert.Equal("Plain text", page.Description);
        Assert.Null(page.ImageUrl);
    }

    [Fact]
    public void TheCharset_ComesFromTheHeaderOrTheMetaTag()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var cyrillic = Encoding.GetEncoding("windows-1251");
        var bytes = cyrillic.GetBytes("<meta charset=\"windows-1251\"><title>Привет</title>");

        var fromMeta = PageMetadata.EncodingOf(bytes, null);
        Assert.Equal("Привет", PageMetadata.Parse(fromMeta.GetString(bytes)).Title);
        Assert.Equal(Encoding.UTF8.WebName, PageMetadata.EncodingOf("<title>x</title>"u8, null).WebName);
        Assert.Equal("koi8-r", PageMetadata.EncodingOf("<title>x</title>"u8, "koi8-r").WebName);
    }

    [Fact]
    public void LongTexts_AreCutWithAnEllipsis()
    {
        Assert.Equal("abc", PageMetadata.Cut("abc", 3));
        Assert.Equal("ab…", PageMetadata.Cut("abcd", 3));
        Assert.Null(PageMetadata.Cut(null, 3));
    }
}
