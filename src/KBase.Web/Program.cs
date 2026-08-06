using KBase.Core;
using KBase.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// 命令行参数：--vault <知识库目录> --port <端口>（kbase serve 传进来）
var vault = GetArg(args, "--vault") ?? builder.Configuration["KBase:VaultPath"];
var port = GetArg(args, "--port");
if (string.IsNullOrWhiteSpace(vault) || !Directory.Exists(vault))
{
    Console.Error.WriteLine("❌ 未指定有效的知识库目录（--vault <路径>）。");
    return 1;
}
if (port is not null && int.TryParse(port, out var p))
    builder.WebHost.UseUrls($"http://localhost:{p}");

builder.Services.AddRazorPages();
builder.Services.AddSingleton<INoteRepository>(new SqliteNoteRepository(vault));

var app = builder.Build();
app.UseStaticFiles();
app.MapRazorPages();
Console.WriteLine($"🌐 KBase Web UI 已启动：{app.Urls.FirstOrDefault() ?? "http://localhost"}");
app.Run();
return 0;

static string? GetArg(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
        if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
            return args[i + 1];
    return null;
}
