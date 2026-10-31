using System.Runtime.InteropServices;
using Windows.Management.Deployment;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace SystemMenu;

// The notification history (roadmap 8.22): every notification Windows shows, kept in
// %LOCALAPPDATA%\launcher\notifications (NoticeLog.cs) and searched on the launcher's
// Notifications page (`omawin launcher notifications`). A verification code is said when it comes
// (`omawin notifications code` copies it); a link opens from the page.
//
// Windows only lets an app read other apps' notifications (UserNotificationListener) when it has
// package identity and you've said yes once. The menu app gets identity from a sparse package
// (identity/AppxManifest.xml), registered for this account by `omawin notifications identity on`,
// no admin. Until omawin's releases are signed, Windows registers an unsigned manifest only with
// Developer Mode on (open-questions/notification-identity.md); without identity the page says so.
public static class NotificationHistory
{
    static string Dir => Path.Combine(MenuModel.StateDir, "notifications");
    static readonly object gate = new();
    static HashSet<string>? seen;
    static string? latestCode;
    // Why notifications aren't being read (null while they are).
    public static string? Why { get; private set; } = "not started";

    // Where they come from: Windows' listener. Anything that hands Notices in can stand in.
    public static INotificationFeed Feed { get; set; } = new ListenerFeed();

    // From the UI thread (Windows asks for access from it), once the menu app is up.
    public static async void Start()
    {
        try
        {
            NoticeLog.Trim(Dir);
            Why = await Feed.Start(Arrived);
        }
        catch (Exception ex) { App.Log(ex); Why = ex.Message; }
    }

    static void Arrived(Notice n)
    {
        lock (gate)
        {
            // What Windows still shows at start was saved last time: each notification once.
            seen ??= [.. NoticeLog.Load(Dir, months: 2).Select(x => x.Key)];
            if (!seen.Add(n.Key)) return;
            try { NoticeLog.Append(Dir, n); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { App.Log(ex); }
            if (NoticeLog.Code(n.Text) is { } code && DateTimeOffset.Now - n.When < TimeSpan.FromMinutes(5))
            {
                latestCode = code;
                Snip.Say($"Code {code} from {n.App}: omawin notifications code copies it");
            }
        }
    }

    public static List<Notice> Recent() => NoticeLog.Load(Dir);

    // --- omawin notifications ----------------------------------------------------------------

    public static async Task<SettingCommands.Answer> Answer(string[] args)
    {
        switch (args.FirstOrDefault()?.ToLowerInvariant() ?? "status")
        {
            case "status":
                int count = Recent().Count;
                return new(0, (Why == null ? "Reading Windows' notifications" : "Not reading notifications: " + Why)
                    + $". {count} in the history (Win+Space, notifications); identity {(Identity.Has ? "on" : Identity.Registered() ? "registered (restart the menu app)" : "off")}.");
            case "code":
                var code = latestCode ?? Recent().Select(n => NoticeLog.Code(n.Text)).FirstOrDefault(c => c != null);
                if (code == null) return new(1, "No verification code in the notifications.");
                ClipboardHistory.SetText(code);
                return new(0, $"Code {code} copied.");
            case "identity":
                var what = args.Length > 1 ? args[1].ToLowerInvariant() : "on";
                return what switch
                {
                    "on" => await Identity.Register(),
                    "off" => await Identity.Unregister(),
                    _ => new(2, "omawin notifications identity [on|off]"),
                };
            default:
                return new(2, "omawin notifications [status|code|identity on|identity off]");
        }
    }

    // --- Windows' listener -------------------------------------------------------------------

    sealed class ListenerFeed : INotificationFeed
    {
        Timer? poll;

        public async Task<string?> Start(Action<Notice> arrived)
        {
            if (!Identity.Has)
                return Identity.Registered() ? "omawin's identity is registered; restart the menu app (omawin restart menu)"
                    : "Windows lets an app read notifications only with package identity: omawin notifications identity on";
            var listener = UserNotificationListener.Current;
            // Windows asks you once; after that the answer is in Settings › Privacy › Notifications.
            if (await listener.RequestAccessAsync() != UserNotificationListenerAccessStatus.Allowed)
                return "you haven't let omawin read notifications (Settings › Privacy & security › Notifications)";
            async Task ReadAll()
            {
                foreach (var n in await listener.GetNotificationsAsync(NotificationKinds.Toast))
                    if (From(n) is { } notice) arrived(notice);
            }
            await ReadAll();
            try
            {
                listener.NotificationChanged += (sender, e) =>
                {
                    if (e.ChangeKind != UserNotificationChangedKind.Added) return;
                    try { if (sender.GetNotification(e.UserNotificationId) is { } n && From(n) is { } notice) arrived(notice); }
                    catch (Exception ex) { App.Log(ex); }
                };
            }
            catch (Exception ex) when (ex is COMException or InvalidCastException or NotImplementedException)
            {
                // Some Windows builds refuse the change event to a desktop app with identity
                // (0x80070490); then the list is read again every 10 seconds, each one kept once.
                App.Log(ex);
                poll = new Timer(_ => ReadAll().ContinueWith(t => { if (t.Exception != null) App.Log(t.Exception); }), null, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(10));
            }
            return null;
        }

        static Notice? From(UserNotification n)
        {
            try
            {
                string app;
                try { app = n.AppInfo.DisplayInfo.DisplayName; }
                catch (Exception ex) when (ex is COMException or ArgumentException) { app = "Windows"; }
                var binding = n.Notification.Visual.GetBinding(KnownNotificationBindings.ToastGeneric);
                var lines = binding?.GetTextElements().Select(t => t.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList() ?? [];
                if (lines.Count == 0) return null;
                return new Notice(n.CreationTime, app, lines[0], string.Join("\n", lines.Skip(1)), n.Id);
            }
            catch (COMException) { return null; }
        }
    }

    // --- package identity --------------------------------------------------------------------

    public static class Identity
    {
        const string Name = "omawin.Shell", Publisher = "CN=omawin";
        static string Folder => Path.Combine(MenuModel.StateDir, "identity");

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        static extern int GetCurrentPackageFullName(ref uint length, char[]? name);

        // This process started with identity (the package registered, app.manifest's <msix>).
        public static bool Has
        {
            get
            {
                uint length = 0;
                return GetCurrentPackageFullName(ref length, null) != 15700; // APPMODEL_ERROR_NO_PACKAGE
            }
        }

        static Windows.ApplicationModel.Package? Package()
        {
            try { return new PackageManager().FindPackagesForUser("", Name, Publisher).FirstOrDefault(); }
            catch (Exception ex) when (ex is COMException or UnauthorizedAccessException) { return null; }
        }

        public static bool Registered() => Package() != null;

        // Developer Mode (Settings › System › For developers; the catalog's developer.dev-mode):
        // Windows registers a manifest that isn't signed only with it on.
        static bool DeveloperMode()
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock");
            return key?.GetValue("AllowDevelopmentWithoutDevLicense") is int on && on == 1;
        }

        public static async Task<SettingCommands.Answer> Register()
        {
            if (!DeveloperMode())
                return new(1, "omawin's identity is an unsigned package until releases are signed, and Windows registers those only with Developer Mode on "
                    + "(omawin setting set developer.dev-mode on, or Settings › System › For developers). Nothing changed.");
            string source = Path.Combine(MenuModel.Repo, "shell", "menu-app", "identity", "AppxManifest.xml");
            if (!File.Exists(source)) return new(1, "The identity manifest isn't in the checkout: " + source);
            // Its own copy, so a pull that changes the repo's never changes a registered package.
            Directory.CreateDirectory(Path.Combine(Folder, "Assets"));
            string manifest = Path.Combine(Folder, "AppxManifest.xml");
            File.Copy(source, manifest, overwrite: true);
            Logo(Path.Combine(Folder, "Assets", "StoreLogo.png"), 50);
            Logo(Path.Combine(Folder, "Assets", "Square150x150Logo.png"), 150);
            Logo(Path.Combine(Folder, "Assets", "Square44x44Logo.png"), 44);
            var options = new RegisterPackageOptions
            {
                DeveloperMode = true,
                // The menu app's folder: SystemMenu.exe runs from there, with this identity.
                ExternalLocationUri = new Uri(AppContext.BaseDirectory),
                ForceUpdateFromAnyVersion = true,
            };
            var result = await new PackageManager().RegisterPackageByUriAsync(new Uri(manifest), options);
            if (result.ExtendedErrorCode != null)
                return new(1, $"Windows didn't register omawin's identity: {result.ErrorText} (0x{result.ExtendedErrorCode.HResult:X8})");
            return new(0, "omawin has an identity for this account. Restart the menu app (omawin restart menu): Windows then asks once whether omawin may read notifications.");
        }

        public static async Task<SettingCommands.Answer> Unregister()
        {
            if (Package() is not { } package) return new(0, "omawin has no identity registered.");
            var result = await new PackageManager().RemovePackageAsync(package.Id.FullName);
            if (result.ExtendedErrorCode != null) return new(1, $"Windows didn't remove it: {result.ErrorText}");
            try { Directory.Delete(Folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            return new(0, "omawin's identity is gone; the history stays. Restart the menu app (omawin restart menu).");
        }

        // The package's logos: an "o" on the theme's accent (Windows shows them in its privacy
        // settings and the notification access list).
        static void Logo(string file, int size)
        {
            var theme = MenuModel.LoadTheme(MenuModel.CurrentThemeName());
            using var bitmap = new System.Drawing.Bitmap(size, size);
            using var g = System.Drawing.Graphics.FromImage(bitmap);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            g.Clear(System.Drawing.ColorTranslator.FromHtml(theme.Accent));
            using var font = new System.Drawing.Font("Segoe UI", size * 0.5f, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Pixel);
            using var brush = new System.Drawing.SolidBrush(System.Drawing.ColorTranslator.FromHtml(theme.Background));
            using var centred = new System.Drawing.StringFormat { Alignment = System.Drawing.StringAlignment.Center, LineAlignment = System.Drawing.StringAlignment.Center };
            g.DrawString("o", font, brush, new System.Drawing.RectangleF(0, 0, size, size * 0.94f), centred);
            bitmap.Save(file, System.Drawing.Imaging.ImageFormat.Png);
        }
    }
}
