using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using Pizon.Doors.Sdk.Text;
using Pizon.Web.Client.Content;

namespace Pizon.Web.Endpoints;

/// <summary>
/// <c>/plain</c>: the bulletins and links as plain HTML, for screen readers, search engines and anyone
/// without JavaScript.
/// </summary>
public static class PlainEndpoint
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Maps <c>/plain</c>.
    /// </summary>
    /// <param name="app">The route builder.</param>
    public static void MapPlainEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/plain", (IWebHostEnvironment env) => Results.Content(Render(env.WebRootFileProvider), "text/html; charset=utf-8"));
    }

    /// <summary>
    /// Renders the plain page from the content files.
    /// </summary>
    /// <param name="files">The web root files.</param>
    /// <returns>The HTML.</returns>
    public static string Render(IFileProvider files)
    {
        ArgumentNullException.ThrowIfNull(files);
        var board = Read<BulletinBoard>(files, "content/bulletins.json") ?? new BulletinBoard();
        var library = Read<FileLibrary>(files, "content/files.json") ?? new FileLibrary();
        var html = HtmlEncoder.Default;
        var sb = new StringBuilder();
        sb.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
            .Append("<title>PIZON BBS (plain text)</title><style>body{font-family:monospace;max-width:80ch;margin:2rem auto;padding:0 1rem;background:#000;color:#c0c0c0}")
            .Append("a{color:#00ffff}h1,h2{color:#ffff00}</style></head><body>")
            .Append("<h1>PIZON BBS</h1><p>").Append(html.Encode(board.Tagline)).Append("</p>")
            .Append("<p>SysOp: ").Append(html.Encode(board.Sysop)).Append(". <a href=\"/\">Dial in to the full BBS</a>.</p>");
        foreach (var b in board.Bulletins)
        {
            sb.Append("<h2>Bulletin #").Append(b.Number).Append(": ").Append(html.Encode(b.Title)).Append("</h2>");
            foreach (var line in b.Lines)
            {
                sb.Append("<p>").Append(html.Encode(PipeCodes.StripToPlain(line))).Append("</p>");
            }
        }

        sb.Append("<h2>File areas (links)</h2>");
        foreach (var area in library.Areas)
        {
            sb.Append("<h3>").Append(html.Encode(area.Name)).Append("</h3><ul>");
            foreach (var f in area.Files)
            {
                sb.Append("<li>");
                if (f.Url is { Scheme: "http" or "https" } url)
                {
                    sb.Append("<a href=\"").Append(html.Encode(url.AbsoluteUri)).Append("\" rel=\"noopener\">").Append(html.Encode(f.Name)).Append("</a>");
                }
                else
                {
                    sb.Append(html.Encode(f.Name));
                }

                sb.Append(" - ").Append(html.Encode(f.Description)).Append("</li>");
            }

            sb.Append("</ul>");
        }

        return sb.Append("</body></html>").ToString();
    }

    private static T? Read<T>(IFileProvider files, string path)
        where T : class
    {
        var file = files.GetFileInfo(path);
        if (!file.Exists)
        {
            return null;
        }

        try
        {
            using var stream = file.CreateReadStream();
            return JsonSerializer.Deserialize<T>(stream, Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
