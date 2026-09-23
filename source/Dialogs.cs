using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DayDesk
{
    public static class Dialogs
    {
        private static void Fit(Window window, Window owner)
        {
            window.MaxHeight = SystemParameters.WorkArea.Height * .94;
            if (owner != null && owner.ActualHeight > 480)
                window.Height = Math.Min(window.Height, owner.ActualHeight * .9);
            window.Height = Math.Min(window.Height, window.MaxHeight);
            window.MinHeight = Math.Min(420, window.Height);
        }

        private static TextBlock Hint(string text)
        {
            TextBlock block = UI.Text(text, 12, UI.Muted);
            block.TextWrapping = TextWrapping.Wrap;
            block.Margin = new Thickness(0, 4, 0, 12);
            return block;
        }

        private static Button SmallButton(string text, Action action)
        {
            Button button = UI.Button(text, action);
            button.FontSize = 11;
            button.Padding = new Thickness(9, 5, 9, 5);
            button.Margin = new Thickness(0, 0, 6, 5);
            return button;
        }

        private static Border Section(UIElement child, string color)
        {
            Border card = UI.Card(child, color, 14);
            card.Margin = new Thickness(0, 0, 0, 10);
            return card;
        }

        private static ScrollViewer Scroll(UIElement child)
        {
            return new ScrollViewer
            {
                Content = child,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0, 0, 8, 0)
            };
        }

        private static bool DateValid(string text)
        {
            DateTime result;
            return String.IsNullOrWhiteSpace(text) || DateTime.TryParseExact(text.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
        }

        private static void TrySave(Window window, DeskStore store, Action mutation, Action refresh)
        {
            try
            {
                mutation();
                store.Save();
                refresh();
            }
            catch (Exception error) { UI.Toast(window, "保存失败：" + error.Message); }
        }

        private static void TryAction(Window window, Action mutation, Action refresh)
        {
            try { mutation(); refresh(); }
            catch (Exception error) { UI.Toast(window, "未能完成操作：" + error.Message); }
        }

        private static MenuItem MenuAction(string title, Action action)
        {
            MenuItem item = new MenuItem { Header = title };
            item.Click += delegate { action(); };
            return item;
        }

        private static Button MoreButton(params MenuItem[] items)
        {
            ContextMenu menu = new ContextMenu();
            foreach (MenuItem item in items) menu.Items.Add(item);
            Button button = null;
            button = SmallButton("⋯", delegate
            {
                menu.PlacementTarget = button;
                menu.IsOpen = true;
            });
            button.ContextMenu = menu;
            button.ToolTip = "改名或删除";
            button.MinWidth = 32;
            return button;
        }

        private static bool ConfirmDelete(Window owner, string details)
        {
            return MessageBox.Show(owner, details, "确认删除", MessageBoxButton.YesNo,
                MessageBoxImage.Question, MessageBoxResult.No) == MessageBoxResult.Yes;
        }

        public static void ShowTracks(Window owner, DeskStore store)
        {
            Window window = UI.Dialog(owner, "管理我的主线", 650, 830);
            Fit(window, owner);
            DockPanel shell = new DockPanel { Margin = new Thickness(22) };
            Button close = UI.Button("完成", delegate { window.Close(); });
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.Margin = new Thickness(0, 12, 0, 0);
            DockPanel.SetDock(close, Dock.Bottom);
            shell.Children.Add(close);
            StackPanel content = new StackPanel();
            shell.Children.Add(Scroll(content));
            window.Content = shell;
            content.Children.Add(UI.Text("按自己的节奏，建立主线", 23, UI.Ink, true));
            content.Children.Add(Hint("学英语、健身、论文……把重要的目标拆成一条可以逐步前进的路线。"));

            StackPanel addPanel = new StackPanel();
            addPanel.Children.Add(UI.Text("新主线", 15, UI.Ink, true));
            TextBox name = UI.Input();
            name.Margin = new Thickness(0, 9, 0, 10);
            name.MaxLength = 500;
            name.ToolTip = "主线名称，例如：学英语";
            addPanel.Children.Add(name);
            TextBox nodes = UI.Input("", true);
            nodes.Height = 105;
            nodes.Margin = new Thickness(0, 8, 0, 8);
            StackPanel nodeFields = new StackPanel();
            nodeFields.Children.Add(nodes);
            nodeFields.Children.Add(Hint("每行一个节点；可以留空，稍后再添加。例如：基础词汇、日常听力、口语练习。"));
            Expander nodeExpander = new Expander { Header = UI.Text("路线节点（可选）", 12, UI.Muted), Content = nodeFields, Margin = new Thickness(0, 0, 0, 12) };
            addPanel.Children.Add(nodeExpander);
            StackPanel list = new StackPanel();
            Action render = null;
            render = delegate
            {
                list.Children.Clear();
                if (store.State.Tracks.Count == 0)
                {
                    list.Children.Add(Hint("还没有主线。先写下一个你想持续推进的目标。"));
                    return;
                }
                TextBlock label = UI.Text("我的主线  ·  " + store.State.Tracks.Count, 15, UI.Ink, true);
                label.Margin = new Thickness(0, 8, 0, 12);
                list.Children.Add(label);
                for (int i = 0; i < store.State.Tracks.Count; i++)
                {
                    Track selected = store.State.Tracks[i];
                    int index = i;
                    StackPanel row = new StackPanel();
                    row.Children.Add(UI.Text(selected.Title, 16, UI.Ink, true));
                    TrackNode active = selected.Nodes.FirstOrDefault(x => x.Status == "current");
                    row.Children.Add(Hint(active == null ? "未设置当前节点  ·  " + selected.Nodes.Count + " 个节点" : "当前  ·  " + active.Title));
                    WrapPanel actions = new WrapPanel();
                    actions.Children.Add(SmallButton("进入路线", delegate { ShowTrack(window, store, selected); render(); }));
                    actions.Children.Add(SmallButton("改名", delegate
                    {
                        string title = UI.Prompt(window, "修改主线名称", "主线名称", selected.Title);
                        if (title != null) TryAction(window, delegate { store.RenameTrack(selected.Id, title); }, render);
                    }));
                    Button up = SmallButton("↑", delegate { TryAction(window, delegate { store.MoveTrack(selected.Id, -1); }, render); });
                    up.ToolTip = "主线上移";
                    up.IsEnabled = index > 0;
                    actions.Children.Add(up);
                    Button down = SmallButton("↓", delegate { TryAction(window, delegate { store.MoveTrack(selected.Id, 1); }, render); });
                    down.ToolTip = "主线下移";
                    down.IsEnabled = index < store.State.Tracks.Count - 1;
                    actions.Children.Add(down);
                    actions.Children.Add(SmallButton("删除", delegate
                    {
                        string detail = "删除主线「" + selected.Title + "」及其 " + selected.Nodes.Count + " 个节点、" + selected.Nodes.Sum(x => x.Tasks.Count) + " 项节点任务？\n\n每日待办保留，解除关联。";
                        if (ConfirmDelete(window, detail)) TryAction(window, delegate { store.DeleteTrack(selected.Id); }, render);
                    }));
                    row.Children.Add(actions);
                    list.Children.Add(Section(row, "#FFFFFF"));
                }
            };
            addPanel.Children.Add(UI.Button("＋ 添加主线", delegate
            {
                TryAction(window, delegate
                {
                    List<string> titles = nodes.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
                    store.AddTrack(name.Text, titles);
                    name.Text = "";
                    nodes.Text = "";
                    nodeExpander.IsExpanded = false;
                }, render);
            }, true));
            content.Children.Add(Section(addPanel, "#EAF4EF"));
            content.Children.Add(list);
            render();
            window.ShowDialog();
        }

        public static void ShowTrack(Window owner, DeskStore store, Track track)
        {
            string trackId = track.Id;
            Window window = UI.Dialog(owner, track.Title + " · 完整路线", 650, 850);
            Fit(window, owner);
            DockPanel shell = new DockPanel { Margin = new Thickness(22) };
            Button close = UI.Button("完成", delegate { window.Close(); });
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.Margin = new Thickness(0, 12, 0, 0);
            DockPanel.SetDock(close, Dock.Bottom);
            shell.Children.Add(close);
            StackPanel content = new StackPanel();
            shell.Children.Add(Scroll(content));
            window.Content = shell;
            HashSet<string> expandedNodes = new HashSet<string>();

            Action render = null;
            render = delegate
            {
                Track currentTrack = store.State.Tracks.FirstOrDefault(x => x.Id == trackId);
                if (currentTrack == null) { window.Close(); return; }
                content.Children.Clear();
                content.Children.Add(UI.Text(currentTrack.Title, 24, UI.Ink, true));
                content.Children.Add(Hint("每条主线只保留一个当前位置。已完成的节点和任务会一直留在这条路线里。"));
                TrackNode active = currentTrack.Nodes.FirstOrDefault(x => x.Status == "current");
                TextBlock currentLabel = UI.Text(active == null ? "还没有当前节点" : "现在进行到  ·  " + active.Title, 14, UI.Accent, true);
                content.Children.Add(Section(currentLabel, "#EAF4EF"));

                for (int i = 0; i < currentTrack.Nodes.Count; i++)
                {
                    TrackNode node = currentTrack.Nodes[i];
                    int nodeIndex = i;
                    bool isCurrent = node.Status == "current";
                    bool isDone = node.Status == "done";
                    if (node.Tasks == null) node.Tasks = new List<NodeTask>();
                    StackPanel header = new StackPanel();
                    TextBlock title = UI.Text((isDone ? "✓  " : isCurrent ? "●  " : "○  ") + node.Title, 15, isCurrent ? UI.Accent : UI.Ink, isCurrent);
                    title.TextWrapping = TextWrapping.Wrap;
                    header.Children.Add(title);
                    header.Children.Add(UI.Text((isDone ? "已完成" : isCurrent ? "当前节点" : "待开始") + (node.Tasks.Count == 0 ? "" : "  ·  " + node.Tasks.Count(x => x.Done) + "/" + node.Tasks.Count + " 项完成"), 11, UI.Muted));
                    StackPanel details = new StackPanel { Margin = new Thickness(6, 12, 0, 0) };
                    WrapPanel actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 9) };
                    if (!isCurrent)
                    {
                        actions.Children.Add(SmallButton("设为当前", delegate
                        {
                            TrySave(window, store, delegate
                            {
                                foreach (TrackNode other in currentTrack.Nodes)
                                    if (other.Status == "current") other.Status = "pending";
                                node.Status = "current";
                            }, render);
                        }));
                    }
                    if (!isDone)
                    {
                        actions.Children.Add(SmallButton("完成整个节点", delegate
                        {
                            TrySave(window, store, delegate
                            {
                                node.Status = "done";
                                foreach (NodeTask task in node.Tasks)
                                    store.SetNodeTaskDone(currentTrack, node, task, true);
                            }, render);
                        }));
                    }
                    Button up = SmallButton("↑", delegate
                    {
                        TrySave(window, store, delegate
                        {
                            currentTrack.Nodes.RemoveAt(nodeIndex);
                            currentTrack.Nodes.Insert(nodeIndex - 1, node);
                        }, render);
                    });
                    up.IsEnabled = nodeIndex > 0;
                    up.ToolTip = "节点上移";
                    actions.Children.Add(up);
                    Button down = SmallButton("↓", delegate
                    {
                        TrySave(window, store, delegate
                        {
                            currentTrack.Nodes.RemoveAt(nodeIndex);
                            currentTrack.Nodes.Insert(nodeIndex + 1, node);
                        }, render);
                    });
                    down.IsEnabled = nodeIndex < currentTrack.Nodes.Count - 1;
                    down.ToolTip = "节点下移";
                    actions.Children.Add(down);
                    actions.Children.Add(MoreButton(
                        MenuAction("修改节点名称", delegate
                        {
                            string text = UI.Prompt(window, "修改节点", "节点名称", node.Title);
                            if (text != null) TryAction(window, delegate { store.RenameNode(trackId, node.Id, text); }, render);
                        }),
                        MenuAction("删除节点", delegate
                        {
                            string detail = "删除节点「" + node.Title + "」及其 " + node.Tasks.Count + " 项任务？\n\n每日待办保留，解除关联。";
                            if (ConfirmDelete(window, detail)) TryAction(window, delegate { store.DeleteNode(trackId, node.Id); }, render);
                        })));
                    details.Children.Add(actions);
                    if (node.Tasks.Count == 0) details.Children.Add(Hint("这个节点还没有具体任务。"));
                    foreach (NodeTask task in node.Tasks)
                    {
                        NodeTask selectedTask = task;
                        TextBlock taskText = UI.Text(task.Title, 13, task.Done ? UI.Muted : UI.Ink);
                        taskText.TextWrapping = TextWrapping.Wrap;
                        if (task.Done) taskText.TextDecorations = TextDecorations.Strikethrough;
                        CheckBox check = new CheckBox
                        {
                            Content = taskText,
                            IsChecked = task.Done,
                            Margin = new Thickness(0, 0, 0, 10),
                            VerticalContentAlignment = VerticalAlignment.Center
                        };
                        check.Click += delegate
                        {
                            TrySave(window, store, delegate
                            {
                                store.SetNodeTaskDone(currentTrack, node, selectedTask, check.IsChecked == true);
                            }, render);
                        };
                        DockPanel taskRow = new DockPanel { LastChildFill = true };
                        Button taskMenu = MoreButton(
                            MenuAction("修改任务名称", delegate
                            {
                                string text = UI.Prompt(window, "修改任务", "任务名称", selectedTask.Title);
                                if (text != null) TryAction(window, delegate { store.RenameTask(trackId, node.Id, selectedTask.Id, text); }, render);
                            }),
                            MenuAction("删除任务", delegate
                            {
                                if (ConfirmDelete(window, "删除任务「" + selectedTask.Title + "」？\n\n每日待办保留，解除与这项任务的关联。"))
                                    TryAction(window, delegate { store.DeleteTask(trackId, node.Id, selectedTask.Id); }, render);
                            }));
                        taskMenu.Margin = new Thickness(8, 0, 0, 6);
                        DockPanel.SetDock(taskMenu, Dock.Right);
                        taskRow.Children.Add(taskMenu);
                        taskRow.Children.Add(check);
                        details.Children.Add(taskRow);
                    }
                    details.Children.Add(SmallButton("＋ 添加具体任务", delegate
                    {
                        string text = UI.Prompt(window, "添加节点任务", "写下这个阶段要完成的一件具体事情");
                        if (!String.IsNullOrWhiteSpace(text))
                            TrySave(window, store, delegate
                            {
                                if (node.Tasks.Any(x => x.Title == text.Trim())) throw new ArgumentException("这个节点已有同名任务。");
                                NodeTask task = new NodeTask { Id = DeskStore.NewId(), Title = text.Trim(), Done = false };
                                node.Tasks.Add(task);
                                store.SetNodeTaskDone(currentTrack, node, task, false);
                            }, render);
                    }));
                    if (!isDone) details.Children.Add(Hint("完成整个节点会同时勾选其全部任务。开始下一阶段时，点击对应节点的「设为当前」。"));
                    Expander expander = new Expander { Header = header, Content = details, IsExpanded = isCurrent || expandedNodes.Contains(node.Id) };
                    expander.Expanded += delegate { expandedNodes.Add(node.Id); };
                    expander.Collapsed += delegate { expandedNodes.Remove(node.Id); };
                    content.Children.Add(Section(expander, isCurrent ? "#F0F6F2" : "#FFFFFF"));
                }
                content.Children.Add(UI.Button("＋ 添加路线节点", delegate
                {
                    string text = UI.Prompt(window, "添加路线节点", "节点名称，例如：基础练习、阶段复盘");
                    if (!String.IsNullOrWhiteSpace(text))
                        TryAction(window, delegate { store.AddNode(trackId, text); }, render);
                }));
            };
            render();
            window.ShowDialog();
        }

        private static string EventSchedule(KeyEvent item)
        {
            string[] week = { "周日", "周一", "周二", "周三", "周四", "周五", "周六" };
            if (item.Repeat == "weekly") return "每" + week[Math.Max(0, Math.Min(6, item.WeekDay))] + "  ·  " + item.Time;
            if (item.Repeat == "monthly") return "每月 " + item.MonthDay + " 日  ·  " + item.Time;
            return "单次  ·  " + item.Date + " " + item.Time;
        }

        private static ComboBox Choice(params string[] values)
        {
            ComboBox combo = new ComboBox
            {
                FontSize = 14, Padding = new Thickness(9, 7, 9, 7), MinHeight = 36,
                Background = UI.Surface, Foreground = UI.Ink,
                Margin = new Thickness(0, 7, 0, 12)
            };
            foreach (string value in values) combo.Items.Add(value);
            return combo;
        }

        private static void ShowKeyEventEditor(Window owner, DeskStore store, KeyEvent existing)
        {
            bool creating = existing == null;
            Window window = UI.Dialog(owner, creating ? "添加关键日程" : "编辑关键日程", 540, 700);
            Fit(window, owner);
            DockPanel shell = new DockPanel { Margin = new Thickness(22) };
            StackPanel content = new StackPanel();
            WrapPanel footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            DockPanel.SetDock(footer, Dock.Bottom);
            shell.Children.Add(footer);
            shell.Children.Add(Scroll(content));
            window.Content = shell;
            content.Children.Add(UI.Text(creating ? "添加一个重要安排" : "调整这个安排", 22, UI.Ink, true));
            content.Children.Add(Hint("单次、每周或每月，设置一次即可。"));
            content.Children.Add(UI.Text("日程名称", 13, UI.Ink, true));
            TextBox title = UI.Input(creating ? "" : existing.Title);
            title.MaxLength = 100;
            title.Margin = new Thickness(0, 7, 0, 12);
            content.Children.Add(title);
            content.Children.Add(UI.Text("重复方式", 13, UI.Ink, true));
            ComboBox repeat = Choice("单次", "每周", "每月");
            repeat.SelectedIndex = creating || existing.Repeat == "none" ? 0 : existing.Repeat == "weekly" ? 1 : 2;
            content.Children.Add(repeat);

            StackPanel onceFields = new StackPanel();
            onceFields.Children.Add(UI.Text("日期（yyyy-MM-dd）", 13, UI.Ink, true));
            TextBox date = UI.Input(creating || String.IsNullOrWhiteSpace(existing.Date) ? DeskStore.Today() : existing.Date);
            date.Margin = new Thickness(0, 7, 0, 12);
            onceFields.Children.Add(date);
            content.Children.Add(onceFields);

            StackPanel weeklyFields = new StackPanel();
            weeklyFields.Children.Add(UI.Text("每周哪一天", 13, UI.Ink, true));
            ComboBox weekday = Choice("周一", "周二", "周三", "周四", "周五", "周六", "周日");
            int day = creating ? (int)DateTime.Today.DayOfWeek : existing.WeekDay;
            weekday.SelectedIndex = day == 0 ? 6 : day - 1;
            weeklyFields.Children.Add(weekday);
            content.Children.Add(weeklyFields);

            StackPanel monthlyFields = new StackPanel();
            monthlyFields.Children.Add(UI.Text("每月几号（1–31）", 13, UI.Ink, true));
            TextBox monthday = UI.Input((creating || existing.MonthDay < 1 ? DateTime.Today.Day : existing.MonthDay).ToString(CultureInfo.InvariantCulture));
            monthday.MaxLength = 2;
            monthday.Margin = new Thickness(0, 7, 0, 6);
            monthlyFields.Children.Add(monthday);
            monthlyFields.Children.Add(Hint("不足该日期的月份，使用当月最后一天。"));
            content.Children.Add(monthlyFields);

            content.Children.Add(UI.Text("时间（24 小时制，HH:mm）", 13, UI.Ink, true));
            TextBox time = UI.Input(creating || String.IsNullOrWhiteSpace(existing.Time) ? "09:00" : existing.Time);
            time.MaxLength = 5;
            time.Margin = new Thickness(0, 7, 0, 6);
            content.Children.Add(time);
            Action selectRepeat = delegate
            {
                onceFields.Visibility = repeat.SelectedIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
                weeklyFields.Visibility = repeat.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
                monthlyFields.Visibility = repeat.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
            };
            repeat.SelectionChanged += delegate { selectRepeat(); };
            selectRepeat();
            footer.Children.Add(UI.Button("取消", delegate { window.Close(); }));
            footer.Children.Add(UI.Button("保存日程", delegate
            {
                TryAction(window, delegate
                {
                    DateTime parsedTime;
                    if (!DateTime.TryParseExact(time.Text.Trim(), "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedTime))
                        throw new ArgumentException("请输入 24 小时制时间，例如 09:00 或 18:30。");
                    if (repeat.SelectedIndex == 0 && (String.IsNullOrWhiteSpace(date.Text) || !DateValid(date.Text)))
                        throw new ArgumentException("请输入有效日期，例如 2026-10-30。");
                    int monthDay = 1;
                    if (repeat.SelectedIndex == 2 && (!Int32.TryParse(monthday.Text.Trim(), out monthDay) || monthDay < 1 || monthDay > 31))
                        throw new ArgumentException("每月日期应为 1 至 31。");
                    KeyEvent item = new KeyEvent
                    {
                        Id = creating ? "" : existing.Id,
                        Title = title.Text.Trim(),
                        Date = repeat.SelectedIndex == 0 ? date.Text.Trim() : "",
                        Time = time.Text.Trim(),
                        Repeat = repeat.SelectedIndex == 0 ? "none" : repeat.SelectedIndex == 1 ? "weekly" : "monthly",
                        WeekDay = (weekday.SelectedIndex + 1) % 7,
                        MonthDay = monthDay
                    };
                    store.SaveKeyEvent(item);
                }, delegate { window.Close(); });
            }, true));
            window.Loaded += delegate { title.Focus(); };
            window.ShowDialog();
        }

        public static void ShowDeparture(Window owner, DeskStore store)
        {
            Window window = UI.Dialog(owner, "关键日程与准备清单", 650, 850);
            Fit(window, owner);
            DockPanel shell = new DockPanel { Margin = new Thickness(22) };
            Button close = UI.Button("完成", delegate { window.Close(); });
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.Margin = new Thickness(0, 12, 0, 0);
            DockPanel.SetDock(close, Dock.Bottom);
            shell.Children.Add(close);
            StackPanel content = new StackPanel();
            shell.Children.Add(Scroll(content));
            window.Content = shell;
            Action render = null;
            render = delegate
            {
                content.Children.Clear();
                content.Children.Add(UI.Text("关键日程与准备清单", 23, UI.Ink, true));
                content.Children.Add(Hint("一次答辩、每周运动、每月复盘。重复日程会自动计算下一次时间。"));
                Button addEvent = UI.Button("＋ 添加关键日程", delegate { ShowKeyEventEditor(window, store, null); render(); }, true);
                addEvent.Margin = new Thickness(0, 0, 0, 14);
                content.Children.Add(addEvent);
                DateTime now = DateTime.Now;
                foreach (var scheduled in store.State.KeyEvents.Select(x => new { Item = x, Next = DeskStore.NextOccurrence(x, now) }).OrderBy(x => x.Next ?? DateTime.MaxValue))
                {
                    KeyEvent selectedEvent = scheduled.Item;
                    StackPanel eventRow = new StackPanel();
                    eventRow.Children.Add(UI.Text(selectedEvent.Title, 16, UI.Ink, true));
                    eventRow.Children.Add(Hint(EventSchedule(selectedEvent)));
                    if (selectedEvent.Repeat != "none" && scheduled.Next.HasValue)
                        eventRow.Children.Add(Hint("下次  ·  " + scheduled.Next.Value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)));
                    else if (!scheduled.Next.HasValue) eventRow.Children.Add(Hint("这个单次日程的时间已过。"));
                    WrapPanel eventActions = new WrapPanel();
                    eventActions.Children.Add(SmallButton("编辑", delegate { ShowKeyEventEditor(window, store, selectedEvent); render(); }));
                    eventActions.Children.Add(SmallButton("删除", delegate
                    {
                        if (ConfirmDelete(window, "删除关键日程「" + selectedEvent.Title + "」？\n\n准备清单中的事项会保留。"))
                            TryAction(window, delegate { store.DeleteKeyEvent(selectedEvent.Id); }, render);
                    }));
                    eventRow.Children.Add(eventActions);
                    content.Children.Add(Section(eventRow, "#F1EEF8"));
                }
                if (store.State.KeyEvents.Count == 0) content.Children.Add(Hint("还没有关键日程。添加单次安排，或设置每周、每月重复。"));
                int finished = store.State.DepartureItems.Count(x => x.Done);
                TextBlock count = UI.Text("准备事项  ·  " + finished + " / " + store.State.DepartureItems.Count + " 已完成", 15, UI.Ink, true);
                count.Margin = new Thickness(0, 8, 0, 12);
                content.Children.Add(count);
                content.Children.Add(Hint("所有日程共用这份准备清单。"));
                foreach (DepartureItem item in store.State.DepartureItems.OrderBy(x => x.Done).ThenBy(x => String.IsNullOrWhiteSpace(x.DueDate) ? "9999-99-99" : x.DueDate).ToList())
                {
                    DepartureItem selected = item;
                    StackPanel entry = new StackPanel();
                    TextBlock title = UI.Text(item.Title, 14, item.Done ? UI.Muted : UI.Ink);
                    title.TextWrapping = TextWrapping.Wrap;
                    if (item.Done) title.TextDecorations = TextDecorations.Strikethrough;
                    CheckBox check = new CheckBox { Content = title, IsChecked = item.Done, VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 9) };
                    check.Click += delegate { TrySave(window, store, delegate { selected.Done = check.IsChecked == true; }, render); };
                    entry.Children.Add(check);
                    WrapPanel buttons = new WrapPanel();
                    buttons.Children.Add(SmallButton(String.IsNullOrWhiteSpace(item.DueDate) ? "设置截止日期" : "截止 " + item.DueDate, delegate
                    {
                        string value = UI.Prompt(window, "截止日期", "yyyy-MM-dd；留空表示不设置", selected.DueDate ?? "");
                        if (value == null) return;
                        if (!DateValid(value)) { UI.Toast(window, "请输入有效日期，例如 2026-10-30。"); return; }
                        TrySave(window, store, delegate { selected.DueDate = value.Trim(); }, render);
                    }));
                    buttons.Children.Add(SmallButton("改名", delegate
                    {
                        string text = UI.Prompt(window, "修改准备事项", "事项名称", selected.Title);
                        if (text == null) return;
                        TrySave(window, store, delegate
                        {
                            if (String.IsNullOrWhiteSpace(text) || text.Trim().Length > 500) throw new ArgumentException("请输入 1 至 500 字的事项名称。");
                            selected.Title = text.Trim();
                        }, render);
                    }));
                    buttons.Children.Add(SmallButton("删除", delegate
                    {
                        TrySave(window, store, delegate { store.State.DepartureItems.Remove(selected); }, render);
                    }));
                    entry.Children.Add(buttons);
                    content.Children.Add(Section(entry, "#FFFFFF"));
                }
                if (store.State.DepartureItems.Count == 0) content.Children.Add(Hint("先记下最重要的一件准备事项。"));
                content.Children.Add(UI.Button("＋ 添加准备事项", delegate
                {
                    string text = UI.Prompt(window, "添加准备事项", "需要提前处理什么？");
                    if (!String.IsNullOrWhiteSpace(text))
                        TrySave(window, store, delegate
                        {
                            store.State.DepartureItems.Add(new DepartureItem { Id = DeskStore.NewId(), Title = text.Trim(), DueDate = "", Done = false });
                        }, render);
                }));
            };
            render();
            window.ShowDialog();
        }

        public static void ShowUpdates(Window owner, DeskStore store, string initialJson = null)
        {
            Window window = UI.Dialog(owner, "用一句记录更新工作台", 690, 910);
            Fit(window, owner);
            DockPanel shell = new DockPanel { Margin = new Thickness(22) };
            StackPanel content = new StackPanel();
            WrapPanel footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            footer.Children.Add(UI.Button("关闭", delegate { window.Close(); }));
            DockPanel.SetDock(footer, Dock.Bottom);
            shell.Children.Add(footer);
            shell.Children.Add(Scroll(content));
            window.Content = shell;
            content.Children.Add(UI.Text("写下今天，路线自然前进", 23, UI.Ink, true));
            content.Children.Add(Hint("当前版本通过外部 AI 整理文字。本窗口读取它生成的更新 JSON，先预览，再应用到本地记录。"));

            StackPanel contextPanel = new StackPanel();
            contextPanel.Children.Add(UI.Text("1  把当前路线交给 AI", 15, UI.Ink, true));
            contextPanel.Children.Add(Hint("复制后，粘贴给 Codex 或其他 AI，再补上你今天的记录。里面包含节点 ID 和更新格式，AI 可以对应到具体任务。"));
            TextBlock copyStatus = Hint("");
            contextPanel.Children.Add(UI.Button("复制 AI 更新说明与当前数据", delegate
            {
                try { Clipboard.SetText(store.ExportContext()); copyStatus.Text = "已复制。接着在 AI 对话里补上今天做了什么。"; }
                catch (Exception error) { UI.Toast(window, "复制失败：" + error.Message); }
            }, true));
            contextPanel.Children.Add(copyStatus);
            content.Children.Add(Section(contextPanel, "#EAF4EF"));

            StackPanel importPanel = new StackPanel();
            importPanel.Children.Add(UI.Text("2  粘贴更新内容并预览", 15, UI.Ink, true));
            TextBox json = UI.Input(initialJson ?? "", true);
            json.Height = 180;
            json.MinHeight = 120;
            json.FontFamily = new FontFamily("Consolas");
            json.FontSize = 12;
            json.AcceptsReturn = true;
            json.AcceptsTab = true;
            json.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            json.TextWrapping = TextWrapping.Wrap;
            json.Margin = new Thickness(0, 12, 0, 9);
            importPanel.Children.Add(json);
            StackPanel previewContent = new StackPanel();
            TextBlock previewStatus = Hint("尚未预览。粘贴 AI 生成的 JSON 后，点击检查。 ");
            importPanel.Children.Add(previewStatus);
            ImportPreview preview = null;
            Button apply = UI.Button("应用这些更新", delegate
            {
                if (preview == null) return;
                try
                {
                    store.ApplyImport(preview);
                    window.Close();
                }
                catch (Exception error) { UI.Toast(window, "无法应用更新：" + error.Message); }
            }, true);
            apply.IsEnabled = false;
            Action makePreview = delegate
            {
                previewContent.Children.Clear();
                preview = null;
                apply.IsEnabled = false;
                try
                {
                    preview = store.PreviewImport(json.Text);
                    if (preview.Descriptions == null || preview.Descriptions.Count == 0)
                    {
                        previewStatus.Text = "没有可以应用的更新。";
                        return;
                    }
                    previewStatus.Text = "将应用 " + preview.Descriptions.Count + " 项更新：";
                    foreach (string description in preview.Descriptions)
                    {
                        TextBlock line = UI.Text("•  " + description, 13, UI.Ink);
                        line.TextWrapping = TextWrapping.Wrap;
                        line.Margin = new Thickness(0, 0, 0, 8);
                        previewContent.Children.Add(line);
                    }
                    apply.IsEnabled = true;
                }
                catch (Exception error) { previewStatus.Text = "未通过检查：" + error.Message; }
            };
            json.TextChanged += delegate
            {
                preview = null;
                apply.IsEnabled = false;
                previewContent.Children.Clear();
                previewStatus.Text = "内容已修改，请重新预览。";
            };
            WrapPanel importButtons = new WrapPanel();
            Button previewButton = UI.Button("检查并预览", makePreview);
            previewButton.Margin = new Thickness(0, 0, 8, 8);
            apply.Margin = new Thickness(0, 0, 0, 8);
            importButtons.Children.Add(previewButton);
            importButtons.Children.Add(apply);
            importPanel.Children.Add(importButtons);
            importPanel.Children.Add(previewContent);
            content.Children.Add(Section(importPanel, "#FFFFFF"));

            StackPanel filePanel = new StackPanel();
            filePanel.Children.Add(UI.Text("3  文件投递与撤销", 15, UI.Ink, true));
            filePanel.Children.Add(Hint("也可以让本机 AI 把更新 JSON 放进数据目录的 inbox 文件夹。工作台会读取文件并提示预览。"));
            TextBox directory = UI.Input(Path.Combine(store.DataDirectory, "inbox"));
            directory.IsReadOnly = true;
            directory.FontSize = 11;
            directory.Margin = new Thickness(0, 0, 0, 10);
            filePanel.Children.Add(directory);
            WrapPanel fileButtons = new WrapPanel();
            fileButtons.Children.Add(SmallButton("打开数据文件夹", delegate
            {
                try { Process.Start(new ProcessStartInfo(store.DataDirectory) { UseShellExecute = true }); }
                catch (Exception error) { UI.Toast(window, "无法打开文件夹：" + error.Message); }
            }));
            fileButtons.Children.Add(SmallButton("撤销最近一次更新", delegate
            {
                try
                {
                    if (store.Undo()) { UI.Toast(window, "已撤销最近一次更新。"); window.Close(); }
                    else UI.Toast(window, "还没有可以撤销的更新。");
                }
                catch (Exception error) { UI.Toast(window, "撤销失败：" + error.Message); }
            }));
            filePanel.Children.Add(fileButtons);
            content.Children.Add(Section(filePanel, "#F1EEF8"));
            if (!String.IsNullOrWhiteSpace(initialJson)) makePreview();
            window.ShowDialog();
        }
    }
}
