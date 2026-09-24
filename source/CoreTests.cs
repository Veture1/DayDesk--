using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using DayDesk;

public static class CoreTests
{
    private static int passed;
    private static string root;
    private static JavaScriptSerializer json = new JavaScriptSerializer();
    private static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    private static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (ArgumentException) { rejected = true; } catch (InvalidOperationException) { rejected = true; }
        Assert(rejected, message);
    }
    private static Track LegacyTrack(string id, string title, params string[] nodes)
    {
        Track track = new Track { Id = id, Title = title, Color = "#718E78", Nodes = new List<TrackNode>() };
        for (int i = 0; i < nodes.Length; i++) track.Nodes.Add(new TrackNode { Id = id + "-" + (i + 1), Title = nodes[i], Status = i == 0 ? "current" : "pending", Tasks = new List<NodeTask>() });
        return track;
    }
    private static DeskStore NewStore(string name)
    {
        DeskStore store = new DeskStore(Path.Combine(root, name));
        store.State.Tracks.AddRange(new[] {
            LegacyTrack("job", "求职", "简历优化", "岗位筛选", "投递与跟进", "面试准备", "面试与复盘"),
            LegacyTrack("github", "GitHub 项目", "项目规划", "核心功能", "测试与完善", "文档与发布"),
            LegacyTrack("leetcode", "LeetCode", "数组与哈希", "双指针", "滑动窗口", "贪心算法", "动态规划", "图论"),
            LegacyTrack("interview", "八股复习", "计算机网络", "操作系统", "数据库", "语言与框架", "模拟问答") });
        store.State.DepartureItems.Add(new DepartureItem { Id = "departure-documents", Title = "确认证件与行程", DueDate = "" });
        store.Save();
        return store;
    }
    private static string Update(string id, params object[] operations) { return json.Serialize(new { id = id, source = "CoreTests", operations = operations }); }
    private static void Test(string name, Action action) { action(); passed++; Console.WriteLine("PASS " + name); }
    public static int Main(string[] args)
    {
        root = Path.Combine(Path.GetTempPath(), "DayDesk-CoreTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            Test("Default data starts without fictional progress", delegate
            {
                DeskStore store = new DeskStore(Path.Combine(root, "default"));
                Assert(store.State.Todos.Count == 0 && store.State.Notes.Count == 0, "Empty daily workspace expected");
                Assert(store.State.Tracks.Count == 0, "New users choose their own tracks");
                Assert(store.State.MilestoneTitle == "关键日期" && store.State.DepartureItems.Count == 0, "Generic milestone with no personal checklist");
                Assert(File.Exists(Path.Combine(store.DataDirectory, "state.json")), "Initial state persisted");
            });
            Test("Whole update is atomic when a later operation is invalid", delegate
            {
                DeskStore store = NewStore("atomic");
                string before = File.ReadAllText(Path.Combine(store.DataDirectory, "state.json"));
                Reject(delegate { store.PreviewImport(Update("bad-1", new { type = "add_note", text = "Should not commit" }, new { type = "complete_task", trackId = "leetcode", nodeId = "leetcode-1", taskId = "missing" })); }, "Invalid operation rejected");
                Assert(store.State.Notes.Count == 0 && store.State.AppliedUpdateIds.Count == 0, "No partial memory changes");
                Assert(File.ReadAllText(Path.Combine(store.DataDirectory, "state.json")) == before, "No partial disk changes");
            });
            Test("Preview is read-only, update IDs persist and prevent reapplication", delegate
            {
                DeskStore store = NewStore("idempotency");
                string update = Update("unique-1", new { type = "add_note", text = "A short thought", date = "2026-09-22" });
                ImportPreview preview = store.PreviewImport(update);
                Assert(store.State.Notes.Count == 0 && preview.Result.Notes.Count == 1, "Preview only");
                store.ApplyImport(preview);
                Assert(store.State.Notes.Count == 1, "One application");
                store.Reload();
                Reject(delegate { store.PreviewImport(update); }, "Duplicate rejected after reload");
            });
            Test("Linked completion synchronizes daily and track tasks without advancing", delegate
            {
                DeskStore store = NewStore("sync");
                Track track = store.State.Tracks.Single(t => t.Id == "leetcode");
                TrackNode node = track.Nodes[0];
                NodeTask task = new NodeTask { Id = "problem-1", Title = "Two Sum" };
                node.Tasks.Add(task);
                Todo todo = new Todo { Id = "daily-1", Title = "Practice Two Sum", Date = DeskStore.Today(), TrackId = track.Id, NodeId = node.Id, NodeTaskId = task.Id };
                store.State.Todos.Add(todo);
                store.SetTodoDone(todo, true);
                Assert(task.Done && todo.Done && node.Status == "current" && track.Nodes[1].Status == "pending", "Sync without node advance");
                store.SetNodeTaskDone(track, node, task, false);
                Assert(!todo.Done && !task.Done, "Reverse sync");
                store.Save();
                store.ApplyImport(store.PreviewImport(Update("done-via-ai", new { type = "complete_task", trackId = track.Id, nodeId = node.Id, taskId = task.Id })));
                Assert(store.State.Todos.Single().Done, "AI sync");
            });
            Test("Node transition requires explicit actions and preserves completed history", delegate
            {
                DeskStore store = NewStore("transition");
                store.ApplyImport(store.PreviewImport(Update("advance", new { type = "complete_node", trackId = "leetcode", nodeId = "leetcode-1" }, new { type = "set_current_node", trackId = "leetcode", nodeId = "leetcode-2" })));
                Track track = store.State.Tracks.Single(t => t.Id == "leetcode");
                Assert(track.Nodes[0].Status == "done" && track.Nodes[1].Status == "current", "Explicit transition");
                store.ApplyImport(store.PreviewImport(Update("switch-only", new { type = "set_current_node", trackId = "leetcode", nodeId = "leetcode-4" })));
                track = store.State.Tracks.Single(t => t.Id == "leetcode");
                Assert(track.Nodes[0].Status == "done" && track.Nodes[1].Status == "pending" && track.Nodes[3].Status == "current", "Switch does not invent completion");
            });
            Test("A new completed task and linked daily record import together", delegate
            {
                DeskStore store = NewStore("new-completion");
                store.ApplyImport(store.PreviewImport(Update("new-done", new { type = "add_task", trackId = "leetcode", nodeId = "leetcode-4", title = "跳跃游戏", done = true }, new { type = "add_todo", trackId = "leetcode", nodeId = "leetcode-4", title = "跳跃游戏" })));
                Track track = store.State.Tracks.Single(t => t.Id == "leetcode");
                NodeTask task = track.Nodes[3].Tasks.Single();
                Todo todo = store.State.Todos.Single();
                Assert(task.Done && todo.Done && todo.NodeTaskId == task.Id, "New done task linked by exact title");
                Assert(track.Nodes[0].Status == "current" && track.Nodes[3].Status == "pending", "Completion never guesses current node");
            });
            Test("Completing a node synchronizes all children and reopening a child reopens node", delegate
            {
                DeskStore store = NewStore("node-sync");
                store.ApplyImport(store.PreviewImport(Update("setup", new { type = "add_task", trackId = "leetcode", nodeId = "leetcode-1", title = "Two Sum" }, new { type = "add_todo", trackId = "leetcode", nodeId = "leetcode-1", title = "Two Sum" }, new { type = "add_task", trackId = "leetcode", nodeId = "leetcode-1", title = "Group Anagrams" })));
                store.ApplyImport(store.PreviewImport(Update("whole-node", new { type = "complete_node", trackId = "leetcode", nodeId = "leetcode-1" })));
                Track track = store.State.Tracks.Single(t => t.Id == "leetcode");
                TrackNode node = track.Nodes[0];
                Assert(node.Status == "done" && node.Tasks.All(t => t.Done) && store.State.Todos.Single().Done, "Entire node and links completed");
                Assert(track.Nodes.All(n => n.Status != "current"), "No implicit next node");
                store.SetNodeTaskDone(track, node, node.Tasks[0], false);
                Assert(node.Status == "pending" && !store.State.Todos.Single().Done, "Reopened work no longer in completed node");
            });
            Test("Undo restores only unchanged imported state and protects later edits", delegate
            {
                DeskStore store = NewStore("undo");
                store.ApplyImport(store.PreviewImport(Update("note-1", new { type = "add_note", text = "Imported" })));
                Assert(store.CanUndo && store.Undo(), "Undo available");
                Assert(store.State.Notes.Count == 0 && store.State.AppliedUpdateIds.Count == 0, "Undo restored before state");
                store.ApplyImport(store.PreviewImport(Update("note-2", new { type = "add_note", text = "Imported again" })));
                store.State.Notes[0].Text = "Edited by user";
                Assert(!store.CanUndo && !store.Undo(), "Unsaved later edit protected");
                store.Save();
                Assert(!store.Undo() && store.State.Notes[0].Text == "Edited by user", "Saved later edit protected");
            });
            Test("Stale and externally modified previews cannot overwrite new edits", delegate
            {
                DeskStore store = NewStore("stale");
                ImportPreview preview = store.PreviewImport(Update("preview-1", new { type = "add_note", text = "Original preview" }));
                preview.Result.Notes[0].Text = "Injected mutation";
                store.ApplyImport(preview);
                Assert(store.State.Notes[0].Text == "Original preview", "Only validated preview used");
                ImportPreview stale = store.PreviewImport(Update("preview-2", new { type = "add_note", text = "Stale" }));
                store.State.Notes[0].Text = "New edit";
                store.Save();
                Reject(delegate { store.ApplyImport(stale); }, "Stale preview rejected");
                Assert(store.State.Notes.Count == 1 && store.State.Notes[0].Text == "New edit", "New edit preserved");
            });
            Test("Invalid dates, types, fields and node references are rejected", delegate
            {
                DeskStore store = NewStore("validation");
                Reject(delegate { store.PreviewImport(Update("v1", new { type = "add_todo", title = "Bad date", date = "2026-02-30" })); }, "Invalid date");
                Reject(delegate { store.PreviewImport(Update("v2", new { type = "complete_departure", departureId = "departure-documents", done = "true" })); }, "String bool");
                Reject(delegate { store.PreviewImport(Update("v3", new { type = "add_note", text = "X", unexpected = "field" })); }, "Unknown field");
                Reject(delegate { store.PreviewImport(Update("v4", new { type = "add_todo", title = "X", trackId = "job", nodeId = "leetcode-1" })); }, "Cross-track node");
                Reject(delegate { store.PreviewImport(Update("v5", new { type = "add_note", text = "X", color = "broken" })); }, "Color validation");
                Assert(store.State.AppliedUpdateIds.Count == 0, "Nothing committed");
            });
            Test("Corrupt primary recovers last backup and preserves damaged file", delegate
            {
                DeskStore store = NewStore("recovery");
                store.State.DepartureDate = "2026-11-01";
                store.Save();
                store.State.DepartureDate = "2026-11-02";
                store.Save();
                File.WriteAllText(Path.Combine(store.DataDirectory, "state.json"), "{broken");
                DeskStore recovered = new DeskStore(store.DataDirectory);
                Assert(recovered.State.DepartureDate == "2026-11-01", "Last backup recovered");
                Assert(!String.IsNullOrEmpty(recovered.RecoveryMessage), "Recovery disclosed");
                Assert(Directory.GetFiles(store.DataDirectory, "state.corrupt.*.json").Length == 1, "Bad file retained");
                new DeskStore(store.DataDirectory);
            });
            Test("Both invalid files stop loading without wiping user data", delegate
            {
                DeskStore store = NewStore("broken-both");
                store.Save();
                string primary = Path.Combine(store.DataDirectory, "state.json");
                string backup = Path.Combine(store.DataDirectory, "state.backup.json");
                File.WriteAllText(primary, "bad-primary");
                File.WriteAllText(backup, "bad-backup");
                bool failed = false;
                try { new DeskStore(store.DataDirectory); } catch (IOException) { failed = true; }
                Assert(failed && File.ReadAllText(primary) == "bad-primary" && File.ReadAllText(backup) == "bad-backup", "Preserved both files");
            });
            Test("Custom routes persist and rejected names do not mutate state", delegate
            {
                var s = new DeskStore(Path.Combine(root, "custom"));
                var a = s.AddTrack("学英语", new[] { "词汇", "听力" });
                var b = s.AddTrack("论文", new string[0]);
                s.RenameTrack(a.Id, "英语学习"); s.MoveTrack(b.Id, -1);
                Assert(s.State.Tracks[0].Id == b.Id, "Custom order");
                var added = s.AddNode(b.Id, "开题"); s.RenameNode(b.Id, added.Id, "选题");
                Assert(s.State.Tracks[0].Nodes[0].Status == "current", "First node starts current");
                string before = json.Serialize(s.State);
                Reject(delegate { s.RenameTrack(b.Id, "英语学习"); }, "Duplicate name rejected");
                Assert(json.Serialize(s.State) == before, "Validation is atomic");
                var loaded = new DeskStore(s.DataDirectory);
                Assert(loaded.State.Tracks[0].Title == "论文" && loaded.State.Tracks[1].Title == "英语学习", "Titles and order persist");
            });
            Test("Deleting routes and nodes preserves daily records without dangling links", delegate
            {
                var s = NewStore("safe-delete"); var tr = s.State.Tracks.First(); var n = tr.Nodes.First();
                s.State.Todos.Add(new Todo { Id="retained",Title="每日记录",Date=DeskStore.Today(),TrackId=tr.Id,NodeId=n.Id,Done=true }); s.Save();
                s.DeleteNode(tr.Id,n.Id);
                Assert(s.State.Todos.Single().Done && String.IsNullOrEmpty(s.State.Todos.Single().TrackId), "Node delete keeps daily record and clears links");
                Assert(!s.State.Tracks.First().Nodes.Any(x=>x.Status=="current"), "No fabricated advancement");
                s.DeleteTrack(tr.Id); new DeskStore(s.DataDirectory);
                Assert(s.State.Todos.Single().Title=="每日记录", "Track delete preserves record");
            });
            Test("Task rename follows exact links while preserving custom daily titles", delegate
            {
                var s=NewStore("task-edit");
                s.ApplyImport(s.PreviewImport(Update("te",new {type="add_task",trackId="leetcode",nodeId="leetcode-1",title="旧名称"},new {type="add_todo",trackId="leetcode",nodeId="leetcode-1",title="旧名称"})));
                var task=s.State.Tracks.Single(x=>x.Id=="leetcode").Nodes[0].Tasks[0];
                s.State.Todos.Add(new Todo{Id="custom-title",Title="保留这个标题",Date=DeskStore.Today(),TrackId="leetcode",NodeId="leetcode-1",NodeTaskId=task.Id});s.Save();
                s.RenameTask("leetcode","leetcode-1",task.Id,"新名称");
                Assert(s.State.Todos[0].Title=="新名称"&&s.State.Todos[1].Title=="保留这个标题","Rename linked auto-title only");
                s.DeleteTask("leetcode","leetcode-1",task.Id);
                Assert(s.State.Todos.Count==2&&s.State.Todos.All(x=>String.IsNullOrEmpty(x.NodeTaskId)&&x.TrackId=="leetcode"),"Unlink deleted task, keep node association");
            });
            Test("Weekly and monthly recurrence handles boundaries and leap years", delegate
            {
                var weekly=new KeyEvent{Id="w",Title="组会",Repeat="weekly",WeekDay=3,Time="10:00"};
                Assert(DeskStore.NextOccurrence(weekly,new DateTime(2026,9,23,9,0,0))==new DateTime(2026,9,23,10,0,0),"Same day upcoming");
                Assert(DeskStore.NextOccurrence(weekly,new DateTime(2026,9,23,10,0,1))==new DateTime(2026,9,30,10,0,0),"Following week after event");
                var monthly=new KeyEvent{Id="m",Title="月会",Repeat="monthly",MonthDay=31,Time="14:00"};
                Assert(DeskStore.NextOccurrence(monthly,new DateTime(2028,2,1))==new DateTime(2028,2,29,14,0,0),"Leap month clamps");
                Assert(DeskStore.NextOccurrence(monthly,new DateTime(2027,2,28,15,0,0))==new DateTime(2027,3,31,14,0,0),"No monthly drift after short month");
                var s=new DeskStore(Path.Combine(root,"events"));s.SaveKeyEvent(weekly);s.SaveKeyEvent(monthly);
                Assert(new DeskStore(s.DataDirectory).State.KeyEvents.Count==2,"Multiple events persist");
            });
            Test("AI creates and edits recurring events and custom routes transactionally",delegate
            {
                var s=new DeskStore(Path.Combine(root,"new-ai"));
                s.ApplyImport(s.PreviewImport(Update("new",new {type="add_track",title="阅读",nodes=new[]{"开始","深入"}},new {type="add_event",title="组会",repeat="weekly",weekDay=3,time="10:30"})));
                var ev=s.State.KeyEvents.Single();s.ApplyImport(s.PreviewImport(Update("edit",new {type="update_event",eventId=ev.Id,title="课题组会",weekDay=4})));
                Assert(s.State.KeyEvents.Single().WeekDay==4&&s.State.Tracks.Single().Nodes.Count==2,"AI custom setup works");
                string before=json.Serialize(s.State);
                Reject(delegate{s.PreviewImport(Update("invalid-recurrence",new {type="add_note",text="must not persist"},new {type="add_event",title="bad",repeat="monthly",monthDay=32}));},"Invalid recurrence rejected");
                Assert(before==json.Serialize(s.State),"No partial event update");
            });
            Test("Notebook snapshots preserve valuable edits and avoid duplicates",delegate
            {
                var s=new DeskStore(Path.Combine(root,"notebook"));
                s.State.Notes.Add(new Note{Id="idea",Date=DeskStore.Today(),Text="好想法",Color="#FFF2C9",Pinned=true});
                s.State.Notes.Add(new Note{Id="loose",Date=DeskStore.Today(),Text="另一条",Color="#FFF2C9"});s.Save();
                Assert(s.ArchiveNotes(DeskStore.Today())==2&&s.State.Notes.Count==1,"Move loose notes, retain pinned");
                var entry=s.State.Notebook.Single(x=>x.SourceNoteId=="idea");s.UpdateNotebookEntry(entry.Id,"笔记本内整理后的内容");
                Assert(s.ArchiveNotes(DeskStore.Today())==0&&s.State.Notebook.Single(x=>x.Id==entry.Id).Text=="笔记本内整理后的内容","Repeated archive preserves edited snapshot");
                s.State.Notes[0].Text="扩展后的好想法";s.Save();Assert(s.ArchiveNotes(DeskStore.Today())==1,"Changed source adds new version");
                Assert(new DeskStore(s.DataDirectory).State.Notebook.Count==3,"All versions persist");
            });
            Test("Legacy records migrate in memory without rewriting old data",delegate
            {
                var s=NewStore("legacy");s.State.DepartureDate="2026-12-01";s.State.Notes.Add(new Note{Id="old-note",Date=DeskStore.Today(),Text="珍贵想法",Color="#FFF2C9"});
                var raw=json.Deserialize<Dictionary<string,object>>(json.Serialize(s.State));
                foreach(string key in new[]{"MilestoneTitle","KeyEvents","Notebook","FunReminderEnabled","LastFunReminderDate"})raw.Remove(key);
                string original=json.Serialize(raw);File.WriteAllText(Path.Combine(s.DataDirectory,"state.json"),original);
                var loaded=new DeskStore(s.DataDirectory);
                Assert(loaded.State.Tracks.Count==4&&loaded.State.Notes.Single().Text=="珍贵想法","Preserve old tracks and ideas");
                Assert(loaded.State.KeyEvents.Single().Title=="离境"&&loaded.State.KeyEvents.Single().Date=="2026-12-01","Migrate old countdown");
                Assert(File.ReadAllText(Path.Combine(s.DataDirectory,"state.json"))==original,"Load does not rewrite");
                loaded.ApplyImport(loaded.PreviewImport(Update("legacy-date",new{type="set_departure_date",date="2026-12-05"})));
                Assert(loaded.State.KeyEvents.Single().Date=="2026-12-05","Old AI operations remain visible");
            });
            Test("Management saves are atomic on filesystem failure and fun setting persists",delegate
            {
                var s=new DeskStore(Path.Combine(root,"disk-failure"));var tr=s.AddTrack("原始主线",new string[0]);
                using(var locked=new FileStream(Path.Combine(s.DataDirectory,"state.json"),FileMode.Open,FileAccess.Read,FileShare.None)){
                    bool rejected=false;try{s.RenameTrack(tr.Id,"未能保存");}catch(IOException){rejected=true;}catch(UnauthorizedAccessException){rejected=true;}
                    Assert(rejected&&s.State.Tracks.Single().Title=="原始主线","Failed write keeps live data");
                }
                s.SetFunReminderEnabled(false);s.MarkFunReminded(DeskStore.Today());var loaded=new DeskStore(s.DataDirectory);
                Assert(loaded.State.FunReminderEnabled==false&&loaded.State.LastFunReminderDate==DeskStore.Today(),"Reminder preference and once-a-day date persist");
            });
            Test("Applications stay within their regional track and preserve routes",delegate {
                var s=new DeskStore(Path.Combine(root,"applications"));var a=s.AddTrack("地区甲",new[]{"持续投递"});var b=s.AddTrack("地区乙",new[]{"持续投递"});
                s.SaveApplication(a.Id,new JobApplication{Company="Example",Role="Engineer",Status="applied",AppliedDate="2026-09-23",Notes="已投"});
                var saved=s.State.Tracks.Single(x=>x.Id==a.Id).Applications.Single();
                s.SaveApplication(b.Id,new JobApplication{Company="Example",Role="Engineer",Status="planned"});
                s.SaveApplication(a.Id,new JobApplication{Id=saved.Id,Company="Example",Role="Engineer",Status="interview",AppliedDate=saved.AppliedDate,Notes="面试准备"});
                var loaded=new DeskStore(s.DataDirectory);
                Assert(loaded.State.Tracks[0].Applications.Single().Status=="interview"&&loaded.State.Tracks[1].Applications.Single().Status=="planned","Independent regional application status");
                Assert(loaded.State.Tracks.All(x=>x.Nodes.Single().Status=="current")&&loaded.State.Todos.Count==0,"Company updates do not modify route or create todos");
                s.DeleteApplication(a.Id,saved.Id);Assert(s.State.Tracks[0].Applications.Count==0&&s.State.Tracks[1].Applications.Count==1,"Delete affects only the selected record");
            });
            Test("Invalid application updates are atomic and reject unsafe links",delegate {
                var s=new DeskStore(Path.Combine(root,"bad-applications"));var a=s.AddTrack("求职",new string[0]);
                s.SaveApplication(a.Id,new JobApplication{Company="Example",Status="planned"});string before=s.ExportContext();
                Reject(()=>s.SaveApplication(a.Id,new JobApplication{Company="Example",Status="applied"}),"Duplicate company-role must reject");
                Reject(()=>s.SaveApplication(a.Id,new JobApplication{Company="Other",Status="guessed"}),"Unknown status must reject");
                Reject(()=>s.SaveApplication(a.Id,new JobApplication{Company="Other",Status="applied",AppliedDate="2026-02-31"}),"Bad date must reject");
                Reject(()=>s.SetApplicationsSource(a.Id,"file:///C:/test"),"Non-web source must reject");
                Assert(s.ExportContext()==before,"Every failed mutation preserves state");
            });
            Test("AI application upserts are repeatable, previewed and undoable",delegate {
                var s=new DeskStore(Path.Combine(root,"ai-applications"));var a=s.AddTrack("求职",new string[0]);
                var input=Update("company-1",new{type="upsert_application",trackId=a.Id,company="Example",role="Engineer",status="planned",notes="仅考虑过"});
                var preview=s.PreviewImport(input);Assert(s.State.Tracks[0].Applications.Count==0,"Preview is read-only");s.ApplyImport(preview);
                s.ApplyImport(s.PreviewImport(Update("company-2",new{type="upsert_application",trackId=a.Id,company="Example",role="Engineer",status="applied",appliedDate="2026-09-23"})));
                Assert(s.State.Tracks[0].Applications.Count==1&&s.State.Tracks[0].Applications[0].Status=="applied"&&s.State.Tracks[0].Applications[0].Notes=="仅考虑过","Upsert reuses company-role and preserves omitted notes");
                Assert(s.Undo()&&s.State.Tracks[0].Applications.Single().Status=="planned","Undo restores earlier status");
                s.SaveApplication(a.Id,new JobApplication{Company="Watching",Status="watch"});s.SaveApplication(a.Id,new JobApplication{Company="Waiting",Status="no_response"});
                Assert(DeskStore.ApplicationSummary(s.State.Tracks[0]).Contains("已投 1")&&DeskStore.ApplicationSummary(s.State.Tracks[0]).Contains("观望 1"),"Watch is not counted as submitted; no response remains submitted");
                string before=s.ExportContext();Reject(()=>s.PreviewImport(Update("bad-company",new{type="upsert_application",trackId=a.Id,company="Other",status="applied"},new{type="set_applications_source",trackId=a.Id,url="javascript:alert(1)"})),"Bad later operation rejects whole update");Assert(before==s.ExportContext(),"Failed preview preserves companies");
            });
            Console.WriteLine("Passed " + passed + " meaningful core tests.");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e.ToString()); return 1; }
        finally
        {
            string resolved = Path.GetFullPath(root);
            if (resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(resolved).StartsWith("DayDesk-CoreTests-")) Directory.Delete(resolved, true);
        }
    }
}
