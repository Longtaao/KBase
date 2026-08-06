using KBase.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace KBase.Web.Pages.Notes;

public class EditModel : PageModel
{
    private readonly INoteRepository _repo;

    public EditModel(INoteRepository repo) => _repo = repo;

    [BindProperty] public string? Id { get; set; }
    [BindProperty] public string Title { get; set; } = "";
    [BindProperty] public string Tags { get; set; } = "";
    [BindProperty] public string Content { get; set; } = "";
    public string? Error { get; set; }

    public void OnGet(string? title, string? id)
    {
        if (id is not null)
        {
            var note = _repo.Get(id);
            if (note is null) { Error = "笔记不存在"; return; }
            Fill(note);
        }
        else if (!string.IsNullOrWhiteSpace(title))
        {
            var note = _repo.FindByTitle(title);
            if (note is not null) Fill(note);
            else Title = title; // 从 wikilink 跳转，预填标题
        }
    }

    public IActionResult OnPost()
    {
        var tagList = Tags.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (string.IsNullOrWhiteSpace(Title))
        {
            Error = "标题不能为空";
            return Page();
        }
        if (string.IsNullOrWhiteSpace(Id))
            _repo.Create(Title, tagList, Content);
        else
            _repo.Update(Id, Title, tagList, Content);
        return RedirectToPage("/Notes/View", new { title = Title });
    }

    private void Fill(Note note)
    {
        Id = note.Id;
        Title = note.Title;
        Tags = string.Join(", ", note.Tags);
        Content = note.Content;
    }
}
