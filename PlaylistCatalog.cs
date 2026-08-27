using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Klaxio
{
    /// <summary>
    /// The curated playlists offered on the Klaxio Music settings screen.
    ///
    /// Maintained in playlists.json next to the executable, so the list can be
    /// changed without a rebuild; the copy embedded in the assembly serves as
    /// the fallback when that file is missing. Editing the file and reloading
    /// the browser is enough - the cache follows the file's timestamp.
    ///
    /// Only "url" is required per entry. A missing name or cover image is read
    /// from the Open Graph tags of the playlist page, which is the only source
    /// that carries the real cover art of a YouTube Music playlist. oEmbed is
    /// kept as a fallback - it has no cover for those playlists at all.
    /// </summary>
    class PlaylistCatalog
    {
        public const string FileName = "playlists.json";

        const string OEmbed = "https://www.youtube.com/oembed?url={0}&format=json";

        /// <summary>Both YouTube Music and youtube.com playlists resolve through this page.</summary>
        const string PlaylistPage = "https://www.youtube.com/playlist?list={0}";

        /// <summary>The Open Graph block sits well into the page, but never past this.</summary>
        const int PageReadLimit = 2 * 1024 * 1024;

        /// <summary>Cover URLs from YouTube are signed and rotate daily, so do not keep them forever.</summary>
        static readonly TimeSpan CacheLife = TimeSpan.FromHours(6);

        static readonly Regex OgImage = new Regex(
            "<meta property=\"og:image\" content=\"([^\"]+)\"", RegexOptions.Compiled);
        static readonly Regex OgTitle = new Regex(
            "<meta property=\"og:title\" content=\"([^\"]+)\"", RegexOptions.Compiled);
        static readonly Regex ListId = new Regex(
            "[?&]list=([A-Za-z0-9_-]+)", RegexOptions.Compiled);

        static readonly HttpClient _http = CreateClient();

        static readonly JsonSerializerOptions _read = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling         = JsonCommentHandling.Skip,
            AllowTrailingCommas         = true,
        };

        readonly object _lock = new object();
        Task<string>    _json;      // cached payload for the browser
        long            _stamp;     // file timestamp the cache was built from
        DateTime        _builtAt;   // when that cache was filled

        // Deliberately no User-Agent: YouTube answers a named client with its
        // cookie-consent page, which carries no Open Graph tags at all.
        static HttpClient CreateClient() =>
            new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        /* File shape */
        class FileModel
        {
            public List<Category> Categories { get; set; } = new List<Category>();
        }

        class Category
        {
            public string Name { get; set; }
            public string Icon { get; set; }
            public List<Entry> Playlists { get; set; } = new List<Entry>();
        }

        class Entry
        {
            public string Name      { get; set; }
            public string Url       { get; set; }
            public string Thumbnail { get; set; }
        }

        /// <summary>Serialised catalogue for the browser, built at most once per file version.</summary>
        public Task<string> GetJsonAsync()
        {
            var stamp = FileStamp();
            lock (_lock)
            {
                var stale = DateTime.UtcNow - _builtAt > CacheLife;
                if (_json == null || _stamp != stamp || stale)
                {
                    _stamp   = stamp;
                    _builtAt = DateTime.UtcNow;
                    _json    = BuildAsync();
                }
                return _json;
            }
        }

        static string FilePath() => Path.Combine(AppContext.BaseDirectory, FileName);

        /// <summary>Last write time of the external file, or 0 while the embedded copy is in use.</summary>
        static long FileStamp()
        {
            try
            {
                var path = FilePath();
                return File.Exists(path) ? File.GetLastWriteTimeUtc(path).Ticks : 0;
            }
            catch { return 0; }
        }

        static string ReadSource(out string origin)
        {
            var path = FilePath();
            try
            {
                if (File.Exists(path))
                {
                    origin = path;
                    return File.ReadAllText(path, Encoding.UTF8);
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(L.Get("PlaylistsReadFailed", path, e.Message));
            }

            origin = "<embedded>";
            using var stream = Assembly.GetExecutingAssembly()
                .GetManifestResourceStream("Klaxio." + FileName);
            if (stream == null) return null;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        async Task<string> BuildAsync()
        {
            var categories = LoadCategories();

            // Look every missing name/cover up in one go - the entries are
            // independent and there are only a handful of them.
            var entries = categories.SelectMany(c => c.Playlists)
                                    .Where(e => e.Name == null || e.Thumbnail == null)
                                    .ToList();
            if (entries.Count > 0)
                await Task.WhenAll(entries.Select(FillFromYouTubeAsync));

            var dto = new
            {
                categories = categories.Select(c => new
                {
                    name      = c.Name ?? "",
                    icon      = string.IsNullOrWhiteSpace(c.Icon) ? "queue_music" : c.Icon,
                    playlists = c.Playlists.Select(e => new
                    {
                        name      = e.Name ?? "",
                        url       = e.Url,
                        thumbnail = e.Thumbnail ?? ""
                    })
                })
            };
            return JsonSerializer.Serialize(dto);
        }

        /// <summary>Parsed categories with blank and duplicate entries dropped.</summary>
        List<Category> LoadCategories()
        {
            string origin;
            var source = ReadSource(out origin);
            if (string.IsNullOrWhiteSpace(source))
            {
                Console.WriteLine(L.Get("PlaylistsNone"));
                return new List<Category>();
            }

            FileModel model;
            try
            {
                model = JsonSerializer.Deserialize<FileModel>(source, _read) ?? new FileModel();
            }
            catch (JsonException e)
            {
                Console.WriteLine(L.Get("PlaylistsInvalid", origin, e.Message));
                return new List<Category>();
            }

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var categories = new List<Category>();

            foreach (var category in model.Categories ?? new List<Category>())
            {
                if (category == null) continue;

                var kept = new List<Entry>();
                foreach (var entry in category.Playlists ?? new List<Entry>())
                {
                    if (entry == null || string.IsNullOrWhiteSpace(entry.Url)) continue;

                    entry.Url = entry.Url.Trim();
                    if (!seen.Add(entry.Url)) continue;   // same playlist listed twice

                    entry.Name      = Blank(entry.Name);
                    entry.Thumbnail = Blank(entry.Thumbnail);
                    kept.Add(entry);
                }

                if (kept.Count == 0) continue;
                category.Playlists = kept;
                categories.Add(category);
            }

            Console.WriteLine(L.Get("PlaylistsLoaded",
                categories.Sum(c => c.Playlists.Count), categories.Count, origin));
            return categories;
        }

        static string Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        /// <summary>
        /// Fill in whatever the file left out. A failure is not fatal: the entry
        /// simply keeps its configured name and shows a placeholder cover.
        /// </summary>
        async Task FillFromYouTubeAsync(Entry entry)
        {
            await FillFromPlaylistPageAsync(entry);

            // oEmbed knows nothing about YouTube Music's curated playlists, so it
            // only ever runs when the page did not answer.
            if (entry.Name == null || entry.Thumbnail == null)
                await FillFromOEmbedAsync(entry);

            if (entry.Thumbnail == null)
                Console.WriteLine(L.Get("PlaylistsNoCover", entry.Name ?? entry.Url));
        }

        /// <summary>
        /// Read the Open Graph tags of the playlist page. For a YouTube Music
        /// playlist this yields its actual cover art; for an ordinary playlist
        /// it yields the same cover YouTube itself shows.
        /// </summary>
        async Task FillFromPlaylistPageAsync(Entry entry)
        {
            var id = PlaylistId(entry.Url);
            if (id == null) return;

            try
            {
                var page = await ReadPageAsync(string.Format(PlaylistPage, Uri.EscapeDataString(id)));

                if (entry.Thumbnail == null)
                {
                    var match = OgImage.Match(page);
                    // The cover URL is signed and carries "&amp;" straight out of the HTML.
                    if (match.Success) entry.Thumbnail = Blank(WebUtility.HtmlDecode(match.Groups[1].Value));
                }

                if (entry.Name == null)
                {
                    var match = OgTitle.Match(page);
                    if (match.Success) entry.Name = Blank(WebUtility.HtmlDecode(match.Groups[1].Value));
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(L.Get("PlaylistsLookupFailed", entry.Name ?? entry.Url, e.Message));
            }
        }

        async Task FillFromOEmbedAsync(Entry entry)
        {
            try
            {
                var body = await _http.GetStringAsync(string.Format(OEmbed, Uri.EscapeDataString(entry.Url)));

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (entry.Name == null && root.TryGetProperty("title", out var title))
                    entry.Name = Blank(title.GetString());

                if (entry.Thumbnail == null && root.TryGetProperty("thumbnail_url", out var thumb))
                    entry.Thumbnail = Blank(thumb.GetString());
            }
            catch (Exception)
            {
                // Expected for YouTube Music playlists - the page above is the real source.
            }
        }

        static string PlaylistId(string url)
        {
            var match = ListId.Match(url ?? "");
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// Stream the page only until the Open Graph block has been seen. YouTube
        /// serves well over a megabyte here, and the tags sit far enough in that
        /// reading the whole response would be wasteful.
        /// </summary>
        static async Task<string> ReadPageAsync(string url)
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            using var stream = await response.Content.ReadAsStreamAsync();
            using var reader = new StreamReader(stream, Encoding.UTF8);

            var buffer   = new char[64 * 1024];
            var page     = new StringBuilder();
            var previous = "";

            int read;
            while (page.Length < PageReadLimit &&
                   (read = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                var chunk = new string(buffer, 0, read);
                page.Append(chunk);

                // The two tags sit next to each other, so a chunk plus its
                // predecessor always holds both once they show up.
                var window = previous + chunk;
                if (OgImage.IsMatch(window) && OgTitle.IsMatch(window)) return window;
                previous = chunk;
            }
            return page.ToString();
        }
    }
}
