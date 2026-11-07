using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TrayLens;

/// <summary>
/// 读 Clash Verge / mihomo 的控制 API，拿当前节点和延迟。
/// 新版 Clash Verge 把 API 只挂在命名管道上（external-controller-pipe），
/// 所以这里说的是「管道上的 HTTP」，不是 TCP。
/// </summary>
internal static class ClashClient
{
    internal readonly record struct ClashStatus(bool Ok, string Mode, bool Tun, string Node, int Delay);

    private static string? _pipeName;
    private static string? _secret;
    private static DateTime _discoveredAt = DateTime.MinValue;

    private static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "io.github.clash-verge-rev.clash-verge-rev", "clash-verge.yaml");

    private static void Discover()
    {
        if (_pipeName is not null && (DateTime.Now - _discoveredAt).TotalMinutes < 5) return;

        _discoveredAt = DateTime.Now;
        _pipeName = null;
        _secret = null;

        try
        {
            if (!File.Exists(ConfigPath)) return;
            foreach (string line in File.ReadLines(ConfigPath))
            {
                string t = line.Trim();
                if (t.StartsWith("external-controller-pipe:", StringComparison.Ordinal))
                {
                    string v = t["external-controller-pipe:".Length..].Trim().Trim('\'', '"');
                    if (v.StartsWith(@"\\.\pipe\", StringComparison.Ordinal))
                        _pipeName = v[@"\\.\pipe\".Length..];
                }
                else if (t.StartsWith("secret:", StringComparison.Ordinal))
                {
                    string v = t["secret:".Length..].Trim().Trim('\'', '"');
                    if (v.Length > 0 && v != "set-your-secret") _secret = v;
                    else if (v.Length > 0) _secret = v; // mihomo 也接受占位串
                }
            }
        }
        catch
        {
            _pipeName = null;
        }
    }

    public static ClashStatus Query(int timeoutMs = 1200)
    {
        Discover();
        if (_pipeName is null) return default;

        try
        {
            JsonElement configs = GetJson(_pipeName, "/configs", _secret).GetAwaiter().GetResult();
            string mode = configs.TryGetProperty("mode", out var m) ? m.GetString() ?? "" : "";
            bool tun = configs.TryGetProperty("tun", out var t)
                       && t.TryGetProperty("enable", out var te)
                       && te.ValueKind == JsonValueKind.True;

            if (mode == "direct") return new ClashStatus(true, mode, tun, "", -1);

            JsonElement proxies = GetJson(_pipeName, "/proxies", _secret).GetAwaiter().GetResult();
            if (!proxies.TryGetProperty("proxies", out JsonElement map)) return new ClashStatus(true, mode, tun, "", -1);

            // 找主选择组：按名字打分，别被「自动选择」这种 URLTest 组抢走
            var candidates = new List<(int score, string name, string now)>();
            foreach (JsonProperty p in map.EnumerateObject())
            {
                string type = p.Value.TryGetProperty("type", out var ty) ? ty.GetString() ?? "" : "";
                if (type != "Selector") continue;
                if (!p.Value.TryGetProperty("now", out var nowEl) || nowEl.ValueKind != JsonValueKind.String) continue;

                string name = p.Name;
                int score = name switch
                {
                    "节点选择" => 100,
                    "Proxy" or "PROXY" or "代理" => 95,
                    _ when name.Contains("🚀") => 90,
                    _ when name.Contains("节点") => 80,
                    _ when name.Contains("代理") => 70,
                    _ when name.Contains("GLOBAL") => 60,
                    _ => 10
                };
                candidates.Add((score, name, nowEl.GetString() ?? ""));
            }

            string? currentNode = candidates
                .OrderByDescending(c => c.score)
                .Select(c => c.now)
                .FirstOrDefault();

            // 选中的可能还是个子组（比如 节点选择 -> 香港组），顺着链走到真正的节点
            for (int depth = 0; depth < 5 && currentNode is not null; depth++)
            {
                if (!map.TryGetProperty(currentNode, out JsonElement el)) break;
                string nodeType = el.TryGetProperty("type", out var tt) ? tt.GetString() ?? "" : "";
                if (nodeType is not ("Selector" or "URLTest" or "Fallback" or "LoadBalance")) break;
                if (!el.TryGetProperty("now", out var inner) || inner.ValueKind != JsonValueKind.String) break;
                string next = inner.GetString() ?? "";
                if (next.Length == 0 || next == currentNode) break;
                currentNode = next;
            }

            int delay = -1;
            if (currentNode is not null && map.TryGetProperty(currentNode, out JsonElement nodeEl)
                && nodeEl.TryGetProperty("history", out JsonElement hist)
                && hist.ValueKind == JsonValueKind.Array && hist.GetArrayLength() > 0)
            {
                JsonElement last = hist[hist.GetArrayLength() - 1];
                if (last.TryGetProperty("delay", out var d) && d.TryGetInt32(out int dv)) delay = dv;
            }

            return new ClashStatus(true, mode, tun, currentNode ?? "", delay);
        }
        catch
        {
            _pipeName = null;
            return default;
        }
    }

    private static async Task<JsonElement> GetJson(string pipeName, string path, string? secret)
    {
        string body = await Task.Run(() => HttpOverPipe(pipeName, path, secret)).ConfigureAwait(false);
        using JsonDocument doc = JsonDocument.Parse(body);
        return doc.RootElement.Clone();
    }

    private static string HttpOverPipe(string pipeName, string path, string? secret)
    {
        using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.None, System.Security.Principal.TokenImpersonationLevel.Impersonation);
        client.Connect(1500);

        var sb = new StringBuilder();
        sb.Append("GET ").Append(path).Append(" HTTP/1.1\r\n");
        sb.Append("Host: localhost\r\n");
        if (!string.IsNullOrEmpty(secret)) sb.Append("Authorization: Bearer ").Append(secret).Append("\r\n");
        sb.Append("Connection: close\r\n\r\n");

        byte[] request = Encoding.UTF8.GetBytes(sb.ToString());
        client.Write(request, 0, request.Length);
        client.Flush();

        using var ms = new MemoryStream();
        byte[] buffer = new byte[16 * 1024];
        int read;
        while ((read = client.Read(buffer, 0, buffer.Length)) > 0)
        {
            ms.Write(buffer, 0, read);
        }

        byte[] all = ms.ToArray();
        int headerEnd = IndexOf(all, "\r\n\r\n"u8.ToArray());
        if (headerEnd < 0) return "";

        string headers = Encoding.ASCII.GetString(all, 0, headerEnd);
        byte[] bodyBytes = all[(headerEnd + 4)..];

        bool chunked = headers.Contains("chunked", StringComparison.OrdinalIgnoreCase);
        if (!chunked) return Encoding.UTF8.GetString(bodyBytes);

        return DecodeChunked(bodyBytes);
    }

    private static string DecodeChunked(byte[] data)
    {
        var output = new MemoryStream();
        int pos = 0;

        while (pos < data.Length)
        {
            int lineEnd = IndexOf(data, "\r\n"u8.ToArray(), pos);
            if (lineEnd < 0) break;

            string sizeLine = Encoding.ASCII.GetString(data, pos, lineEnd - pos).Split(';')[0].Trim();
            if (!int.TryParse(sizeLine, System.Globalization.NumberStyles.HexNumber, null, out int size) || size == 0)
                break;

            pos = lineEnd + 2;
            if (pos + size > data.Length) break;
            output.Write(data, pos, size);
            pos += size + 2;
        }

        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static int IndexOf(byte[] haystack, byte[] needle, int start = 0)
    {
        for (int i = start; i <= haystack.Length - needle.Length; i++)
        {
            bool ok = true;
            for (int j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j]) { ok = false; break; }
            }
            if (ok) return i;
        }
        return -1;
    }
}
