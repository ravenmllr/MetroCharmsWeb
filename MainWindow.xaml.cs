using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using Microsoft.Win32;
using Windows.Devices.Geolocation;
using Windows.Devices.Power;

namespace MetroCharms;

public partial class MainWindow : Window
{
    #region Win32
    delegate IntPtr HookProc(int n, IntPtr w, IntPtr l);
    [DllImport("user32")] static extern IntPtr SetWindowsHookEx(int id, HookProc cb, IntPtr mod, uint tid);
    [DllImport("user32")] static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32")] static extern IntPtr CallNextHookEx(IntPtr h, int n, IntPtr w, IntPtr l);
    [DllImport("user32")] static extern int GetSystemMetrics(int i);
    [DllImport("user32")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32")] static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("kernel32")] static extern IntPtr GetModuleHandle(string n);
    [DllImport("kernel32")] static extern bool GetSystemPowerStatus(out PWR p);

    [StructLayout(LayoutKind.Sequential)]
    struct PWR { public byte AC, Flag, Pct, Sys; public int Life, Full; }
    #endregion

    // ---- konfigurasi web ----
    const string UrlWhatsApp = "https://web.whatsapp.com";
    const string UrlInstagram = "https://www.instagram.com";
    const string HomeUrl = UrlWhatsApp; // situs bawaan saat pertama kali dijalankan

    // Lebar panel per situs (dalam DIP) -> ubah dua angka ini sesuai selera
    const double WidthWhatsApp = 800;
    const double WidthInstagram = 800;

    static readonly string DataDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MetroCharms");
    // Folder profil WebView2: cookie, localStorage, IndexedDB, cache -> login tetap tersimpan
    static readonly string WebDataPath = Path.Combine(DataDir, "WebView2");
    static readonly string LastUrlPath = Path.Combine(DataDir, "lasturl.txt");

    // ---- state ----
    IntPtr _hook; HookProc _hookProc;
    bool _visible, _clean, _pinned;
    int _scrPx, _panelLeftPx; double _scrW;
    DoubleAnimation _cur;
    string _currentHome = HomeUrl;
    CoreWebView2Environment _env;
    int _batMin = -1, _batN; bool _lastChg;

    readonly DispatcherTimer _tick = new(DispatcherPriority.Normal) { Interval = TimeSpan.FromSeconds(1) };

    static SolidColorBrush Br(byte r, byte g, byte b) { var x = new SolidColorBrush(Color.FromRgb(r, g, b)); x.Freeze(); return x; }
    static readonly SolidColorBrush Off = Br(0x3A, 0x3A, 0x3A), Dark = Br(0x2B, 0x2B, 0x2B);
    SolidColorBrush _accent = Br(0x1B, 0xA1, 0xE2); // diganti dengan accent Windows di RefreshAccent()

    public MainWindow()
    {
        InitializeComponent();
        _scrPx = GetSystemMetrics(0);
        _scrW = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight; Top = 0; Left = _scrW;

        try { if (File.Exists(LastUrlPath)) { var u = File.ReadAllText(LastUrlPath).Trim(); if (u == UrlWhatsApp || u == UrlInstagram) _currentHome = u; } } catch { }
        RefreshAccent(); ApplyWidth();

        _tick.Tick += (_, _) => Update();
        Deactivated += (_, _) => { if (!_pinned) HidePanel(); };
        Loaded += async (_, _) => await InitWebAsync();

        // Low-level mouse hook: event-driven, 0% CPU saat idle (tanpa polling)
        _hookProc = OnMouse;
        _hook = SetWindowsHookEx(14, _hookProc, GetModuleHandle(null), 0);

        // Pre-warm: tampilkan sekali di luar layar agar WebView2 terinisialisasi (Loaded terpicu),
        // lalu disembunyikan lagi di InitWebAsync. Hotspot jadi tetap instan.
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = -20000; ShowActivated = false;
        Show();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var h = new WindowInteropHelper(this).Handle; // sembunyikan dari Alt+Tab
        SetWindowLong(h, -20, GetWindowLong(h, -20) | 0x80);
    }

    public void Cleanup()
    {
        if (_clean) return; _clean = true;
        if (_hook != IntPtr.Zero) UnhookWindowsHookEx(_hook);
        try { WebWa.Dispose(); } catch { }
        try { WebIg.Dispose(); } catch { }
    }

    // ================= WEBVIEW2 (dua view: WhatsApp & Instagram) =================
    WebView2 ViewFor(string url) => url == UrlInstagram ? WebIg : WebWa;
    WebView2 Active => ViewFor(_currentHome);

    async Task InitWebAsync()
    {
        try
        {
            Directory.CreateDirectory(WebDataPath);
            // Satu environment & folder data persisten dipakai bersama -> login tetap tersimpan
            _env = await CoreWebView2Environment.CreateAsync(null, WebDataPath);

            // WhatsApp selalu dimuat (tetap hidup di latar). Instagram dimuat saat pertama dipilih.
            await EnsureSite(UrlWhatsApp);
            if (_currentHome == UrlInstagram) await EnsureSite(UrlInstagram);
            ShowOnly();
        }
        catch (Exception ex)
        {
            // Biasanya: WebView2 Runtime belum terpasang
            MessageBox.Show("WebView2 gagal dimuat:\n" + ex.Message +
                            "\n\nPastikan Microsoft Edge WebView2 Runtime (Evergreen) terpasang.",
                            "MetroCharms", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            if (!_visible) Hide(); // selesai pre-warm
        }
    }

    // Pastikan view situs ini sudah diinisialisasi & dimuat (hanya sekali; tidak pernah reload saat pindah tab)
    async Task EnsureSite(string url)
    {
        var v = ViewFor(url);
        v.Visibility = Visibility.Visible; // kontrol harus terlihat agar bisa diinisialisasi
        if (v.CoreWebView2 != null) return;

        await v.EnsureCoreWebView2Async(_env);
        v.DefaultBackgroundColor = System.Drawing.Color.Black;
        var cw = v.CoreWebView2;
        cw.Settings.AreDevToolsEnabled = false;
        cw.Settings.AreDefaultContextMenusEnabled = true;
        cw.Settings.IsStatusBarEnabled = false;
        cw.Settings.IsZoomControlEnabled = false;

        // Link target=_blank / window.open -> buka di browser bawaan, bukan jendela WebView2 baru
        cw.NewWindowRequested += (_, a) =>
        {
            a.Handled = true;
            try { Process.Start(new ProcessStartInfo(a.Uri) { UseShellExecute = true }); } catch { }
        };

        cw.Navigate(url);
    }

    void ShowOnly()
    {
        WebWa.Visibility = _currentHome == UrlWhatsApp ? Visibility.Visible : Visibility.Collapsed;
        WebIg.Visibility = _currentHome == UrlInstagram ? Visibility.Visible : Visibility.Collapsed;
    }

    // WhatsApp tetap berjalan (suara/notifikasi jalan). Instagram dibekukan bila tidak sedang
    // terlihat: panel tersembunyi ATAU sedang di tab WhatsApp. Dibangunkan lagi saat dibutuhkan.
    async Task UpdateSuspend()
    {
        try
        {
            var cw = WebIg.CoreWebView2;
            if (cw == null) return;
            bool Sleep() => _currentHome != UrlInstagram || (!_visible && !_pinned);

            if (!Sleep()) { if (cw.IsSuspended) cw.Resume(); return; }
            if (cw.IsSuspended) return;
            await cw.TrySuspendAsync();
            if (!Sleep() && cw.IsSuspended) cw.Resume(); // keadaan berubah saat suspend berjalan
        }
        catch { }
    }

    async void Nav_Click(object s, RoutedEventArgs e)
    {
        string url = (string)((Button)s).Tag;
        if (url == _currentHome) return;
        _currentHome = url;
        SetNav(); ApplyWidth();
        try { Directory.CreateDirectory(DataDir); File.WriteAllText(LastUrlPath, _currentHome); } catch { }
        try { await EnsureSite(url); } catch { }
        ShowOnly();
        _ = UpdateSuspend();
        try { Active.Focus(); } catch { }
    }

    // Atur lebar panel sesuai situs aktif; area klik-luar ikut dihitung ulang
    void ApplyWidth()
    {
        double w = _currentHome == UrlInstagram ? WidthInstagram : WidthWhatsApp;
        Width = w;
        _panelLeftPx = _scrPx - (int)(w * _scrPx / _scrW);
        if (_visible) Left = _scrW - w; // tetap menempel di tepi kanan
    }

    void SetNav()
    {
        WaBtn.Background = _currentHome == UrlWhatsApp ? _accent : Off;
        IgBtn.Background = _currentHome == UrlInstagram ? _accent : Off;
    }

    void Reload_Click(object s, RoutedEventArgs e) => Active.CoreWebView2?.Reload();

    void Pin_Click(object s, RoutedEventArgs e)
    {
        _pinned = !_pinned;
        PinBtn.Background = _pinned ? _accent : Dark;
        PinIco.Text = _pinned ? "\uE77A" : "\uE718";
        _ = UpdateSuspend();
    }

    void Exit_Click(object s, RoutedEventArgs e) { Cleanup(); Application.Current.Shutdown(); }

    // ================= HOTSPOT & ANIMASI =================
    IntPtr OnMouse(int n, IntPtr w, IntPtr l)
    {
        if (n >= 0)
        {
            int msg = (int)w, x = Marshal.ReadInt32(l);
            if (!_visible)
            {
                // Hover pojok kanan atas -> buka
                if (msg == 0x200 /*WM_MOUSEMOVE*/ && x >= _scrPx - 4 && Marshal.ReadInt32(l, 4) <= 4)
                {
                    _visible = true;
                    Dispatcher.BeginInvoke(new Action(ShowPanel));
                }
            }
            // Klik kiri/kanan/tengah di luar panel -> tutup (kecuali di-pin)
            else if (!_pinned && (msg == 0x201 || msg == 0x204 || msg == 0x207) && x < _panelLeftPx)
                Dispatcher.BeginInvoke(new Action(HidePanel));
        }
        return CallNextHookEx(_hook, n, w, l);
    }

    void ShowPanel()
    {
        _visible = true;
        _cur = null; BeginAnimation(LeftProperty, null);
        Left = _scrW - Width;
        RefreshAccent(); // ikuti perubahan warna accent Windows
        _ = UpdateSuspend(); // bangunkan Instagram bila sedang aktif
        Show(); Activate(); SetForegroundWindow(new WindowInteropHelper(this).Handle);
        try { Active.Focus(); } catch { }

        Dispatcher.BeginInvoke(new Action(() => { Refresh(); _tick.Start(); }), DispatcherPriority.Background);
    }

    void HidePanel()
    {
        if (!_visible) return;
        _visible = false; _tick.Stop();
        var a = new DoubleAnimation(_scrW, TimeSpan.FromMilliseconds(140))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        _cur = a;
        a.Completed += (_, _) =>
        {
            if (_cur != a) return; // panel dibuka lagi saat animasi berjalan
            BeginAnimation(LeftProperty, null); Hide();
            _ = UpdateSuspend();
        };
        BeginAnimation(LeftProperty, a);
    }

    void Window_KeyDown(object s, KeyEventArgs e) { if (e.Key == Key.Escape && !_pinned) HidePanel(); }

    // ================= JAM & TANGGAL =================
    void Refresh() { Update(); RefreshWeather(); }

    void Update()
    {
        var now = DateTime.Now;
        Clock.Text = now.ToString("HH:mm"); DateTxt.Text = now.ToString("dddd, d MMMM");
        UpdateBattery();
    }

    // ================= BATERAI =================
    void UpdateBattery()
    {
        if (!(GetSystemPowerStatus(out var p) && p.Flag != 128 && p.Pct != 255))
        {
            BatLine.Visibility = Visibility.Collapsed; // PC desktop / tanpa baterai
            return;
        }
        BatLine.Visibility = Visibility.Visible;

        bool chg = (p.Flag & 8) != 0;
        if (chg != _lastChg) { _lastChg = chg; _batMin = -1; _batN = 0; } // status berubah -> hitung ulang
        if (_batN++ % 10 == 0) RefreshBatTime();                           // hitung ulang tiap ~10 detik

        string t;
        if (chg)
            t = _batMin >= 0 ? $"{_batMin / 60}h {_batMin % 60:00}m to full" : "Charging";
        else if (p.AC == 1)
            t = "Plugged in";
        else
        {
            int mins = p.Life > 0 ? p.Life / 60 : _batMin; // detik -> menit; fallback ke hitungan sendiri
            t = mins >= 0 ? $"{mins / 60}h {mins % 60:00}m left" : "Calculating...";
        }

        BatIco.Text = chg ? "\uE83E" : p.Pct >= 95 ? "\uE83F" : ((char)(0xE850 + p.Pct / 10)).ToString();
        BatTxt.Text = $"{p.Pct}% · {t}";
    }

    async void RefreshBatTime() => _batMin = await Task.Run(CalcBatMinutes);

    // Estimasi dari kapasitas (mWh) dan laju charge/discharge (mW); -1 = tidak diketahui
    static int CalcBatMinutes()
    {
        try
        {
            var r = Battery.AggregateBattery.GetReport();
            if (r.RemainingCapacityInMilliwattHours is not int rem ||
                r.FullChargeCapacityInMilliwattHours is not int full ||
                r.ChargeRateInMilliwatts is not int rate || rate == 0) return -1;
            double hrs = rate > 0 ? (full - rem) / (double)rate : rem / (double)(-rate);
            return hrs < 0 || hrs > 48 ? -1 : (int)Math.Round(hrs * 60);
        }
        catch { return -1; }
    }

    // ================= ACCENT WINDOWS =================
    static Color WinAccent()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (k?.GetValue("AccentColor") is int v) // format ABGR
                return Color.FromRgb((byte)v, (byte)(v >> 8), (byte)(v >> 16));
        }
        catch { }
        var g = SystemParameters.WindowGlassColor;
        return Color.FromRgb(g.R, g.G, g.B);
    }

    void RefreshAccent()
    {
        var b = new SolidColorBrush(WinAccent()); b.Freeze();
        _accent = b;
        Resources["AccentBrush"] = b; // border hover di semua tombol
        SetNav();
        PinBtn.Background = _pinned ? _accent : Dark;
    }

    // ================= CUACA (Open-Meteo) =================
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };
    DateTime _wxTime = DateTime.MinValue;
    double? _lat, _lon; // lokasi otomatis, di-cache selama aplikasi berjalan

    string _city;

    async Task ResolveCity()
    {
        try
        {
            string u = FormattableString.Invariant(
                $"https://api.bigdatacloud.net/data/reverse-geocode-client?latitude={_lat.Value}&longitude={_lon.Value}&localityLanguage=en");
            using var d = JsonDocument.Parse(await Http.GetStringAsync(u));
            foreach (var k in new[] { "city", "locality", "principalSubdivision" })
                if (d.RootElement.TryGetProperty(k, out var v) && v.GetString() is { Length: > 0 } s) { _city = s; return; }
        }
        catch { }
    }

    async Task<bool> Locate() // Windows Location Service
    {
        try
        {
            if (await Geolocator.RequestAccessAsync() != GeolocationAccessStatus.Allowed) return false;
            var g = new Geolocator { DesiredAccuracyInMeters = 5000 };
            var pos = await g.GetGeopositionAsync(TimeSpan.FromHours(1), TimeSpan.FromSeconds(10));
            _lat = pos.Coordinate.Point.Position.Latitude;
            _lon = pos.Coordinate.Point.Position.Longitude;
            return true;
        }
        catch { return false; }
    }

    static (string Icon, string Text) Wx(int c, bool day) => c switch
    {
        0 => (day ? "\u2600" : "\u263E", day ? "Clear" : "Clear night"),
        1 => ("\u26C5", "Mostly clear"),
        2 => ("\u26C5", "Partly cloudy"),
        3 => ("\u2601", "Overcast"),
        45 or 48 => ("\U0001F32B", "Fog"),
        >= 51 and <= 57 => ("\U0001F326", "Drizzle"),
        >= 61 and <= 67 => ("\U0001F327", "Rain"),
        (>= 71 and <= 77) or 85 or 86 => ("\u2744", "Snow"),
        >= 80 and <= 82 => ("\U0001F327", "Showers"),
        >= 95 => ("\u26A1", "Thunderstorm"),
        _ => ("\u2601", "--")
    };

    async void RefreshWeather()
    {
        if (DateTime.Now - _wxTime < TimeSpan.FromMinutes(15)) return; // cache 15 menit
        _wxTime = DateTime.Now;
        try
        {
            if (_lat == null && !await Locate()) { _wxTime = DateTime.MinValue; WxDesc.Text = "Location off"; return; }
            string url = FormattableString.Invariant(
                $"https://api.open-meteo.com/v1/forecast?latitude={_lat.Value}&longitude={_lon.Value}&current=temperature_2m,weather_code,is_day&timezone=auto");
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            var c = doc.RootElement.GetProperty("current");
            var (icon, text) = Wx(c.GetProperty("weather_code").GetInt32(), c.GetProperty("is_day").GetInt32() == 1);
            WxIco.Text = icon;
            WxTemp.Text = $"{c.GetProperty("temperature_2m").GetDouble():0}°C";
            WxDesc.Text = text;
            if (_city == null) await ResolveCity();
            WxCity.Text = _city ?? "";
        }
        catch { _wxTime = DateTime.MinValue; WxDesc.Text = "Offline"; }
    }
}
