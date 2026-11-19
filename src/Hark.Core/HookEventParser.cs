using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Hark.Core
{
    public static class HookEventParser
    {
        public static bool TryParse(string json, DateTime receivedUtc, out HookEvent e)
        {
            e = null;
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }
            try
            {
                using (var doc = JsonDocument.Parse(json))
                {
                    var r = doc.RootElement;
                    if (r.ValueKind != JsonValueKind.Object)
                    {
                        return false;
                    }
                    var name = Str(r, "event");
                    var session = Str(r, "session");
                    if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(session))
                    {
                        return false;
                    }
                    e = new HookEvent
                    {
                        Ts = Long(r, "ts"),
                        ReceivedUtc = receivedUtc,
                        Event = name,
                        SessionId = session,
                        Project = Str(r, "project"),
                        Cwd = Str(r, "cwd"),
                        Source = Str(r, "source") == "vscode" ? SourceKind.VsCode : SourceKind.Cli,
                        AgentId = Str(r, "agent_id"),
                        AgentType = Str(r, "agent_type"),
                        AgentTask = Str(r, "agent_task"),
                        Tool = Str(r, "tool"),
                        ToolUseId = Str(r, "tool_use_id"),
                        Summary = Str(r, "summary"),
                        NotificationType = Str(r, "notification_type"),
                        Message = Str(r, "message"),
                        Questions = Questions(r),
                        Error = Str(r, "error"),
                        LastMessage = Str(r, "last_message"),
                        Asks = r.TryGetProperty("asks", out var asks) && asks.ValueKind == JsonValueKind.True,
                        SpawnType = Str(r, "spawn_type"),
                        Todos = Todos(r),
                    };
                    return true;
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private static string Str(JsonElement r, string name) =>
            r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        private static long Long(JsonElement r, string name) =>
            r.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;

        private static IReadOnlyList<TodoItem> Todos(JsonElement r)
        {
            var list = new List<TodoItem>();
            if (r.TryGetProperty("todos", out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var t in array.EnumerateArray())
                {
                    if (t.ValueKind == JsonValueKind.Object)
                    {
                        list.Add(new TodoItem { Text = Str(t, "text"), Status = Str(t, "status") ?? "pending" });
                    }
                }
            }
            return list;
        }

        private static IReadOnlyList<string> Strings(JsonElement r, string name)
        {
            var list = new List<string>();
            if (r.TryGetProperty(name, out var array) && array.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in array.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        list.Add(item.GetString());
                    }
                }
            }
            return list;
        }

        private static IReadOnlyList<AskQuestion> Questions(JsonElement r)
        {
            if (!r.TryGetProperty("questions", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<AskQuestion>();
            }
            var result = new List<AskQuestion>();
            foreach (var q in list.EnumerateArray())
            {
                if (q.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }
                result.Add(new AskQuestion
                {
                    Text = Str(q, "question"),
                    Header = Str(q, "header"),
                    Options = Strings(q, "options"),
                    Descriptions = Strings(q, "descriptions"),
                    Multi = q.TryGetProperty("multi", out var m) && m.ValueKind == JsonValueKind.True,
                });
            }
            return result;
        }
    }
}
