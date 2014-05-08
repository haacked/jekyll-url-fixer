using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace SubtextJekyllExporter
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Please pass the path to the Google Web Report 404s and the past to your Jekyll _posts directory");
                return;
            }
            string errorCsv = args[0];
            string postsDirectory = args[1];

            var index = Directory.EnumerateFiles(
                Path.Combine(postsDirectory, "archived"))
                .Select(ParseFileName)
                .Where(f => f != null)
                .ToDictionary(f => f.Slug, f => f, StringComparer.OrdinalIgnoreCase);

            if (args.Length == 3)
            {
                var redirects = index
                    .Select(i => new
                    {
                        from = i.Value.SubtractDay(), target = i.Value
                    });

                const string contentFormat = @"---
layout: redirect
{0}
redirect: {1}
---
";
                // Create Redirects.
                foreach (var file in redirects)
                {
                    var content = String.Format(
                        CultureInfo.InvariantCulture,
                        contentFormat,
                        file.from.FormattedYamlDate,
                        file.target.Url);
                    var redirectsFilePath = Path.Combine(
                        postsDirectory,
                        "redirects",
                        file.from.JekyllFileName);

                    File.WriteAllText(redirectsFilePath, content);
                }

                return;
            }

            var brokenUrls = File.ReadAllLines(errorCsv)
                .Skip(1)
                .Select(line => line.Split(',').First())
                .Select(ParseUrl)
                .Where(f => f != null && f.Slug != null)
                .GroupBy(f => f.Slug, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Where(f => index.ContainsKey(f.Slug) && f.Year > 2000)
                .Select(f => new { expected = f, current = index[f.Slug] })
                .Where(pair => !pair.current.MatchesDate(pair.expected));
                
            foreach (var pair in brokenUrls)
            {
                Console.WriteLine("Rename: " + pair.expected.JekyllFileName + 
                    " to " + pair.current.JekyllFileName);
                File.Move(
                    Path.Combine(postsDirectory, pair.current.JekyllFileName),
                    Path.Combine(postsDirectory, pair.expected.JekyllFileName));
            }

            // Fix up content
            var fixups = Directory.EnumerateFiles(postsDirectory)
                .Select(path => new
                {
                    path,
                    post = ParseFileName(path),
                    contents = File.ReadAllLines(path)
                })
                .Where(info => info.post != null)
                .Select(info => new
                {
                    info.path, 
                    info.post,
                    info.contents,
                    dateInfo = info.contents.Select(
                        (line, idx) => new
                        {
                            index = idx, 
                            date = ParseLine(line)
                        })
                        .FirstOrDefault(x => x.date != null)})
                .Where(info => !info.post.MatchesDate(info.dateInfo.date))
                .Select(info => new
                {
                    info.path,
                    updated = GetUpdatedContents(info.contents, info.dateInfo.index, info.post.FormattedYamlDate)
                });

            foreach (var fixup in fixups)
            {
                Console.WriteLine("Fixing: " + fixup.path);
                File.WriteAllText(fixup.path, fixup.updated);
            }

            Console.WriteLine("Hit ENTER to close");
            Console.ReadLine();
        }

        private static string GetUpdatedContents(string[] lines, int index, string newLine)
        {
            lines[index] = newLine;
            return String.Join(Environment.NewLine, lines) + Environment.NewLine;
        }

        static readonly Regex _fileNameRegex = new Regex(@"(?<year>\d{4})-(?<month>\d{2})-(?<day>\d{2})-(?<slug>.*?)\.aspx\.markdown", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static PostInfo ParseFileName(string path)
        {
            string fileName = Path.GetFileName(path);
            return ParsePostInfo(_fileNameRegex, fileName);
        }

        static readonly Regex _urlRegex = new Regex(@".*?/archive/(?<year>\d{4})/(?<month>\d{2})/(?<day>\d{2})/(?<slug>.*?)\.aspx", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static PostInfo ParseUrl(string url)
        {
            return ParsePostInfo(_urlRegex, url);
        }

        static readonly Regex _yamlRegex = new Regex(@"^date: (?<year>\d{4})-(?<month>\d{2})-(?<day>\d{2}) -0800\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
        private static PostInfo ParseLine(string line)
        {
            return ParsePostInfo(_yamlRegex, line);
        }

        private static PostInfo ParsePostInfo(Regex regex, string text)
        {
            var match = regex.Match(text);

            if (!match.Success) return null;

            return new PostInfo
            {
                Year = Convert.ToInt32(match.Groups["year"].Value),
                Month = Convert.ToInt32(match.Groups["month"].Value),
                Day = Convert.ToInt32(match.Groups["day"].Value),
                Slug = match.Groups["slug"].Value
            };
        }
    }

    internal class PostInfo
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public int Day { get; set; }
        public string Slug { get; set; }

        public DateTime AsDate
        {
            get
            {
                return DateTime.Parse(Year + "/" + Month + "/" + Day);
            }
        }

        public PostInfo SubtractDay()
        {
            var previousDay = AsDate.AddDays(-1);

            return new PostInfo
            {
                Year = previousDay.Year,
                Month = previousDay.Month,
                Day = previousDay.Day,
                Slug = Slug
            };
        }

        public bool MatchesDate(PostInfo otherPostInfo)
        {
            return otherPostInfo.Year == Year
                   && otherPostInfo.Month == Month
                   && otherPostInfo.Day == Day;
        }

        public string FormattedYamlDate
        {
            get
            {
                return String.Format(CultureInfo.InvariantCulture,
                    "date: {0}-{1:D2}-{2:D2} -0800",
                    Year,
                    Month,
                    Day);
            }
        }

        public string JekyllFileName
        {
            get
            {
                return String.Format(CultureInfo.InvariantCulture, 
                    "{0}-{1:D2}-{2:D2}-{3}.aspx.markdown",
                    Year,
                    Month,
                    Day,
                    Slug);
            }
        }

        public string Url
        {
            get
            {
                return String.Format(
                    CultureInfo.InvariantCulture,
                    "/archive/{0}/{1:D2}/{2:D2}/{3}.aspx/",
                    Year,
                    Month,
                    Day,
                    Slug
                    );
            }
        }
    }
}