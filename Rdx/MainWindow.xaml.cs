using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using Microsoft.Web.WebView2.Core;using Microsoft.Web.WebView2.Wpf;
using Rdx.Services;

namespace Rdx;

public partial class MainWindow : Window
{
    private sealed class Tab
    {
        public Border Header = null!;
        public TextBlock TitleText = null!;
        public WebView2 View = null!;
    }

    private readonly List<Tab> _tabs = new();
    private Tab? _active;
    private CoreWebView2Environment? _env;

    private const string HomeUrl = "https://duckduckgo.com/";
    private const string SearchPrefix = "https://duckduckgo.com/?q=";

    private static readonly SolidColorBrush TabActiveBg = new(Color.FromRgb(0x2E, 0x25, 0x42));
    private static readonly SolidColorBrush TabIdleBg = new(Colors.Transparent);
    private static readonly SolidColorBrush TabActiveFg = new(Color.FromRgb(0xED, 0xED, 0xED));
    private static readonly SolidColorBrush TabIdleFg = new(Color.FromRgb(0x8A, 0x84, 0x96));
    private static readonly SolidColorBrush Accent = new(Color.FromRgb(0x7E, 0xCA, 0x9C));
    private static readonly SolidColorBrush Dim = new(Color.FromRgb(0x55, 0x50, 0x65));

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
        PreviewKeyDown += MainWindow_PreviewKeyDown;
        StateChanged += MainWindow_StateChanged;
    }

    // ---------- init ----------

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var userData = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "RdxBrowser", "WebView2");
            Directory.CreateDirectory(userData);
            _env = await CoreWebView2Environment.CreateAsync(null, userData);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "WebView2 não inicializou.\nInstale o WebView2 Runtime e tente de novo.\n\n" + ex.Message,
                "rdx", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        UpdateShieldUI();
        await CreateTabAsync(HomeUrl);
        Omnibox.Focus();
    }

    // ---------- abas ----------

    private async Task<Tab> CreateTabAsync(string? url)
    {
        var view = new WebView2 { Visibility = Visibility.Collapsed };
        WebHost.Children.Add(view);

        try
        {
            await view.EnsureCoreWebView2Async(_env);
        }
        catch (Exception ex)
        {
            WebHost.Children.Remove(view);
            MessageBox.Show("Falha ao criar aba: " + ex.Message, "rdx",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            throw;
        }

        var core = view.CoreWebView2;
        core.Settings.AreDefaultContextMenusEnabled = true;
        core.Settings.IsZoomControlEnabled = true;
        core.Settings.AreDevToolsEnabled = true;

        // bloqueador na camada de rede
        core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += Core_WebResourceRequested;
        core.NewWindowRequested += Core_NewWindowRequested;
        view.NavigationCompleted += View_NavigationCompleted;

        var tab = new Tab { View = view };
        tab.Header = BuildTabHeader(tab);
        tab.TitleText = (TextBlock)((StackPanel)tab.Header.Child).Children[0];

        view.CoreWebView2.DocumentTitleChanged += (_, _) =>
            Dispatcher.Invoke(() => UpdateTabTitle(tab));
        view.SourceChanged += (_, _) =>
            Dispatcher.Invoke(() =>
            {
                if (tab == _active)
                {
                    SetOmnibox(view.Source?.ToString() ?? "");
                    UpdateNavButtons();
                }
            });

        _tabs.Add(tab);
        TabsPanel.Children.Add(tab.Header);
        SelectTab(tab);

        if (!string.IsNullOrWhiteSpace(url))
            view.CoreWebView2.Navigate(url);
        else
            view.CoreWebView2.Navigate(HomeUrl);

        return tab;
    }

    private Border BuildTabHeader(Tab tab)
    {
        var title = new TextBlock
        {
            Text = "nova aba",
            FontSize = 12,
            MaxWidth = 150,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        var close = new Button
        {
            Content = "×",
            FontSize = 13,
            Width = 22,
            Height = 22,
            Margin = new Thickness(6, 0, 0, 0),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = TabIdleFg,
            Cursor = Cursors.Hand
        };
        close.Click += (_, _) => CloseTab(tab);

        var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        row.Children.Add(title);
        row.Children.Add(close);

        var border = new Border
        {
            Child = row,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 4, 6, 4),
            Margin = new Thickness(0, 0, 4, 0),
            Cursor = Cursors.Hand,
            Tag = tab
        };
        border.MouseLeftButtonUp += (_, _) => SelectTab(tab);
        // cliques nas abas precisam furar a caption customizada
        WindowChrome.SetIsHitTestVisibleInChrome(border, true);
        WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        return border;
    }

    private void SelectTab(Tab tab)
    {
        _active = tab;
        foreach (var t in _tabs)
        {
            bool on = t == tab;
            t.View.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            t.Header.Background = on ? TabActiveBg : TabIdleBg;
            ((TextBlock)((StackPanel)t.Header.Child).Children[0]).Foreground = on ? TabActiveFg : TabIdleFg;
        }
        SetOmnibox(tab.View.Source?.ToString() ?? "");
        UpdateTabTitle(tab);
        UpdateNavButtons();
        UpdateStatus();
    }

    private void CloseTab(Tab tab)
    {
        int idx = _tabs.IndexOf(tab);
        if (idx < 0) return;

        _tabs.Remove(tab);
        TabsPanel.Children.Remove(tab.Header);
        WebHost.Children.Remove(tab.View);
        try { tab.View.Dispose(); } catch { /* ignore */ }

        if (_tabs.Count == 0)
        {
            _ = CreateTabAsync(HomeUrl);
            return;
        }
        if (_active == tab)
            SelectTab(_tabs[Math.Min(idx, _tabs.Count - 1)]);
    }

    private void UpdateTabTitle(Tab tab)
    {
        string title = "";
        try { title = tab.View.CoreWebView2?.DocumentTitle ?? ""; } catch { }
        if (string.IsNullOrWhiteSpace(title))
        {
            try { title = tab.View.Source?.Host ?? "nova aba"; } catch { title = "nova aba"; }
        }
        if (title.Length > 24) title = title[..24] + "…";
        tab.TitleText.Text = title;
        if (tab == _active)
            Title = title == "nova aba" ? "rdx" : $"{title} — rdx";
    }

    // ---------- navegação ----------

    private static string ResolveInput(string raw)
    {
        var q = raw.Trim();
        if (q.Length == 0) return HomeUrl;
        if (q.Contains(' ') || !q.Contains('.'))
        {
            if (Uri.TryCreate(q, UriKind.Absolute, out var abs) &&
                (abs.Scheme == Uri.UriSchemeHttp || abs.Scheme == Uri.UriSchemeHttps))
                return abs.ToString();
            return SearchPrefix + Uri.EscapeDataString(q);
        }
        if (!q.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            q = "https://" + q;
        return Uri.TryCreate(q, UriKind.Absolute, out var u) ? u.ToString()
            : SearchPrefix + Uri.EscapeDataString(raw.Trim());
    }

    private void NavigateActive(string input)
    {
        if (_active == null) return;
        try { _active.View.CoreWebView2.Navigate(ResolveInput(input)); }
        catch { /* ignora url invalida */ }
    }

    private void UpdateNavButtons()
    {
        if (_active?.View.CoreWebView2 == null) return;
        try
        {
            BackButton.IsEnabled = _active.View.CanGoBack;
            ForwardButton.IsEnabled = _active.View.CanGoForward;
        }
        catch { }
    }

    private void SetOmnibox(string url)
    {
        Omnibox.Text = url;
        OmniboxHint.Visibility =
            string.IsNullOrEmpty(url) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateStatus()
    {
        StatusText.Text = AdBlocker.Enabled
            ? $"rdx • ⛨ {AdBlocker.BlockedCount} bloqueados"
            : "rdx • bloqueador desligado";
    }

    // ---------- eventos WebView2 ----------

    private void Core_WebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (AdBlocker.ShouldBlock(e.Request.Uri))
        {
            e.Response = _active?.View.CoreWebView2.Environment.CreateWebResourceResponse(
                null, 404, "Blocked", "Content-Type: text/plain");
            AdBlocker.Hit();
            Dispatcher.InvokeAsync(UpdateStatus);
        }
    }

    private async void Core_NewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true; // popup vira aba, sem frescura
        try { await CreateTabAsync(e.Uri); } catch { }
    }

    private async void View_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (sender is not WebView2 v) return;
        var tab = _tabs.FirstOrDefault(t => t.View == v);
        if (tab == null) return;

        // camada cosmetica
        if (AdBlocker.Enabled)
        {
            try { await v.ExecuteScriptAsync(AdBlocker.CosmeticJs); } catch { }
        }
        Dispatcher.Invoke(() =>
        {
            if (tab == _active)
            {
                SetOmnibox(v.Source?.ToString() ?? "");
                UpdateNavButtons();
            }
            UpdateTabTitle(tab);
        });
    }

    // ---------- UI events ----------

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        try { if (_active?.View.CanGoBack == true) _active.View.GoBack(); } catch { }
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        try { if (_active?.View.CanGoForward == true) _active.View.GoForward(); } catch { }
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        try { _active?.View.Reload(); } catch { }
    }

    private void HomeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_active == null) return;
        try { _active.View.CoreWebView2.Navigate(HomeUrl); } catch { }
    }

    private async void NewTabButton_Click(object sender, RoutedEventArgs e)
    {
        try { await CreateTabAsync(HomeUrl); } catch { }
        Omnibox.Focus();
        Omnibox.SelectAll();
    }

    private void ShieldButton_Click(object sender, RoutedEventArgs e)
    {
        AdBlocker.Enabled = !AdBlocker.Enabled;
        UpdateShieldUI();
        UpdateStatus();
        try { _active?.View.Reload(); } catch { } // reaplica na pagina atual
    }

    private void UpdateShieldUI()
    {
        ShieldButton.Foreground = AdBlocker.Enabled ? Accent : Dim;
        ShieldButton.ToolTip = AdBlocker.Enabled
            ? $"Bloqueador de anúncios: ligado ({AdBlocker.BlockedCount} bloqueados) — clique p/ desligar"
            : "Bloqueador de anúncios: desligado — clique p/ ligar";
    }

    // ---------- botoes da janela (title bar propria) ----------

    private void MinButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void MaxButton_Click(object sender, RoutedEventArgs e)
        => ToggleMaximize();

    private void CloseButton_Click(object sender, RoutedEventArgs e)
        => Close();

    private void ToggleMaximize()
        => WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        // evita conteudo colado nas bordas do monitor quando maximizado
        RootGrid.Margin = WindowState == WindowState.Maximized
            ? new Thickness(6)
            : new Thickness(0);
        MaxButton.Content = WindowState == WindowState.Maximized ? "❐" : "▢";
        MaxButton.ToolTip = WindowState == WindowState.Maximized ? "Restaurar" : "Maximizar";
    }

    private void Omnibox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            NavigateActive(Omnibox.Text);
            _active?.View.Focus();
        }
    }

    private void Omnibox_GotFocus(object sender, RoutedEventArgs e) => Omnibox.SelectAll();

    private async void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        if (ctrl && e.Key == Key.T)
        {
            e.Handled = true;
            try { await CreateTabAsync(HomeUrl); } catch { }
        }
        else if (ctrl && e.Key == Key.W)
        {
            e.Handled = true;
            if (_active != null) CloseTab(_active);
        }
        else if (ctrl && e.Key == Key.L)
        {
            e.Handled = true;
            Omnibox.Focus();
            Omnibox.SelectAll();
        }
        else if (e.Key == Key.F5)
        {
            e.Handled = true;
            try { _active?.View.Reload(); } catch { }
        }
    }
}
