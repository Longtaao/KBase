using KBase.Core;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace KBase.Web.Pages;

public class IndexModel : PageModel
{
    private readonly INoteRepository _repo;

    public IndexModel(INoteRepository repo) => _repo = repo;

    public List<Note> Notes { get; set; } = [];
    public List<Tag> AllTags { get; set; } = [];
    public string? Search { get; set; }
    public string? Tag { get; set; }

    public void OnGet(string? search, string? tag)
    {
        Search = search;
        Tag = tag;
        Notes = !string.IsNullOrWhiteSpace(search) ? _repo.Search(search)
              : _repo.List(tag);
        AllTags = _repo.Stats().Tags;
    }
}
