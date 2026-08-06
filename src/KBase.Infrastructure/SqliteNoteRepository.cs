using System.Text;
using System.Text.RegularExpressions;
using KBase.Core;
using Microsoft.Data.Sqlite;

namespace KBase.Infrastructure;

/// <summary>
/// SQLite 实现：笔记存 Markdown 文件（含 YAML frontmatter 标签），
/// 索引存 SQLite，全文搜索用 FTS5 + trigram 分词（天然支持中文子串匹配），
/// 双向链接 [[wikilink]] 解析后存 links 表。
/// </summary>
public partial class SqliteNoteRepository : INoteRepository
{
    private readonly string _root;
    private readonly string _dbPath;

    /// <summary>知识库元数据目录名</summary>
    public const string MetaDir = ".kbase";

    public SqliteNoteRepository(string rootPath)
    {
        _root = rootPath;
        _dbPath = Path.Combine(_root, MetaDir, "kbase.db");
    }

    public void Init()
    {
        Directory.CreateDirectory(Path.Combine(_root, MetaDir));
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS notes (
                id         TEXT PRIMARY KEY,
                title      TEXT NOT NULL,
                path       TEXT NOT NULL UNIQUE,
                tags       TEXT NOT NULL DEFAULT '',
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE VIRTUAL TABLE IF NOT EXISTS notes_fts USING fts5(
                note_id UNINDEXED, title, content,
                tokenize = 'trigram'
            );
            CREATE TABLE IF NOT EXISTS links (
                source_id TEXT NOT NULL,
                target    TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS idx_links_source ON links(source_id);
            CREATE INDEX IF NOT EXISTS idx_links_target ON links(target);
            """;
        cmd.ExecuteNonQuery();
    }

    public Note Create(string title, IEnumerable<string> tags, string content = "")
    {
        Init();

        var safeName = SanitizeFileName(title);
        var filePath = Path.Combine(_root, safeName + ".md");
        var n = 2;
        while (File.Exists(filePath))
            filePath = Path.Combine(_root, $"{safeName} ({n++}).md");

        var note = new Note
        {
            Title = title,
            Path = Path.GetFileName(filePath),
            Tags = tags.Distinct().ToList(),
            Content = content,
            CreatedAt = DateTime.Now,
            UpdatedAt = DateTime.Now,
        };

        // 文件 = YAML frontmatter（标签）+ 正文
        File.WriteAllText(filePath, BuildFrontmatter(note.Tags) + content, Encoding.UTF8);

        using var conn = Open();
        using var tx = conn.BeginTransaction();

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = """
                INSERT INTO notes (id, title, path, tags, created_at, updated_at)
                VALUES ($id, $title, $path, $tags, $created, $updated)
                """;
            cmd.Parameters.AddWithValue("$id", note.Id);
            cmd.Parameters.AddWithValue("$title", note.Title);
            cmd.Parameters.AddWithValue("$path", note.Path);
            cmd.Parameters.AddWithValue("$tags", PackTags(note.Tags));
            cmd.Parameters.AddWithValue("$created", note.CreatedAt.ToString("o"));
            cmd.Parameters.AddWithValue("$updated", note.UpdatedAt.ToString("o"));
            cmd.ExecuteNonQuery();
        }

        InsertFts(conn, tx, note);
        ReplaceLinks(conn, tx, note.Id, ExtractWikiLinks(note.Content));
        tx.Commit();
        return note;
    }

    public List<Note> List(string? tag = null)
    {
        Init();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = tag is null
            ? "SELECT id, title, path, tags, created_at, updated_at FROM notes ORDER BY updated_at DESC"
            : "SELECT id, title, path, tags, created_at, updated_at FROM notes WHERE tags LIKE $tag ORDER BY updated_at DESC";
        if (tag is not null)
            cmd.Parameters.AddWithValue("$tag", $"%,{tag},%");
        return ReadNotes(cmd);
    }

    public Note? FindByTitle(string title)
    {
        Init();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT id, title, path, tags, created_at, updated_at FROM notes WHERE title = $title COLLATE NOCASE LIMIT 1";
        cmd.Parameters.AddWithValue("$title", title);
        return ReadNotes(cmd).FirstOrDefault();
    }

    public List<Note> Search(string query)
    {
        Init();
        using var conn = Open();

        // 短查询（<3 字符）trigram 索引不上，用 LIKE 兜底，保证中文二字词能搜到
        if (query.Length < 3)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT id, title, path, tags, created_at, updated_at FROM notes
                WHERE title LIKE $q OR path LIKE $q ORDER BY updated_at DESC
                """;
            cmd.Parameters.AddWithValue("$q", $"%{EscapeLike(query)}%");
            return ReadNotes(cmd);
        }

        using var cmd2 = conn.CreateCommand();
        cmd2.CommandText = """
            SELECT n.id, n.title, n.path, n.tags, n.created_at, n.updated_at
            FROM notes_fts f JOIN notes n ON n.id = f.note_id
            WHERE notes_fts MATCH $q
            ORDER BY n.updated_at DESC
            """;
        cmd2.Parameters.AddWithValue("$q", "\"" + query.Replace("\"", "\"\"") + "\"");
        return ReadNotes(cmd2);
    }

    public (int NoteCount, List<Tag> Tags, DateTime LatestUpdate) Stats()
    {
        Init();
        using var conn = Open();

        int count;
        DateTime latest;
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*), COALESCE(MAX(updated_at), '') FROM notes";
            using var r = cmd.ExecuteReader();
            r.Read();
            count = r.GetInt32(0);
            latest = DateTime.TryParse(r.GetString(1), out var t) ? t : DateTime.MinValue;
        }

        // 标签聚合（应用层做，MVP 笔记量小，够用）
        var tagCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT tags FROM notes";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                foreach (var t in UnpackTags(r.GetString(0)))
                    tagCounts[t] = tagCounts.GetValueOrDefault(t) + 1;
        }

        var tags = tagCounts.OrderByDescending(kv => kv.Value)
                            .Select(kv => new Tag { Name = kv.Key, Count = kv.Value })
                            .ToList();
        return (count, tags, latest);
    }

    // ---------- v0.2 双向链接 ----------

    public List<Note> Backlinks(string title)
    {
        Init();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT n.id, n.title, n.path, n.tags, n.created_at, n.updated_at
            FROM links l JOIN notes n ON n.id = l.source_id
            WHERE l.target COLLATE NOCASE = $title
            ORDER BY n.updated_at DESC
            """;
        cmd.Parameters.AddWithValue("$title", title);
        return ReadNotes(cmd);
    }

    public List<(string FromTitle, string ToTitle)> Graph()
    {
        Init();
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT n.title, l.target FROM links l JOIN notes n ON n.id = l.source_id
            """;
        var edges = new List<(string, string)>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
            edges.Add((r.GetString(0), r.GetString(1)));
        return edges;
    }

    public (int Added, int Updated, int Removed) Reindex()
    {
        Init();
        using var conn = Open();

        // 文件清单（排除元数据目录）
        var files = Directory.GetFiles(_root, "*.md", SearchOption.AllDirectories)
                             .Where(f => !f.StartsWith(Path.Combine(_root, MetaDir), StringComparison.OrdinalIgnoreCase))
                             .ToList();

        // 现有 DB 记录
        var byPath = new Dictionary<string, Note>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT id, title, path, tags, created_at, updated_at FROM notes";
            foreach (var n in ReadNotes(cmd))
                byPath[n.Path] = n;
        }

        // 旧正文（content 只存在 FTS 表）
        var contentByNote = new Dictionary<string, string>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT note_id, content FROM notes_fts";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                contentByNote[r.GetString(0)] = r.GetString(1);
        }

        int added = 0, updated = 0;

        foreach (var file in files)
        {
            var rel = Path.GetRelativePath(_root, file);
            var full = File.ReadAllText(file, Encoding.UTF8);
            var (fm, body) = SplitFrontmatter(full);
            var title = Path.GetFileNameWithoutExtension(rel);
            var tags = ParseFrontmatterTags(fm);
            var links = ExtractWikiLinks(body);

            if (byPath.TryGetValue(rel, out var existing))
            {
                var changed = contentByNote.GetValueOrDefault(existing.Id) != body
                           || !existing.Tags.SequenceEqual(tags, StringComparer.OrdinalIgnoreCase);
                if (changed)
                {
                    using var tx = conn.BeginTransaction();
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.Transaction = tx;
                        cmd.CommandText = """
                            UPDATE notes SET title = $title, tags = $tags, updated_at = $updated
                            WHERE id = $id
                            """;
                        cmd.Parameters.AddWithValue("$title", title);
                        cmd.Parameters.AddWithValue("$tags", PackTags(tags));
                        cmd.Parameters.AddWithValue("$updated", DateTime.Now.ToString("o"));
                        cmd.Parameters.AddWithValue("$id", existing.Id);
                        cmd.ExecuteNonQuery();
                    }
                    ReplaceFts(conn, tx, existing.Id, title, body);
                    ReplaceLinks(conn, tx, existing.Id, links);
                    tx.Commit();
                    updated++;
                }
                byPath.Remove(rel);
            }
            else
            {
                var note = new Note
                {
                    Title = title,
                    Path = rel,
                    Tags = tags,
                    Content = body,
                    CreatedAt = File.GetCreationTime(file),
                    UpdatedAt = File.GetLastWriteTime(file),
                };
                using var tx = conn.BeginTransaction();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = """
                        INSERT INTO notes (id, title, path, tags, created_at, updated_at)
                        VALUES ($id, $title, $path, $tags, $created, $updated)
                        """;
                    cmd.Parameters.AddWithValue("$id", note.Id);
                    cmd.Parameters.AddWithValue("$title", note.Title);
                    cmd.Parameters.AddWithValue("$path", note.Path);
                    cmd.Parameters.AddWithValue("$tags", PackTags(note.Tags));
                    cmd.Parameters.AddWithValue("$created", note.CreatedAt.ToString("o"));
                    cmd.Parameters.AddWithValue("$updated", note.UpdatedAt.ToString("o"));
                    cmd.ExecuteNonQuery();
                }
                InsertFts(conn, tx, note);
                ReplaceLinks(conn, tx, note.Id, links);
                tx.Commit();
                added++;
            }
        }

        // 文件已删除的 DB 记录
        int removed = 0;
        foreach (var orphan in byPath.Values)
        {
            using var tx = conn.BeginTransaction();
            DeleteNote(conn, tx, orphan.Id);
            tx.Commit();
            removed++;
        }

        return (added, updated, removed);
    }

    // ---------- 私有工具 ----------

    private SqliteConnection Open()
    {
        var conn = new SqliteConnection($"Data Source={_dbPath}");
        conn.Open();
        return conn;
    }

    private static void InsertFts(SqliteConnection conn, SqliteTransaction tx, Note note)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO notes_fts (note_id, title, content) VALUES ($id, $title, $content)";
        cmd.Parameters.AddWithValue("$id", note.Id);
        cmd.Parameters.AddWithValue("$title", note.Title);
        cmd.Parameters.AddWithValue("$content", note.Content);
        cmd.ExecuteNonQuery();
    }

    private static void ReplaceFts(SqliteConnection conn, SqliteTransaction tx, string id, string title, string content)
    {
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM notes_fts WHERE note_id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
        using var cmd2 = conn.CreateCommand();
        cmd2.Transaction = tx;
        cmd2.CommandText = "INSERT INTO notes_fts (note_id, title, content) VALUES ($id, $title, $content)";
        cmd2.Parameters.AddWithValue("$id", id);
        cmd2.Parameters.AddWithValue("$title", title);
        cmd2.Parameters.AddWithValue("$content", content);
        cmd2.ExecuteNonQuery();
    }

    private static void DeleteNote(SqliteConnection conn, SqliteTransaction tx, string id)
    {
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM notes_fts WHERE note_id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM links WHERE source_id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM notes WHERE id = $id";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }
    }

    private static void ReplaceLinks(SqliteConnection conn, SqliteTransaction tx, string sourceId, List<string> targets)
    {
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM links WHERE source_id = $id";
            cmd.Parameters.AddWithValue("$id", sourceId);
            cmd.ExecuteNonQuery();
        }
        foreach (var t in targets.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO links (source_id, target) VALUES ($id, $target)";
            cmd.Parameters.AddWithValue("$id", sourceId);
            cmd.Parameters.AddWithValue("$target", t);
            cmd.ExecuteNonQuery();
        }
    }

    private static List<Note> ReadNotes(SqliteCommand cmd)
    {
        var notes = new List<Note>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            notes.Add(new Note
            {
                Id = r.GetString(0),
                Title = r.GetString(1),
                Path = r.GetString(2),
                Tags = UnpackTags(r.GetString(3)),
                CreatedAt = DateTime.Parse(r.GetString(4)),
                UpdatedAt = DateTime.Parse(r.GetString(5)),
            });
        }
        return notes;
    }

    /// <summary>tags 打包：",a,b," 形式，方便 LIKE 精确匹配</summary>
    private static string PackTags(IEnumerable<string> tags)
        => "," + string.Join(",", tags.Select(t => t.Replace(",", ""))) + ",";

    private static List<string> UnpackTags(string packed)
        => packed.Trim(',').Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

    /// <summary>Windows 非法文件名字符替换为 _</summary>
    private static string SanitizeFileName(string title)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(title.Length);
        foreach (var c in title.Trim())
            sb.Append(invalid.Contains(c) ? '_' : c);
        var result = sb.ToString().Trim();
        if (result.Length == 0) result = "未命名";
        if (result.Length > 100) result = result[..100];
        return result;
    }

    private static string EscapeLike(string s)
        => s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    // ---------- frontmatter 与 wikilink ----------

    private static readonly Regex WikiLinkRegex = WikiLinkPattern();

    [GeneratedRegex(@"\[\[([^\]|]+)(?:\|([^\]]+))?\]\]", RegexOptions.Compiled)]
    private static partial Regex WikiLinkPattern();

    /// <summary>生成 YAML frontmatter（Obsidian 兼容的 tags 写法），统一 \n 换行</summary>
    public static string BuildFrontmatter(IEnumerable<string> tags)
    {
        var sb = new StringBuilder();
        sb.Append("---\n");
        var list = tags.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        if (list.Count > 0)
        {
            sb.Append("tags:\n");
            foreach (var t in list)
                sb.Append($"  - {t}\n");
        }
        sb.Append($"created: {DateTime.Now:yyyy-MM-dd}\n");
        sb.Append("---\n\n");
        return sb.ToString();
    }

    /// <summary>拆 frontmatter：返回 (frontmatter 文本, 正文)。兼容 \r\n 文件。</summary>
    public static (string Frontmatter, string Body) SplitFrontmatter(string fileContent)
    {
        var text = fileContent.Replace("\r\n", "\n");
        if (text.StartsWith("---\n", StringComparison.Ordinal))
        {
            var end = text.IndexOf("\n---\n", 4, StringComparison.Ordinal);
            if (end > 0)
            {
                var body = text[(end + 5)..];
                if (body.StartsWith('\n')) body = body[1..]; // 去掉 frontmatter 后的空行
                return (text[4..end], body);
            }
        }
        return ("", text);
    }

    /// <summary>解析 frontmatter 里的 tags 列表</summary>
    public static List<string> ParseFrontmatterTags(string fm)
    {
        var tags = new List<string>();
        var inTags = false;
        foreach (var raw in fm.Split('\n'))
        {
            var line = raw.Trim();
            if (line == "tags:" || line == "tags: []")
            {
                inTags = true;
                continue;
            }
            if (inTags)
            {
                if (line.StartsWith("- ", StringComparison.Ordinal))
                    tags.Add(line[2..].Trim());
                else if (line.StartsWith('#'))
                    tags.Add(line[1..].Trim());
                else if (line.Length > 0)
                    inTags = false; // 离开 tags 块
            }
        }
        return tags;
    }

    /// <summary>提取正文中的 [[wikilink]] 目标，跳过 ![[嵌入]]</summary>
    public static List<string> ExtractWikiLinks(string body)
    {
        var links = new List<string>();
        foreach (Match m in WikiLinkRegex.Matches(body))
        {
            if (m.Index > 0 && body[m.Index - 1] == '!') continue;
            var target = m.Groups[1].Value.Trim();
            if (target.Length > 0) links.Add(target);
        }
        return links;
    }
}
