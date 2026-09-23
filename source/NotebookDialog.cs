using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace DayDesk
{
    public static class NotebookDialog
    {
        public static void Show(Window owner, DeskStore store)
        {
            Window window = UI.Dialog(owner, "笔记本 · 留住每天的想法", 700, 820);
            window.MinHeight = 360;
            Grid shell = new Grid { Margin = new Thickness(22) };
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            window.Content = shell;

            StackPanel heading = UI.Stack(
                UI.Text("笔记本", 25, UI.Ink, true),
                UI.Text("每天的零散想法，按日期慢慢积累。", 12, UI.Muted));
            heading.Margin = new Thickness(0, 0, 0, 16);
            shell.Children.Add(heading);

            StackPanel searchPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            searchPanel.Children.Add(UI.Text("搜索内容或日期", 12, UI.Muted));
            TextBox search = UI.Input();
            search.Margin = new Thickness(0, 6, 0, 6);
            searchPanel.Children.Add(search);
            TextBlock count = UI.Text("", 11, UI.Muted);
            searchPanel.Children.Add(count);
            Grid.SetRow(searchPanel, 1);
            shell.Children.Add(searchPanel);

            StackPanel entries = new StackPanel();
            ScrollViewer scroll = new ScrollViewer
            {
                Content = entries,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0, 0, 8, 0)
            };
            Grid.SetRow(scroll, 2);
            shell.Children.Add(scroll);

            Button close = UI.Button("完成", delegate { window.Close(); });
            close.HorizontalAlignment = HorizontalAlignment.Right;
            close.Margin = new Thickness(0, 12, 0, 0);
            Grid.SetRow(close, 3);
            shell.Children.Add(close);

            Action render = null;
            render = delegate
            {
                entries.Children.Clear();
                string query = search.Text.Trim();
                List<NotebookEntry> all = store.State.Notebook ?? new List<NotebookEntry>();
                List<NotebookEntry> filtered = all
                    .Where(entry => entry != null &&
                        (query.Length == 0 || (entry.Text ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                         (entry.Date ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
                    .OrderByDescending(entry => entry.Date, StringComparer.Ordinal)
                    .ThenByDescending(entry => entry.SavedAt, StringComparer.Ordinal)
                    .ToList();
                count.Text = query.Length == 0 ? all.Count + " 条已收录的想法" : "找到 " + filtered.Count + " 条 · 共 " + all.Count + " 条";

                if (filtered.Count == 0)
                {
                    StackPanel empty = UI.Stack(
                        UI.Text(query.Length == 0 ? "把值得留下的想法收进来。" : "没有找到匹配的笔记", 19, UI.Ink, true),
                        UI.Text(query.Length == 0 ? "回到工作台，点击「收工存档」，\n便利贴就会按日期收录在这里。" : "试试更短的关键词，或输入日期。", 13, UI.Muted));
                    empty.Margin = new Thickness(10, 32, 10, 16);
                    entries.Children.Add(empty);
                    return;
                }

                foreach (IGrouping<string, NotebookEntry> group in filtered.GroupBy(entry => entry.Date))
                {
                    TextBlock date = UI.Text(FormatDate(group.Key) + "  ·  " + group.Count() + " 条", 14, UI.Accent, true);
                    date.Margin = new Thickness(2, 14, 0, 10);
                    entries.Children.Add(date);
                    foreach (NotebookEntry entry in group)
                    {
                        NotebookEntry selected = entry;
                        StackPanel cardContent = new StackPanel();
                        TextBox body = UI.Input(entry.Text ?? "", true);
                        body.IsReadOnly = true;
                        body.IsReadOnlyCaretVisible = false;
                        body.Background = Brushes.Transparent;
                        body.BorderThickness = new Thickness(0);
                        body.Padding = new Thickness(0, 0, 0, 4);
                        body.MinHeight = 48;
                        body.MaxHeight = 260;
                        body.VerticalContentAlignment = VerticalAlignment.Top;
                        body.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
                        body.ToolTip = "可选择并复制文字；长笔记可在这里滚动阅读。";
                        cardContent.Children.Add(body);

                        WrapPanel actions = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
                        actions.Children.Add(UI.Button("编辑", delegate
                        {
                            if (Edit(window, store, selected.Id)) render();
                        }));
                        actions.Children.Add(UI.Button("删除", delegate
                        {
                            MessageBoxResult result = MessageBox.Show(window,
                                "删除这条已归档的想法？此操作不会删除工作台上的便利贴。",
                                "删除笔记", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
                            if (result != MessageBoxResult.Yes) return;
                            store.DeleteNotebookEntry(selected.Id);
                            render();
                        }));
                        cardContent.Children.Add(actions);
                        string color = !String.IsNullOrEmpty(entry.Color) && Regex.IsMatch(entry.Color, "^#[0-9a-fA-F]{6}$") ? entry.Color : "#FFF5D8";
                        Border card = UI.Card(cardContent, color, 16);
                        card.Margin = new Thickness(0, 0, 0, 10);
                        entries.Children.Add(card);
                    }
                }
            };
            search.TextChanged += delegate { render(); };
            render();
            window.ShowDialog();
        }

        private static string FormatDate(string value)
        {
            DateTime date;
            if (DateTime.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                return date.ToString("yyyy 年 M 月 d 日", CultureInfo.GetCultureInfo("zh-CN"));
            return String.IsNullOrWhiteSpace(value) ? "未注明日期" : value;
        }

        private static bool Edit(Window owner, DeskStore store, string entryId)
        {
            NotebookEntry entry = store.State.Notebook.FirstOrDefault(item => item.Id == entryId);
            if (entry == null) { UI.Toast(owner, "这条笔记已经不存在了。"); return false; }
            Window window = UI.Dialog(owner, "编辑笔记", 650, 620);
            window.MinHeight = 320;
            Grid shell = new Grid { Margin = new Thickness(22) };
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            TextBlock heading = UI.Text(FormatDate(entry.Date), 14, UI.Muted);
            heading.Margin = new Thickness(0, 0, 0, 12);
            shell.Children.Add(heading);
            TextBox input = UI.Input(entry.Text, true);
            input.MaxLength = 20000;
            input.VerticalContentAlignment = VerticalAlignment.Top;
            input.AcceptsTab = true;
            input.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            Grid.SetRow(input, 1);
            shell.Children.Add(input);
            bool saved = false;
            WrapPanel buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            buttons.Children.Add(UI.Button("取消", delegate { window.Close(); }));
            buttons.Children.Add(UI.Button("保存笔记", delegate
            {
                if (String.IsNullOrWhiteSpace(input.Text)) { UI.Toast(window, "请保留一些文字；如果不需要这条笔记，可以返回后删除。"); return; }
                store.UpdateNotebookEntry(entryId, input.Text);
                saved = true;
                window.Close();
            }, true));
            Grid.SetRow(buttons, 2);
            shell.Children.Add(buttons);
            window.Content = shell;
            window.Loaded += delegate { input.Focus(); input.CaretIndex = input.Text.Length; };
            window.ShowDialog();
            return saved;
        }
    }
}
