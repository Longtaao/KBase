using System.Text.RegularExpressions;
using KBase.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace KBase.Web.Pages.Notes;

public class ViewModel : PageModel
{
    private readonly INoteRepository _repo;

    public ViewModel(INoteRepository repo) => _repo = repo;

    public Note? Note { get; set; }
    public List<Note> Backlinks { get; set; } = [];
    public string Html { get; set; } = "";

    public IActionResult OnGet(string title)
    {
        Note = _repo.FindByTitle(title)
            ?? _repo.List().FirstOrDefault(n => n.Title.Contains(title, StringComparison.OrdinalIgnoreCase));
        if (Note is null) return NotFound();
        Note = _repo.Get(Note.Id) ?? Note; // FindByTitle 不含正文，补全
        Backlinks = _repo.Backlinks(Note.Title);
        Html = RenderMarkdown(Note.Content);
        return Page();
    }

    public IActionResult OnPostDelete(string id)
    {
        _repo.Delete(id);
        return RedirectToPage("/Index");
    }

    /// <summary>Markdown → HTML；[[wikilink]] 转成站内链接</summary>
    public static string RenderMarkdown(string content)
    {
        var withLinks = WikiLinkRegex.Replace(content, m =>
        {
            var target = m.Groups[1].Value.Trim();
            var text = m.Groups[2].Success ? m.Groups[2].Value : target;
            return $"[{text}](/Notes/View?title={Uri.EscapeDataString(target)})";
        });
        return Markdig.Markdown.ToHtml(withLinks);
    }

    private static readonly Regex WikiLinkRegex = new(
        @"\[\[([^\]|]+)(?:\|([^\]]+))?\]\]", RegexOptions.Compiled);
}
