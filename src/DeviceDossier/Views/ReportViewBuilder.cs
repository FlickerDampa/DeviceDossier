using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using DeviceDossier.Core;

namespace DeviceDossier.Views
{
    public static class ReportViewBuilder
    {
        public static void Populate(TabControl tabs, AuditReport report)
        {
            tabs.Items.Clear();
            tabs.Items.Add(new TabItem { Header = TabHeader("Summary", SectionState.Ok), Content = SummaryPage(report) });
            foreach (var spec in ReportLayout.Tabs)
            {
                var sections = spec.Sections.Select(id => report[id]).Where(s => s != null).ToList();
                if (sections.Count == 0) continue;
                var worst = ReportLayout.Worst(sections);
                object content = sections.Count == 1 && ReportLayout.ListSections.Contains(sections[0].Id) && IsSingleTable(sections[0])
                    ? ListPage(sections[0])
                    : DetailPage(sections);
                tabs.Items.Add(new TabItem { Header = TabHeader(spec.Header, worst), Content = content });
            }
            tabs.SelectedIndex = 0;
        }

        private static bool IsSingleTable(Section s)
        {
            return s.Blocks.Count == 1 && s.Blocks[0].Kind == BlockKind.Table;
        }

        private static object TabHeader(string text, SectionState state)
        {
            if (state == SectionState.Ok) return text;
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            panel.Children.Add(new TextBlock { Text = text });
            panel.Children.Add(Dyn(new System.Windows.Shapes.Ellipse
            {
                Width = 7,
                Height = 7,
                Margin = new Thickness(7, 1, 0, 0),
                VerticalAlignment = VerticalAlignment.Center
            }, System.Windows.Shapes.Shape.FillProperty, state == SectionState.Error ? "Danger" : state == SectionState.Warning ? "Warning" : "TextSubtle"));
            return panel;
        }

        private static Style S(string key)
        {
            return (Style)Application.Current.FindResource(key);
        }

        private static T Dyn<T>(T element, DependencyProperty property, string key) where T : FrameworkElement
        {
            element.SetResourceReference(property, key);
            return element;
        }

        private static UIElement Banner(Section s)
        {
            if (s.State == SectionState.Ok) return null;
            string back = s.State == SectionState.Error ? "DangerSoft" : s.State == SectionState.Skipped ? "Neutral" : "WarningSoft";
            string fore = s.State == SectionState.Error ? "Danger" : s.State == SectionState.Skipped ? "Info" : "Warning";
            string text = ReportLayout.StateText(s);
            return Dyn(new Border
            {
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 0, 0, 12),
                Child = Dyn(new TextBlock { Text = text, FontSize = 12.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap }, TextBlock.ForegroundProperty, fore)
            }, Border.BackgroundProperty, back);
        }

        private static UIElement DetailPage(List<Section> sections)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 0, 6, 10) };
            foreach (var s in sections)
            {
                stack.Children.Add(new TextBlock { Text = ReportLayout.Pretty(s.Title), Style = S("H2"), Margin = new Thickness(2, stack.Children.Count == 0 ? 0 : 18, 0, 10) });
                var banner = Banner(s);
                if (banner != null) stack.Children.Add(banner);
                foreach (var b in s.Blocks)
                {
                    if (!string.IsNullOrEmpty(b.Caption) && s.Id != "drives")
                        stack.Children.Add(new TextBlock { Text = b.Caption.ToUpperInvariant(), Style = S("Caps"), Margin = new Thickness(2, 6, 0, 8) });
                    if (b.Kind == BlockKind.KeyValue) stack.Children.Add(FieldCard(b.Fields.Select(f => new KeyValuePair<string, string>(f.Name, f.Text)).ToList(), null));
                    else if (b.Rows.Count <= 4 && b.Headers.Length >= 5) foreach (var row in RowCards(b)) stack.Children.Add(row);
                    else stack.Children.Add(MakeGrid(b, 360));
                }
            }
            return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stack };
        }

        private static IEnumerable<UIElement> RowCards(Block b)
        {
            for (int r = 0; r < b.Rows.Count; r++)
            {
                var row = b.Rows[r];
                var pairs = new List<KeyValuePair<string, string>>();
                for (int c = 1; c < b.Headers.Length; c++) pairs.Add(new KeyValuePair<string, string>(b.Headers[c], Fmt.Text(row[c])));
                string title = Fmt.Text(row[0]);
                if (b.Rows.Count > 1) title = (r + 1) + ".  " + title;
                yield return FieldCard(pairs, title);
            }
        }

        private static UIElement FieldCard(List<KeyValuePair<string, string>> fields, string title)
        {
            var grid = new Grid();
            bool wide = fields.Count >= 6;
            int pairsPerRow = wide ? 2 : 1;
            for (int i = 0; i < pairsPerRow; i++)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }
            int rows = (fields.Count + pairsPerRow - 1) / pairsPerRow;
            for (int i = 0; i < rows; i++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            for (int i = 0; i < fields.Count; i++)
            {
                int row = i / pairsPerRow, col = (i % pairsPerRow) * 2;
                var label = Dyn(new TextBlock
                {
                    Text = fields[i].Key,
                    FontSize = 12.5,
                    Margin = new Thickness(0, 6, 12, 6),
                    TextWrapping = TextWrapping.Wrap
                }, TextBlock.ForegroundProperty, "TextMuted");
                var value = new TextBox { Text = fields[i].Value, Style = S("ValueText"), Margin = new Thickness(0, 6, 18, 6) };
                System.Windows.Controls.Grid.SetRow(label, row);
                System.Windows.Controls.Grid.SetColumn(label, col);
                System.Windows.Controls.Grid.SetRow(value, row);
                System.Windows.Controls.Grid.SetColumn(value, col + 1);
                grid.Children.Add(label);
                grid.Children.Add(value);
            }

            var inner = new StackPanel();
            if (!string.IsNullOrEmpty(title))
                inner.Children.Add(Dyn(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap }, TextBlock.ForegroundProperty, "TextPrimary"));
            inner.Children.Add(grid);
            return new Border { Style = S("Card"), Margin = new Thickness(0, 0, 0, 12), Child = inner };
        }

        private static DataGrid MakeGrid(Block b, double maxHeight)
        {
            var rows = b.Rows.Select(r => r.Select(v => Fmt.Text(v)).ToArray()).ToList();
            var grid = new DataGrid { ItemsSource = rows, Margin = new Thickness(0, 0, 0, 12) };
            if (maxHeight > 0) grid.MaxHeight = maxHeight;
            for (int i = 0; i < b.Headers.Length; i++)
            {
                grid.Columns.Add(new DataGridTextColumn
                {
                    Header = b.Headers[i],
                    Binding = new Binding("[" + i + "]"),
                    MaxWidth = 520,
                    Width = new DataGridLength(1, DataGridLengthUnitType.Auto)
                });
            }
            return grid;
        }

        private static UIElement ListPage(Section s)
        {
            var block = s.Blocks[0];
            var dock = new DockPanel();

            var header = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            titles.Children.Add(new TextBlock { Text = ReportLayout.Pretty(s.Title), Style = S("H2") });
            var count = new TextBlock { Style = S("Muted"), FontSize = 12, Margin = new Thickness(0, 2, 0, 0) };
            titles.Children.Add(count);
            if (!string.IsNullOrEmpty(block.Caption))
                titles.Children.Add(new TextBlock { Text = block.Caption, Style = S("Muted"), FontSize = 12 });
            header.Children.Add(titles);

            var searchHost = new Grid { VerticalAlignment = VerticalAlignment.Center };
            var search = new TextBox { Style = S("Input") };
            var placeholder = Dyn(new TextBlock
            {
                Text = "Search " + ReportLayout.Pretty(s.Title).ToLowerInvariant() + "...",
                FontSize = 13,
                Margin = new Thickness(12, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsHitTestVisible = false
            }, TextBlock.ForegroundProperty, "TextSubtle");
            searchHost.Children.Add(search);
            searchHost.Children.Add(placeholder);
            System.Windows.Controls.Grid.SetColumn(searchHost, 1);
            header.Children.Add(searchHost);

            var copy = new Button { Content = "Copy all", Style = S("SecondaryButton"), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Controls.Grid.SetColumn(copy, 2);
            header.Children.Add(copy);

            DockPanel.SetDock(header, Dock.Top);
            dock.Children.Add(header);

            var banner = Banner(s);
            if (banner != null)
            {
                DockPanel.SetDock(banner, Dock.Top);
                dock.Children.Add(banner);
            }

            var grid = MakeGrid(block, 0);
            grid.Margin = new Thickness(0);
            dock.Children.Add(grid);

            var rows = (List<string[]>)grid.ItemsSource;
            var view = CollectionViewSource.GetDefaultView(rows);
            Action updateCount = () =>
            {
                int shown = view.Cast<object>().Count();
                count.Text = shown == rows.Count ? rows.Count + " items" : shown + " of " + rows.Count + " items";
            };
            updateCount();

            search.TextChanged += (sender, e) =>
            {
                string q = search.Text.Trim();
                placeholder.Visibility = q.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                view.Filter = q.Length == 0 ? (Predicate<object>)null : o => ((string[])o).Any(v => v != null && v.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
                updateCount();
            };

            copy.Click += (sender, e) =>
            {
                var sb = new StringBuilder();
                sb.AppendLine(string.Join("\t", block.Headers));
                foreach (string[] r in view) sb.AppendLine(string.Join("\t", r));
                try { Clipboard.SetText(sb.ToString()); copy.Content = "Copied"; }
                catch { copy.Content = "Copy failed"; }
            };
            return dock;
        }

        private static UIElement SummaryPage(AuditReport report)
        {
            var stack = new StackPanel { Margin = new Thickness(0, 0, 6, 10) };
            stack.Children.Add(new TextBlock { Text = report.DeviceName, Style = S("H2"), FontSize = 18 });
            stack.Children.Add(new TextBlock
            {
                Text = "Audited " + report.Generated.ToString("dd MMM yyyy, HH:mm", CultureInfo.InvariantCulture) + " · run as " + report.RunAsUser +
                       (report.RanAsAdmin ? " (administrator)" : " (limited mode)") + " · " + report.ToolName + " " + report.ToolVersion,
                Style = S("Muted"),
                Margin = new Thickness(0, 3, 0, 14)
            });

            var tiles = new UniformGrid { Columns = 3 };
            foreach (var t in ReportLayout.Summary(report)) tiles.Children.Add(Tile(t.Tag, t.Label, t.Value, t.Detail));
            stack.Children.Add(tiles);

            string patch = ReportLayout.LatestSecurityUpdate(report);
            if (!string.IsNullOrEmpty(patch))
            {
                stack.Children.Add(new TextBlock { Text = "LATEST SECURITY UPDATE", Style = S("Caps"), Margin = new Thickness(2, 8, 0, 6) });
                stack.Children.Add(new Border { Style = S("Card"), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 0, 14, 12), Child = new TextBox { Text = patch, Style = S("ValueText") } });
            }

            var issues = report.Sections.Where(s => s.State == SectionState.Warning || s.State == SectionState.Error).ToList();
            if (issues.Count > 0)
            {
                stack.Children.Add(new TextBlock { Text = "NEEDS ATTENTION", Style = S("Caps"), Margin = new Thickness(2, 8, 0, 6) });
                foreach (var s in issues)
                {
                    var banner = Banner(s) as Border;
                    if (banner == null) continue;
                    ((TextBlock)banner.Child).Text = ReportLayout.Pretty(s.Title) + ": " + ((TextBlock)banner.Child).Text;
                    banner.Margin = new Thickness(0, 0, 14, 8);
                    stack.Children.Add(banner);
                }
            }
            return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = stack };
        }

        private static UIElement Tile(string tag, string label, string value, string detail)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var badge = new Border { Style = S("Badge"), VerticalAlignment = VerticalAlignment.Top, Child = Dyn(new TextBlock { Text = tag, FontWeight = FontWeights.Bold, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, TextBlock.ForegroundProperty, "AccentText") };
            grid.Children.Add(badge);
            var texts = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
            texts.Children.Add(new TextBlock { Text = label.ToUpperInvariant(), Style = S("Caps") });
            texts.Children.Add(Dyn(new TextBlock { Text = value, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 3, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = value }, TextBlock.ForegroundProperty, "TextPrimary"));
            texts.Children.Add(new TextBlock { Text = detail ?? "", Style = S("Muted"), FontSize = 12, Margin = new Thickness(0, 2, 0, 0), TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = detail });
            System.Windows.Controls.Grid.SetColumn(texts, 1);
            grid.Children.Add(texts);
            return new Border { Style = S("Card"), Padding = new Thickness(16), Margin = new Thickness(0, 0, 14, 14), Child = grid };
        }
    }
}
