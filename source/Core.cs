using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace DayDesk
{
    public class AppState
    {
        public int SchemaVersion { get; set; }
        public string MilestoneTitle { get; set; }
        public string DepartureDate { get; set; }
        public List<DepartureItem> DepartureItems { get; set; }
        public List<Track> Tracks { get; set; }
        public List<Todo> Todos { get; set; }
        public List<Note> Notes { get; set; }
        public List<HistoryEntry> History { get; set; }
        public List<string> AppliedUpdateIds { get; set; }
        public List<KeyEvent> KeyEvents { get; set; }
        public List<NotebookEntry> Notebook { get; set; }
        public bool? FunReminderEnabled { get; set; }
        public string LastFunReminderDate { get; set; }
    }
    public class KeyEvent { public string Id { get; set; } public string Title { get; set; } public string Date { get; set; } public string Time { get; set; } public string Repeat { get; set; } public int WeekDay { get; set; } public int MonthDay { get; set; } }
    public class NotebookEntry { public string Id { get; set; } public string Date { get; set; } public string Text { get; set; } public string Color { get; set; } public string SourceNoteId { get; set; } public string SourceText { get; set; } public string SavedAt { get; set; } }
    public class DepartureItem { public string Id { get; set; } public string Title { get; set; } public string DueDate { get; set; } public bool Done { get; set; } }
    public class Track { public string Id { get; set; } public string Title { get; set; } public string Color { get; set; } public List<TrackNode> Nodes { get; set; } public bool ApplicationsEnabled { get; set; } public string ApplicationsSourceUrl { get; set; } public List<JobApplication> Applications { get; set; } public Track() { Applications = new List<JobApplication>(); } }
    public class TrackNode { public string Id { get; set; } public string Title { get; set; } public string Status { get; set; } public List<NodeTask> Tasks { get; set; } }
    public class NodeTask { public string Id { get; set; } public string Title { get; set; } public bool Done { get; set; } }
    public class Todo { public string Id { get; set; } public string Title { get; set; } public string Date { get; set; } public bool Done { get; set; } public string TrackId { get; set; } public string NodeId { get; set; } public string NodeTaskId { get; set; } }
    public class Note { public string Id { get; set; } public string Date { get; set; } public string Text { get; set; } public string Color { get; set; } public bool Pinned { get; set; } }
    public class HistoryEntry { public string At { get; set; } public string Text { get; set; } }
    public class ImportPreview
    {
        public AppState Result { get; set; }
        public List<string> Descriptions { get; set; }
        public string Id { get; set; }
        public string Source { get; set; }
        public string OriginalJson { get; set; }
        internal string BaselineJson;
        internal string ResultJson;
    }

    public partial class DeskStore
    {
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = 16 * 1024 * 1024, RecursionLimit = 100 };
        private string undoBefore;
        private string undoAfter;
        public AppState State { get; private set; }
        public string DataDirectory { get; private set; }
        public string RecoveryMessage { get; private set; }
        public event Action Changed;
        public bool CanUndo { get { return undoBefore != null && Serialize(State) == undoAfter; } }
        private string StatePath { get { return Path.Combine(DataDirectory, "state.json"); } }
        private string BackupPath { get { return Path.Combine(DataDirectory, "state.backup.json"); } }

        public DeskStore(string dataDirectory)
        {
            if (String.IsNullOrWhiteSpace(dataDirectory)) throw new ArgumentException("需要数据目录。");
            DataDirectory = Path.GetFullPath(dataDirectory);
            Directory.CreateDirectory(DataDirectory);
            Directory.CreateDirectory(Path.Combine(DataDirectory, "inbox"));
            Load();
        }
        public static string Today() { return DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture); }
        public static string NewId() { return Guid.NewGuid().ToString("N"); }
        private string Serialize(AppState state) { return json.Serialize(state); }
        private AppState Deserialize(string value)
        {
            AppState state = json.Deserialize<AppState>(value);
            NormalizeOptionalState(state);
            ValidateState(state);
            return state;
        }
        private void Notify() { Action changed = Changed; if (changed != null) changed(); }
        private static void NormalizeOptionalState(AppState state)
        {
            if (state == null) return;
            if (state.KeyEvents == null)
            {
                state.KeyEvents = new List<KeyEvent>();
                if (!String.IsNullOrEmpty(state.DepartureDate) && ValidDate(state.DepartureDate, false))
                    state.KeyEvents.Add(new KeyEvent { Id = "legacy-milestone", Title = String.IsNullOrWhiteSpace(state.MilestoneTitle) ? "离境" : state.MilestoneTitle, Date = state.DepartureDate, Time = "09:00", Repeat = "none", MonthDay = 1 });
            }
            if (state.Notebook == null) state.Notebook = new List<NotebookEntry>();
            foreach (NotebookEntry entry in state.Notebook) if (entry != null && entry.SourceText == null) entry.SourceText = entry.Text;
            if (!state.FunReminderEnabled.HasValue) state.FunReminderEnabled = true;
            if (state.Tracks != null) foreach (Track track in state.Tracks) if (track != null && track.Applications == null) track.Applications = new List<JobApplication>();
        }

        private void Load()
        {
            RecoveryMessage = null;
            if (!File.Exists(StatePath) && !File.Exists(BackupPath))
            {
                State = CreateDefault();
                Save();
                return;
            }
            Exception primaryError = null;
            if (File.Exists(StatePath))
            {
                try { State = Deserialize(File.ReadAllText(StatePath, Encoding.UTF8)); return; }
                catch (Exception e) { primaryError = e; }
            }
            if (File.Exists(BackupPath))
            {
                try
                {
                    AppState recovered = Deserialize(File.ReadAllText(BackupPath, Encoding.UTF8));
                    string quarantined = null;
                    if (File.Exists(StatePath))
                    {
                        quarantined = Path.Combine(DataDirectory, "state.corrupt." + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + ".json");
                        File.Move(StatePath, quarantined);
                    }
                    State = recovered;
                    WriteAtomic(Serialize(State));
                    RecoveryMessage = "数据文件无法读取，已从上一次备份恢复。" + (quarantined == null ? "" : " 原文件保留在：" + quarantined);
                    return;
                }
                catch (Exception backupError)
                {
                    throw new IOException("数据和备份无法读取，已保留原文件。请检查数据目录：" + DataDirectory, backupError);
                }
            }
            throw new IOException("数据文件无法读取，已保留原文件，未创建空白数据。数据目录：" + DataDirectory, primaryError);
        }
        public void Reload()
        {
            AppState previous = State;
            try { Load(); }
            catch { State = previous; throw; }
            undoBefore = undoAfter = null;
            Notify();
        }
        private void WriteAtomic(string content)
        {
            string temporary = Path.Combine(DataDirectory, ".state-" + NewId() + ".tmp");
            try
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(content);
                using (FileStream file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    file.Write(bytes, 0, bytes.Length);
                    file.Flush(true);
                }
                if (File.Exists(StatePath)) File.Replace(temporary, StatePath, BackupPath, true);
                else File.Move(temporary, StatePath);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        public void Save()
        {
            ValidateState(State);
            string serialized = Serialize(State);
            WriteAtomic(serialized);
            if (undoAfter != null && undoAfter != serialized) undoBefore = undoAfter = null;
            Notify();
        }
        public bool Undo()
        {
            if (!CanUndo) return false;
            AppState previous = State;
            State = Deserialize(undoBefore);
            try { WriteAtomic(Serialize(State)); }
            catch { State = previous; throw; }
            undoBefore = undoAfter = null;
            Notify();
            return true;
        }

        // Management actions commit a validated copy. Failed validation or disk writes
        // leave the live state and its undo history untouched.
        private AppState CommitChange(Action<AppState> mutation)
        {
            AppState changed = Deserialize(Serialize(State));
            mutation(changed);
            ValidateState(changed);
            WriteAtomic(Serialize(changed));
            State = changed;
            undoBefore = undoAfter = null;
            Notify();
            return changed;
        }
        private static string CheckedTitle(string title, IEnumerable<string> peers)
        {
            if (String.IsNullOrWhiteSpace(title) || title.Trim().Length > 500) throw new ArgumentException("名称需要填写 1–500 个字符。");
            string trimmed = title.Trim();
            if (peers.Any(p => String.Equals(p == null ? null : p.Trim(), trimmed, StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("同一层级已存在这个名称，请使用不同名称。");
            return trimmed;
        }
        private static string CheckedMilestoneTitle(string title)
        {
            if (String.IsNullOrWhiteSpace(title) || title.Trim().Length > 100) throw new ArgumentException("关键日期名称需要填写 1–100 个字符。");
            return title.Trim();
        }
        public void UpdateMilestone(string title, string date)
        {
            CommitChange(delegate(AppState state)
            {
                state.MilestoneTitle = CheckedMilestoneTitle(title);
                if (!ValidDate(date, true)) throw new ArgumentException("日期必须是有效的 yyyy-MM-dd 日期或空字符串。");
                state.DepartureDate = date ?? "";
                SyncLegacyMilestone(state);
            });
        }
        private static void SyncLegacyMilestone(AppState state)
        {
            KeyEvent item = state.KeyEvents.FirstOrDefault(e => e.Id == "legacy-milestone");
            if (String.IsNullOrEmpty(state.DepartureDate)) { if (item != null) state.KeyEvents.Remove(item); return; }
            if (item == null) { item = new KeyEvent { Id = "legacy-milestone", Time = "09:00", Repeat = "none", MonthDay = 1 }; state.KeyEvents.Add(item); }
            item.Title = String.IsNullOrWhiteSpace(state.MilestoneTitle) ? "离境" : state.MilestoneTitle;
            item.Date = state.DepartureDate;
            item.Repeat = "none";
        }
        private static void ValidateEvent(KeyEvent item)
        {
            if (item == null) throw new ArgumentException("关键事项不能为空。");
            CheckedMilestoneTitle(item.Title);
            DateTime time;
            if (!DateTime.TryParseExact(item.Time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out time)) throw new ArgumentException("时间格式需要使用 HH:mm，例如 09:00。");
            if (item.Repeat == "none") { if (!ValidDate(item.Date, false)) throw new ArgumentException("一次性事项需要有效的 yyyy-MM-dd 日期。"); }
            else if (item.Repeat == "weekly") { if (item.WeekDay < 0 || item.WeekDay > 6) throw new ArgumentException("星期需要在 0（周日）到 6（周六）之间。"); }
            else if (item.Repeat == "monthly") { if (item.MonthDay < 1 || item.MonthDay > 31) throw new ArgumentException("每月日期需要在 1–31 之间。"); }
            else throw new ArgumentException("重复方式需要是 none、weekly 或 monthly。");
        }
        public void SaveKeyEvent(KeyEvent item)
        {
            ValidateEvent(item);
            string id = String.IsNullOrWhiteSpace(item.Id) ? NewId() : item.Id;
            KeyEvent copy = new KeyEvent { Id = id, Title = item.Title.Trim(), Date = item.Date, Time = item.Time, Repeat = item.Repeat, WeekDay = item.WeekDay, MonthDay = item.MonthDay };
            CommitChange(delegate(AppState state)
            {
                int index = state.KeyEvents.FindIndex(e => e.Id == id);
                if (index < 0) state.KeyEvents.Add(copy); else state.KeyEvents[index] = copy;
                if (id == "legacy-milestone") { state.MilestoneTitle = copy.Title; state.DepartureDate = copy.Repeat == "none" ? copy.Date : ""; }
            });
        }
        public void DeleteKeyEvent(string id)
        {
            CommitChange(delegate(AppState state)
            {
                KeyEvent item = state.KeyEvents.FirstOrDefault(e => e.Id == id);
                if (item == null) throw new ArgumentException("关键事项不存在。");
                state.KeyEvents.Remove(item);
                if (id == "legacy-milestone") state.DepartureDate = "";
            });
        }
        public static DateTime? NextOccurrence(KeyEvent item, DateTime now)
        {
            ValidateEvent(item);
            TimeSpan time = TimeSpan.ParseExact(item.Time, "hh\\:mm", CultureInfo.InvariantCulture);
            if (item.Repeat == "none") return DateTime.ParseExact(item.Date, "yyyy-MM-dd", CultureInfo.InvariantCulture).Add(time);
            if (item.Repeat == "weekly")
            {
                int days = (item.WeekDay - (int)now.DayOfWeek + 7) % 7;
                DateTime next = now.Date.AddDays(days).Add(time);
                return next < now ? next.AddDays(7) : next;
            }
            int day = Math.Min(item.MonthDay, DateTime.DaysInMonth(now.Year, now.Month));
            DateTime monthly = new DateTime(now.Year, now.Month, day).Add(time);
            if (monthly >= now) return monthly;
            DateTime nextMonth = new DateTime(now.Year, now.Month, 1).AddMonths(1);
            return new DateTime(nextMonth.Year, nextMonth.Month, Math.Min(item.MonthDay, DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month))).Add(time);
        }
        public int ArchiveNotes(string date)
        {
            if (!ValidDate(date, false)) throw new ArgumentException("归档日期需要使用 yyyy-MM-dd。");
            int count = 0;
            CommitChange(delegate(AppState state)
            {
                List<Note> selected = state.Notes.Where(n => n.Date == date).ToList();
                foreach (Note note in selected)
                {
                    if (String.IsNullOrWhiteSpace(note.Text)) continue;
                    NotebookEntry entry = state.Notebook.FirstOrDefault(n => n.SourceNoteId == note.Id && n.SourceText == note.Text);
                    if (entry == null)
                    {
                        entry = new NotebookEntry { Id = NewId(), SourceNoteId = note.Id, SourceText = note.Text, Date = note.Date, Text = note.Text, Color = note.Color, SavedAt = DateTimeOffset.Now.ToString("o") };
                        state.Notebook.Add(entry);
                        count++;
                    }
                    if (!note.Pinned) state.Notes.Remove(note);
                }
            });
            return count;
        }
        public void UpdateNotebookEntry(string id, string text)
        {
            if (text == null || text.Length > 20000) throw new ArgumentException("笔记内容不能超过 20,000 个字符。");
            CommitChange(delegate(AppState state)
            {
                NotebookEntry entry = state.Notebook.FirstOrDefault(n => n.Id == id);
                if (entry == null) throw new ArgumentException("笔记不存在。");
                entry.Text = text; entry.SavedAt = DateTimeOffset.Now.ToString("o");
            });
        }
        public void DeleteNotebookEntry(string id)
        {
            CommitChange(delegate(AppState state)
            {
                NotebookEntry entry = state.Notebook.FirstOrDefault(n => n.Id == id);
                if (entry == null) throw new ArgumentException("笔记不存在。");
                state.Notebook.Remove(entry);
            });
        }
        public void MarkFunReminded(string date)
        {
            if (!ValidDate(date, false)) throw new ArgumentException("提醒日期需要使用 yyyy-MM-dd。");
            CommitChange(delegate(AppState state) { state.LastFunReminderDate = date; });
        }
        public void SetFunReminderEnabled(bool enabled)
        {
            CommitChange(delegate(AppState state) { state.FunReminderEnabled = enabled; });
        }
        private static Track AddTrackIn(AppState state, string title, IEnumerable<string> nodeTitles)
        {
            string name = CheckedTitle(title, state.Tracks.Select(t => t.Title));
            List<string> titles = nodeTitles == null ? new List<string>() : nodeTitles.ToList();
            if (titles.Count > 200) throw new ArgumentException("一次最多创建 200 个路线节点。");
            string[] palette = { "#718E78", "#8B81AE", "#BC8F57", "#648A9E" };
            Track added = new Track { Id = NewId(), Title = name, Color = palette[state.Tracks.Count % palette.Length], Nodes = new List<TrackNode>() };
            foreach (string nodeTitle in titles)
                added.Nodes.Add(new TrackNode { Id = NewId(), Title = CheckedTitle(nodeTitle, added.Nodes.Select(n => n.Title)), Status = added.Nodes.Count == 0 ? "current" : "pending", Tasks = new List<NodeTask>() });
            state.Tracks.Add(added);
            return added;
        }
        private static TrackNode AddNodeIn(Track track, string title)
        {
            TrackNode added = new TrackNode { Id = NewId(), Title = CheckedTitle(title, track.Nodes.Select(n => n.Title)), Status = track.Nodes.Count == 0 ? "current" : "pending", Tasks = new List<NodeTask>() };
            track.Nodes.Add(added);
            return added;
        }
        public Track AddTrack(string title, IEnumerable<string> nodeTitles)
        {
            string addedId = null;
            AppState result = CommitChange(delegate(AppState state) { addedId = AddTrackIn(state, title, nodeTitles).Id; });
            return FindTrack(result, addedId);
        }
        public void RenameTrack(string trackId, string title)
        {
            CommitChange(delegate(AppState state)
            {
                Track track = FindTrack(state, trackId);
                track.Title = CheckedTitle(title, state.Tracks.Where(t => t.Id != trackId).Select(t => t.Title));
            });
        }
        public void MoveTrack(string trackId, int offset)
        {
            if (offset != -1 && offset != 1) throw new ArgumentException("主线每次只能上移或下移一位。");
            CommitChange(delegate(AppState state)
            {
                Track track = FindTrack(state, trackId);
                int oldIndex = state.Tracks.IndexOf(track);
                int newIndex = oldIndex + offset;
                if (newIndex < 0 || newIndex >= state.Tracks.Count) throw new ArgumentException("主线已在列表边界，无法继续移动。");
                state.Tracks.RemoveAt(oldIndex);
                state.Tracks.Insert(newIndex, track);
            });
        }
        private static void ClearTodoLink(Todo todo)
        {
            todo.TrackId = null; todo.NodeId = null; todo.NodeTaskId = null;
        }
        public void DeleteTrack(string trackId)
        {
            CommitChange(delegate(AppState state)
            {
                Track track = FindTrack(state, trackId);
                foreach (Todo todo in state.Todos.Where(t => t.TrackId == trackId)) ClearTodoLink(todo);
                state.Tracks.Remove(track);
            });
        }
        public TrackNode AddNode(string trackId, string title)
        {
            string addedId = null;
            AppState result = CommitChange(delegate(AppState state) { addedId = AddNodeIn(FindTrack(state, trackId), title).Id; });
            return FindNode(FindTrack(result, trackId), addedId);
        }
        public void RenameNode(string trackId, string nodeId, string title)
        {
            CommitChange(delegate(AppState state)
            {
                Track track = FindTrack(state, trackId);
                FindNode(track, nodeId).Title = CheckedTitle(title, track.Nodes.Where(n => n.Id != nodeId).Select(n => n.Title));
            });
        }
        public void DeleteNode(string trackId, string nodeId)
        {
            CommitChange(delegate(AppState state)
            {
                Track track = FindTrack(state, trackId);
                TrackNode node = FindNode(track, nodeId);
                foreach (Todo todo in state.Todos.Where(t => t.TrackId == trackId && t.NodeId == nodeId)) ClearTodoLink(todo);
                track.Nodes.Remove(node);
            });
        }
        private static void RenameTaskIn(AppState state, Track track, TrackNode node, NodeTask task, string title)
        {
            string name = CheckedTitle(title, node.Tasks.Where(t => t.Id != task.Id).Select(t => t.Title));
            string previousTitle = task.Title;
            task.Title = name;
            foreach (Todo todo in state.Todos.Where(t => t.TrackId == track.Id && t.NodeId == node.Id && t.NodeTaskId == task.Id && t.Title == previousTitle)) todo.Title = name;
        }
        public void RenameTask(string trackId, string nodeId, string taskId, string title)
        {
            CommitChange(delegate(AppState state)
            {
                Track track = FindTrack(state, trackId); TrackNode node = FindNode(track, nodeId);
                RenameTaskIn(state, track, node, FindTask(node, taskId), title);
            });
        }
        public void DeleteTask(string trackId, string nodeId, string taskId)
        {
            CommitChange(delegate(AppState state)
            {
                Track track = FindTrack(state, trackId); TrackNode node = FindNode(track, nodeId); NodeTask task = FindTask(node, taskId);
                foreach (Todo todo in state.Todos.Where(t => t.TrackId == trackId && t.NodeId == nodeId && t.NodeTaskId == taskId)) todo.NodeTaskId = null;
                node.Tasks.Remove(task);
            });
        }

        public void SetTodoDone(Todo todo, bool done)
        {
            if (todo == null || !State.Todos.Contains(todo)) throw new ArgumentException("待办不存在。");
            SetTodoDoneIn(State, todo, done);
        }
        private static void SetTodoDoneIn(AppState state, Todo todo, bool done)
        {
            todo.Done = done;
            Track track = state.Tracks.FirstOrDefault(t => t.Id == todo.TrackId);
            TrackNode node = track == null ? null : track.Nodes.FirstOrDefault(n => n.Id == todo.NodeId);
            if (node == null) return;
            NodeTask linked = null;
            if (!String.IsNullOrEmpty(todo.NodeTaskId)) linked = node.Tasks.FirstOrDefault(t => t.Id == todo.NodeTaskId);
            else
            {
                List<NodeTask> matches = node.Tasks.Where(t => t.Title == todo.Title).ToList();
                if (matches.Count == 1) { linked = matches[0]; todo.NodeTaskId = linked.Id; }
            }
            if (linked != null) SetNodeTaskDoneIn(state, track, node, linked, done);
        }
        public void SetNodeTaskDone(Track track, TrackNode node, NodeTask task, bool done)
        {
            if (track == null || node == null || task == null || !State.Tracks.Contains(track) || !track.Nodes.Contains(node) || !node.Tasks.Contains(task)) throw new ArgumentException("节点任务不存在。");
            SetNodeTaskDoneIn(State, track, node, task, done);
        }
        private static void SetNodeTaskDoneIn(AppState state, Track track, TrackNode node, NodeTask task, bool done)
        {
            task.Done = done;
            if (!done && node.Status == "done") node.Status = "pending";
            foreach (Todo todo in state.Todos.Where(t => t.TrackId == track.Id && t.NodeId == node.Id && (t.NodeTaskId == task.Id || (String.IsNullOrEmpty(t.NodeTaskId) && t.Title == task.Title && node.Tasks.Count(n => n.Title == task.Title) == 1))))
            {
                todo.NodeTaskId = task.Id;
                todo.Done = done;
            }
        }

        public string ExportContext()
        {
            return json.Serialize(new { schemaVersion = 1, today = Today(), instructions = "请按用户原文生成独立 JSON 更新文件。格式：{id:唯一更新ID,source:来源,operations:[操作]}。主线完全由用户定义，没有固定的主线或节点 ID。操作已有项目时使用下面的准确 ID；不要编造完成记录、推断整章完成或自动推进节点。进度不明确时只 add_note。新建主线可用 add_track 的 nodes 一次提供路线；新 ID 由应用生成，需要再次复制当前数据后才能引用。先 complete_node，再 set_current_node 才表示明确结束上一节点并开始下一节点。", operationSchema = new[] {
                "add_track: title, nodes?(字符串数组，最多 200 个；首节点 current，其余 pending)",
                "rename_track: trackId, title",
                "upsert_application: trackId, company, status(planned/applied/assessment/interview/offer/rejected/watch/no_response), applicationId?, role?, appliedDate?(yyyy-MM-dd或空), notes?；无ID按同一主线的公司+岗位匹配，重复更新不会新增一条；仅记录用户明确的投递状态",
                "set_applications_source: trackId, url(http/https或空；仅保存清单链接，不自动同步)",
                "add_node: trackId, title (主线为空时 current，否则 pending)",
                "rename_node: trackId, nodeId, title",
                "rename_task: trackId, nodeId, taskId, title (保留自定义待办标题)",
                "set_milestone: title(1–100字符), date?(yyyy-MM-dd；省略保留原日期，空字符串清除日期)",
                "add_event: title, date?(一次性必填), time?(默认09:00), repeat?(none/weekly/monthly，默认none), weekDay?(每周必填0周日–6周六), monthDay?(每月必填1–31；短月取月末)",
                "update_event: eventId, title?, date?, time?, repeat?, weekDay?, monthDay? (省略字段保留原值)",
                "add_todo: title, date?(yyyy-MM-dd，默认今天), trackId?, nodeId?, nodeTaskId?, done?(默认 false；关联已有任务时默认继承状态)",
                "complete_todo: todoId, done?(默认 true)",
                "add_task: trackId, nodeId, title, done?(默认 false)",
                "complete_task: trackId, nodeId, taskId, done?(默认 true)",
                "set_current_node: trackId, nodeId",
                "complete_node: trackId, nodeId (同时完成其子任务)",
                "add_note: text, date?(默认今天), color?(hex)",
                "add_departure: title, dueDate?(yyyy-MM-dd)",
                "complete_departure: departureId, done?(默认 true)",
                "set_departure_date: date(yyyy-MM-dd或空字符串；兼容历史名称，更新当前关键日期)"
            }, state = State });
        }

        public ImportPreview PreviewImport(string input)
        {
            if (String.IsNullOrWhiteSpace(input) || input.Length > 2 * 1024 * 1024) throw new ArgumentException("更新文件为空或超过 2 MB。");
            Dictionary<string, object> root;
            try { root = json.DeserializeObject(input) as Dictionary<string, object>; }
            catch (Exception e) { throw new ArgumentException("更新文件不是有效 JSON。", e); }
            if (root == null) throw new ArgumentException("更新文件必须是 JSON 对象。");
            CheckKeys(root, "id", "source", "operations");
            string id = Required(root, "id", 160);
            if (State.AppliedUpdateIds.Contains(id)) throw new ArgumentException("这份更新已应用，无需重复导入。");
            string source = Optional(root, "source", "AI 更新", 160);
            object operationsRaw;
            object[] operations = root.TryGetValue("operations", out operationsRaw) ? operationsRaw as object[] : null;
            if (operations == null || operations.Length == 0 || operations.Length > 200) throw new ArgumentException("operations 必须包含 1–200 个操作。");
            string baseline = Serialize(State);
            AppState result = Deserialize(baseline);
            List<string> descriptions = new List<string>();
            for (int i = 0; i < operations.Length; i++)
            {
                Dictionary<string, object> operation = operations[i] as Dictionary<string, object>;
                if (operation == null) throw new ArgumentException("第 " + (i + 1) + " 个操作必须是对象。");
                try { ApplyOperation(result, operation, descriptions); }
                catch (ArgumentException e) { throw new ArgumentException("第 " + (i + 1) + " 个操作：" + e.Message, e); }
            }
            result.AppliedUpdateIds.Add(id);
            result.History.Add(new HistoryEntry { At = DateTimeOffset.Now.ToString("o"), Text = source + "：" + String.Join("；", descriptions) });
            ValidateState(result);
            return new ImportPreview { Result = result, Id = id, Source = source, OriginalJson = input, Descriptions = descriptions, BaselineJson = baseline, ResultJson = Serialize(result) };
        }
        public void ApplyImport(ImportPreview preview)
        {
            if (preview == null || preview.BaselineJson == null || preview.ResultJson == null) throw new ArgumentException("请先预览更新。");
            if (Serialize(State) != preview.BaselineJson) throw new InvalidOperationException("数据已发生变化，请重新预览后再应用。");
            AppState previous = State;
            AppState applied = Deserialize(preview.ResultJson);
            WriteAtomic(preview.ResultJson);
            State = applied;
            undoBefore = Serialize(previous);
            undoAfter = preview.ResultJson;
            Notify();
        }

        private static void ApplyOperation(AppState state, Dictionary<string, object> op, List<string> descriptions)
        {
            string type = Required(op, "type", 40);
            if (type == "upsert_application" || type == "set_applications_source")
                ApplyApplicationOperation(state, op, type, descriptions);
            else if (type == "add_event" || type == "update_event")
            {
                if (type == "add_event") CheckKeys(op, "type", "title", "date", "time", "repeat", "weekDay", "monthDay");
                else CheckKeys(op, "type", "eventId", "title", "date", "time", "repeat", "weekDay", "monthDay");
                KeyEvent item;
                if (type == "add_event") item = new KeyEvent { Id = NewId(), Title = Required(op, "title", 100), Date = "", Time = "09:00", Repeat = "none", WeekDay = -1, MonthDay = 0 };
                else
                {
                    string id = Required(op, "eventId", 160);
                    item = state.KeyEvents.FirstOrDefault(e => e.Id == id);
                    if (item == null) throw new ArgumentException("找不到关键事项：" + id);
                }
                item.Title = CheckedMilestoneTitle(Optional(op, "title", item.Title, 100));
                item.Date = Optional(op, "date", item.Date, 10);
                item.Time = Optional(op, "time", item.Time, 5);
                item.Repeat = Optional(op, "repeat", item.Repeat, 10);
                item.WeekDay = Integer(op, "weekDay", item.WeekDay);
                item.MonthDay = Integer(op, "monthDay", item.MonthDay);
                ValidateEvent(item);
                if (type == "add_event") state.KeyEvents.Add(item);
                if (item.Id == "legacy-milestone") { state.MilestoneTitle = item.Title; state.DepartureDate = item.Repeat == "none" ? item.Date : ""; }
                descriptions.Add((type == "add_event" ? "添加关键事项：" : "更新关键事项：") + item.Title + " · " + (item.Repeat == "none" ? item.Date : item.Repeat == "weekly" ? "每周 " + new[] { "日", "一", "二", "三", "四", "五", "六" }[item.WeekDay] : "每月 " + item.MonthDay + " 日") + " " + item.Time);
            }
            else if (type == "add_track")
            {
                CheckKeys(op, "type", "title", "nodes");
                List<string> nodeTitles = new List<string>();
                object raw;
                if (op.TryGetValue("nodes", out raw))
                {
                    object[] values = raw as object[];
                    if (values == null || values.Length > 200 || values.Any(v => !(v is string))) throw new ArgumentException("nodes 必须是最多包含 200 个名称的字符串数组。");
                    nodeTitles.AddRange(values.Cast<string>());
                }
                Track added = AddTrackIn(state, Required(op, "title", 500), nodeTitles);
                descriptions.Add("添加主线「" + added.Title + "」：" + (added.Nodes.Count == 0 ? "暂无线路节点" : String.Join(" → ", added.Nodes.Select(n => n.Title))));
            }
            else if (type == "rename_track")
            {
                CheckKeys(op, "type", "trackId", "title");
                Track track = FindTrack(state, Required(op, "trackId", 160));
                string oldTitle = track.Title;
                track.Title = CheckedTitle(Required(op, "title", 500), state.Tracks.Where(t => t.Id != track.Id).Select(t => t.Title));
                descriptions.Add("主线「" + oldTitle + "」改名为「" + track.Title + "」");
            }
            else if (type == "add_node" || type == "rename_node" || type == "rename_task")
            {
                if (type == "add_node") CheckKeys(op, "type", "trackId", "title");
                else if (type == "rename_node") CheckKeys(op, "type", "trackId", "nodeId", "title");
                else CheckKeys(op, "type", "trackId", "nodeId", "taskId", "title");
                Track track = FindTrack(state, Required(op, "trackId", 160));
                string title = Required(op, "title", 500);
                if (type == "add_node")
                {
                    TrackNode added = AddNodeIn(track, title);
                    descriptions.Add(track.Title + "：添加节点「" + added.Title + "」");
                }
                else
                {
                    TrackNode node = FindNode(track, Required(op, "nodeId", 160));
                    if (type == "rename_node")
                    {
                        string oldTitle = node.Title;
                        node.Title = CheckedTitle(title, track.Nodes.Where(n => n.Id != node.Id).Select(n => n.Title));
                        descriptions.Add(track.Title + "：节点「" + oldTitle + "」改名为「" + node.Title + "」");
                    }
                    else
                    {
                        NodeTask task = FindTask(node, Required(op, "taskId", 160));
                        string oldTitle = task.Title;
                        RenameTaskIn(state, track, node, task, title);
                        descriptions.Add(track.Title + " / " + node.Title + "：任务「" + oldTitle + "」改名为「" + task.Title + "」");
                    }
                }
            }
            else if (type == "add_todo")
            {
                CheckKeys(op, "type", "title", "date", "trackId", "nodeId", "nodeTaskId", "done");
                string title = Required(op, "title", 500);
                string date = DateValue(op, "date", Today(), false);
                string trackId = Optional(op, "trackId", null, 160);
                string nodeId = Optional(op, "nodeId", null, 160);
                string taskId = Optional(op, "nodeTaskId", null, 160);
                if (String.IsNullOrEmpty(trackId) != String.IsNullOrEmpty(nodeId)) throw new ArgumentException("关联待办需要同时提供 trackId 和 nodeId。");
                if (!String.IsNullOrEmpty(taskId) && String.IsNullOrEmpty(nodeId)) throw new ArgumentException("nodeTaskId 需要对应主线与节点。");
                NodeTask linked = null;
                if (!String.IsNullOrEmpty(trackId))
                {
                    Track track = FindTrack(state, trackId);
                    TrackNode node = FindNode(track, nodeId);
                    if (!String.IsNullOrEmpty(taskId)) linked = FindTask(node, taskId);
                    else
                    {
                        List<NodeTask> matches = node.Tasks.Where(t => t.Title == title).ToList();
                        if (matches.Count > 1) throw new ArgumentException("同名节点任务不唯一，请提供 nodeTaskId。");
                        if (matches.Count == 1) { linked = matches[0]; taskId = linked.Id; }
                    }
                }
                if (state.Todos.Any(t => t.Date == date && t.Title == title && t.TrackId == trackId && t.NodeId == nodeId)) throw new ArgumentException("当日已有相同待办。");
                Todo added = new Todo { Id = NewId(), Title = title, Date = date, TrackId = trackId, NodeId = nodeId, NodeTaskId = taskId, Done = Bool(op, "done", linked != null && linked.Done) };
                state.Todos.Add(added);
                if (linked != null) SetTodoDoneIn(state, added, added.Done);
                descriptions.Add("添加" + (added.Done ? "已完成" : "") + "待办（" + date + "）：" + title);
            }
            else if (type == "complete_todo")
            {
                CheckKeys(op, "type", "todoId", "done");
                string id = Required(op, "todoId", 160);
                Todo todo = state.Todos.FirstOrDefault(t => t.Id == id);
                if (todo == null) throw new ArgumentException("找不到待办：" + id);
                bool done = Bool(op, "done", true);
                SetTodoDoneIn(state, todo, done);
                descriptions.Add((done ? "完成待办：" : "重新打开待办：") + todo.Title);
            }
            else if (type == "add_task" || type == "complete_task" || type == "set_current_node" || type == "complete_node")
            {
                if (type == "add_task") CheckKeys(op, "type", "trackId", "nodeId", "title", "done");
                else if (type == "complete_task") CheckKeys(op, "type", "trackId", "nodeId", "taskId", "done");
                else CheckKeys(op, "type", "trackId", "nodeId");
                Track track = FindTrack(state, Required(op, "trackId", 160));
                TrackNode node = FindNode(track, Required(op, "nodeId", 160));
                if (type == "add_task")
                {
                    string title = CheckedTitle(Required(op, "title", 500), node.Tasks.Select(t => t.Title));
                    NodeTask added = new NodeTask { Id = NewId(), Title = title, Done = Bool(op, "done", false) };
                    node.Tasks.Add(added);
                    SetNodeTaskDoneIn(state, track, node, added, added.Done);
                    descriptions.Add(track.Title + " / " + node.Title + "：添加" + (added.Done ? "已完成" : "") + "任务「" + title + "」");
                }
                else if (type == "complete_task")
                {
                    NodeTask task = FindTask(node, Required(op, "taskId", 160));
                    bool done = Bool(op, "done", true);
                    SetNodeTaskDoneIn(state, track, node, task, done);
                    descriptions.Add(track.Title + " / " + node.Title + (done ? "：完成「" : "：重新打开「") + task.Title + "」");
                }
                else if (type == "set_current_node")
                {
                    foreach (TrackNode other in track.Nodes.Where(n => n.Status == "current")) other.Status = "pending";
                    node.Status = "current";
                    descriptions.Add(track.Title + "：当前节点 → " + node.Title);
                }
                else
                {
                    node.Status = "done";
                    foreach (NodeTask task in node.Tasks) SetNodeTaskDoneIn(state, track, node, task, true);
                    descriptions.Add(track.Title + "：完成节点「" + node.Title + "」及其子任务");
                }
            }
            else if (type == "add_note")
            {
                CheckKeys(op, "type", "text", "date", "color");
                string note = Required(op, "text", 20000);
                string color = Optional(op, "color", "#F3E9B0", 30);
                if (!ValidColor(color)) throw new ArgumentException("便签颜色应为 #RRGGBB。");
                state.Notes.Add(new Note { Id = NewId(), Date = DateValue(op, "date", Today(), false), Text = note, Color = color, Pinned = false });
                descriptions.Add("添加便利贴：" + (note.Length > 80 ? note.Substring(0, 80) + "…" : note));
            }
            else if (type == "add_departure")
            {
                CheckKeys(op, "type", "title", "dueDate");
                string title = Required(op, "title", 500);
                if (state.DepartureItems.Any(d => d.Title == title)) throw new ArgumentException("已有同名离境事项。");
                state.DepartureItems.Add(new DepartureItem { Id = NewId(), Title = title, DueDate = DateValue(op, "dueDate", "", true) });
                descriptions.Add("添加离境事项：" + title);
            }
            else if (type == "complete_departure")
            {
                CheckKeys(op, "type", "departureId", "done");
                string id = Required(op, "departureId", 160);
                DepartureItem item = state.DepartureItems.FirstOrDefault(d => d.Id == id);
                if (item == null) throw new ArgumentException("找不到离境事项：" + id);
                item.Done = Bool(op, "done", true);
                descriptions.Add((item.Done ? "完成离境事项：" : "重新打开离境事项：") + item.Title);
            }
            else if (type == "set_milestone")
            {
                CheckKeys(op, "type", "title", "date");
                state.MilestoneTitle = CheckedMilestoneTitle(Required(op, "title", 100));
                state.DepartureDate = DateValue(op, "date", state.DepartureDate, true);
                SyncLegacyMilestone(state);
                descriptions.Add("关键日期 → " + state.MilestoneTitle + (String.IsNullOrEmpty(state.DepartureDate) ? "（未设置日期）" : " · " + state.DepartureDate));
            }
            else if (type == "set_departure_date")
            {
                CheckKeys(op, "type", "date");
                if (!op.ContainsKey("date")) throw new ArgumentException("缺少 date。");
                state.DepartureDate = DateValue(op, "date", "", true);
                SyncLegacyMilestone(state);
                descriptions.Add(String.IsNullOrEmpty(state.DepartureDate) ? "清除关键日期" : "关键日期 → " + state.DepartureDate);
            }
            else throw new ArgumentException("不支持的操作：" + type);
        }

        private static Track FindTrack(AppState state, string id) { Track track = state.Tracks.FirstOrDefault(t => t.Id == id); if (track == null) throw new ArgumentException("找不到主线：" + id); return track; }
        private static TrackNode FindNode(Track track, string id) { TrackNode node = track.Nodes.FirstOrDefault(n => n.Id == id); if (node == null) throw new ArgumentException("主线「" + track.Title + "」内找不到节点：" + id); return node; }
        private static NodeTask FindTask(TrackNode node, string id) { NodeTask task = node.Tasks.FirstOrDefault(t => t.Id == id); if (task == null) throw new ArgumentException("节点「" + node.Title + "」内找不到任务：" + id); return task; }
        private static void CheckKeys(Dictionary<string, object> value, params string[] allowed)
        {
            foreach (string key in value.Keys) if (!allowed.Contains(key)) throw new ArgumentException("无法识别字段：" + key);
        }
        private static string Required(Dictionary<string, object> value, string key, int max)
        {
            string result = Optional(value, key, null, max);
            if (String.IsNullOrWhiteSpace(result)) throw new ArgumentException("缺少有效的 " + key + "。");
            return result;
        }
        private static string Optional(Dictionary<string, object> value, string key, string fallback, int max)
        {
            object raw;
            if (!value.TryGetValue(key, out raw)) return fallback;
            string text = raw as string;
            if (text == null || text.Length > max) throw new ArgumentException(key + " 必须是长度不超过 " + max + " 的字符串。");
            return text.Trim();
        }
        private static bool Bool(Dictionary<string, object> value, string key, bool fallback)
        {
            object raw;
            if (!value.TryGetValue(key, out raw)) return fallback;
            if (!(raw is bool)) throw new ArgumentException(key + " 必须是 true 或 false。");
            return (bool)raw;
        }
        private static int Integer(Dictionary<string, object> value, string key, int fallback)
        {
            object raw;
            if (!value.TryGetValue(key, out raw)) return fallback;
            if (!(raw is int)) throw new ArgumentException(key + " 必须是整数。");
            return (int)raw;
        }
        private static string DateValue(Dictionary<string, object> value, string key, string fallback, bool emptyAllowed)
        {
            string result = Optional(value, key, fallback, 10);
            if (!ValidDate(result, emptyAllowed)) throw new ArgumentException(key + " 必须是有效的 yyyy-MM-dd 日期" + (emptyAllowed ? "或空字符串。" : "。"));
            return result;
        }
        private static bool ValidDate(string value, bool emptyAllowed)
        {
            if (String.IsNullOrEmpty(value)) return emptyAllowed;
            DateTime date;
            return DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }
        private static bool ValidColor(string color) { return color != null && System.Text.RegularExpressions.Regex.IsMatch(color, "^#[0-9a-fA-F]{6}$"); }
        private static void EnsureId(HashSet<string> ids, string id)
        {
            if (String.IsNullOrWhiteSpace(id) || !ids.Add(id)) throw new ArgumentException("数据包含缺失或重复 ID。");
        }
        private static void ValidateState(AppState state)
        {
            if (state == null || state.SchemaVersion != 1 || state.Tracks == null || state.Todos == null || state.Notes == null || state.DepartureItems == null || state.History == null || state.AppliedUpdateIds == null) throw new ArgumentException("数据结构或版本无效。");
            if (!ValidDate(state.DepartureDate, true)) throw new ArgumentException("离境日期无效。");
            if (state.MilestoneTitle != null && (String.IsNullOrWhiteSpace(state.MilestoneTitle) || state.MilestoneTitle.Length > 100)) throw new ArgumentException("关键日期名称无效。");
            if (state.KeyEvents == null || state.Notebook == null) throw new ArgumentException("关键事项或笔记本结构无效。");
            if (!ValidDate(state.LastFunReminderDate, true)) throw new ArgumentException("提醒记录日期无效。");
            HashSet<string> ids = new HashSet<string>();
            foreach (KeyEvent item in state.KeyEvents) { ValidateEvent(item); EnsureId(ids, item.Id); }
            foreach (NotebookEntry entry in state.Notebook)
            {
                if (entry == null || entry.Text == null || !ValidDate(entry.Date, false) || String.IsNullOrWhiteSpace(entry.SourceNoteId) || String.IsNullOrWhiteSpace(entry.SavedAt)) throw new ArgumentException("笔记本数据无效。");
                EnsureId(ids, entry.Id);
            }
            foreach (Track track in state.Tracks)
            {
                if (track == null || String.IsNullOrWhiteSpace(track.Title) || track.Nodes == null) throw new ArgumentException("主线数据无效。");
                EnsureId(ids, track.Id);
                ValidateApplications(track, ids);
                if (track.Nodes.Count(n => n != null && n.Status == "current") > 1) throw new ArgumentException("每条主线最多有一个当前节点。");
                foreach (TrackNode node in track.Nodes)
                {
                    if (node == null || String.IsNullOrWhiteSpace(node.Title) || node.Tasks == null || !(node.Status == "pending" || node.Status == "current" || node.Status == "done")) throw new ArgumentException("节点数据无效。");
                    EnsureId(ids, node.Id);
                    foreach (NodeTask task in node.Tasks) { if (task == null || String.IsNullOrWhiteSpace(task.Title)) throw new ArgumentException("节点任务数据无效。"); EnsureId(ids, task.Id); }
                }
            }
            foreach (Todo todo in state.Todos)
            {
                if (todo == null || String.IsNullOrWhiteSpace(todo.Title) || !ValidDate(todo.Date, false)) throw new ArgumentException("待办数据无效。");
                EnsureId(ids, todo.Id);
                if (String.IsNullOrEmpty(todo.TrackId) != String.IsNullOrEmpty(todo.NodeId)) throw new ArgumentException("待办关联节点不完整。");
                if (!String.IsNullOrEmpty(todo.TrackId))
                {
                    TrackNode node = FindNode(FindTrack(state, todo.TrackId), todo.NodeId);
                    if (!String.IsNullOrEmpty(todo.NodeTaskId)) FindTask(node, todo.NodeTaskId);
                }
                else if (!String.IsNullOrEmpty(todo.NodeTaskId)) throw new ArgumentException("待办缺少节点关联。");
            }
            foreach (Note note in state.Notes) { if (note == null || note.Text == null || !ValidDate(note.Date, false)) throw new ArgumentException("便签数据无效。"); EnsureId(ids, note.Id); }
            foreach (DepartureItem item in state.DepartureItems) { if (item == null || String.IsNullOrWhiteSpace(item.Title) || !ValidDate(item.DueDate, true)) throw new ArgumentException("离境事项数据无效。"); EnsureId(ids, item.Id); }
            if (state.History.Any(h => h == null || h.Text == null || h.At == null)) throw new ArgumentException("历史数据无效。");
            if (state.AppliedUpdateIds.Any(String.IsNullOrWhiteSpace) || state.AppliedUpdateIds.Distinct().Count() != state.AppliedUpdateIds.Count) throw new ArgumentException("更新记录 ID 无效。");
        }

        public static AppState CreateDefault()
        {
            return new AppState
            {
                SchemaVersion = 1, MilestoneTitle = "关键日期", DepartureDate = "", AppliedUpdateIds = new List<string>(), History = new List<HistoryEntry>(), Todos = new List<Todo>(), Notes = new List<Note>(),
                Tracks = new List<Track>(),
                DepartureItems = new List<DepartureItem>(), KeyEvents = new List<KeyEvent>(), Notebook = new List<NotebookEntry>(), FunReminderEnabled = true
            };
        }
    }
}
