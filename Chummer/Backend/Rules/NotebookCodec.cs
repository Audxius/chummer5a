/* This file is part of Chummer5a. Licensed GPLv3; see LICENSE. */
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace Chummer.Rules
{
    public sealed class NotebookPageData
    {
        public Guid Id { get; }
        public string Title { get; }
        public string Body { get; }
        public NotebookPageData(Guid id, string title, string body)
        { Id = id; Title = title ?? "Notes"; Body = body ?? string.Empty; }
    }

    /// <summary>Readable sections in the existing Notes string. No character XML schema extensions.</summary>
    public static class NotebookCodec
    {
        private static readonly Regex Header = new Regex(@"^<!-- chummer-note:v1 (?<id>[0-9a-fA-F-]{36}) (?<title>.+) -->\r?$", RegexOptions.Multiline | RegexOptions.CultureInvariant);
        private static readonly Regex Escape = new Regex(@"^(\\*)(<!-- /?chummer-note)", RegexOptions.Multiline | RegexOptions.CultureInvariant);
        private static readonly Regex Unescape = new Regex(@"^\\(\\*<!-- /?chummer-note)", RegexOptions.Multiline | RegexOptions.CultureInvariant);

        public static IReadOnlyList<NotebookPageData> Parse(string text)
        {
            text = text ?? string.Empty;
            var pages = new List<NotebookPageData>();
            var ids = new HashSet<Guid>();
            int cursor = 0;
            foreach (Match start in Header.Matches(text))
            {
                if (start.Index < cursor || !Guid.TryParse(start.Groups["id"].Value, out Guid id) || ids.Contains(id)) continue;
                string title;
                try { title = JsonConvert.DeserializeObject<string>(start.Groups["title"].Value); }
                catch (JsonException) { continue; }
                if (title == null) continue;
                int bodyStart = start.Index + start.Length;
                if (bodyStart >= text.Length || text[bodyStart] != '\n') continue;
                ++bodyStart;
                Match end = Regex.Match(text.Substring(bodyStart), @"^<!-- /chummer-note:" + Regex.Escape(id.ToString("D")) + @" -->\r?$", RegexOptions.Multiline | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                if (!end.Success) continue;
                string before = text.Substring(cursor, start.Index - cursor);
                if (!string.IsNullOrWhiteSpace(before)) pages.Add(new NotebookPageData(Guid.NewGuid(), "Imported notes", before));
                string body = text.Substring(bodyStart, end.Index);
                if (body.EndsWith("\r\n", StringComparison.Ordinal)) body = body.Substring(0, body.Length - 2);
                else if (body.EndsWith("\n", StringComparison.Ordinal)) body = body.Substring(0, body.Length - 1);
                pages.Add(new NotebookPageData(id, title, Unescape.Replace(body, "$1"))); ids.Add(id);
                cursor = bodyStart + end.Index + end.Length;
                if (cursor < text.Length && text[cursor] == '\n') ++cursor;
            }
            if (pages.Count == 0) return new[] { new NotebookPageData(Guid.Empty, "Notes", text) };
            if (cursor < text.Length && !string.IsNullOrWhiteSpace(text.Substring(cursor)))
                pages.Add(new NotebookPageData(Guid.NewGuid(), "Imported notes", text.Substring(cursor)));
            return pages;
        }

        public static string Serialize(IEnumerable<NotebookPageData> source)
        {
            var pages = source.ToList();
            if (pages.Count == 0) return string.Empty;
            if (pages.Count == 1 && pages[0].Id == Guid.Empty && pages[0].Title == "Notes" && !Header.IsMatch(pages[0].Body)) return pages[0].Body;
            if (pages.Select(x => x.Id).Distinct().Count() != pages.Count) throw new ArgumentException("Notebook page identifiers must be unique.", nameof(source));
            var result = new StringBuilder();
            foreach (var page in pages)
            {
                result.Append("<!-- chummer-note:v1 ").Append(page.Id.ToString("D")).Append(' ').Append(JsonConvert.SerializeObject(page.Title)).Append(" -->\n");
                result.Append(Escape.Replace(page.Body, m => "\\" + m.Value));
                result.Append("\n<!-- /chummer-note:").Append(page.Id.ToString("D")).Append(" -->\n");
            }
            return result.ToString();
        }
    }
}
