using System.Diagnostics;
using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;

namespace SystemMenu;

// Every animation in the menu app (the menu, the launcher, the bar) takes its numbers from here
// (MotionTokens.cs) and runs on the compositor, off the UI thread. Off (`omawin toggle
// animations`, or Windows' own Settings > Accessibility > Visual effects > Animation effects),
// every one becomes a jump to its end: Composition can't run a 0 ms animation, so the helpers set
// the end value instead.
//
// The setting is %LOCALAPPDATA%\launcher\animations ("on" or "off"). With none yet, the first
// start writes one: off in a virtual machine, on anywhere else. Changing it re-renders GlazeWM's
// config (themes/render.ps1 -Reload), which is where its window animations take it from.
public static partial class Motion
{
    static string SettingFile => Path.Combine(MenuModel.StateDir, "animations");
    static bool fileOn = true;
    static Windows.UI.ViewManagement.UISettings? windows;
    static bool windowsOn = true;
    static FileSystemWatcher? watcher;

    // Raised on the UI thread when Enabled has changed.
    public static event Action? Changed;

    // Animate? Both the setting and Windows' own switch say yes.
    public static bool Enabled => fileOn && windowsOn;

    // The setting alone (the system menu's row shows it).
    public static bool On => fileOn;

    // From App.OnLaunched, on the UI thread, before anything animates.
    public static void Start(DispatcherQueue queue)
    {
        var said = Read();
        fileOn = said ?? true;
        if (said == null) Task.Run(WriteDefault);
        try
        {
            Directory.CreateDirectory(MenuModel.StateDir);
            watcher = new FileSystemWatcher(MenuModel.StateDir, "animations") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName, EnableRaisingEvents = true };
            // A file caught half written reads as neither: keep what it was until it says. Gone is on.
            FileSystemEventHandler again = (_, _) => queue.TryEnqueue(() => Update(Read() ?? (File.Exists(SettingFile) ? fileOn : true), windowsOn));
            watcher.Changed += again;
            watcher.Created += again;
            watcher.Deleted += again;
        }
        catch (Exception ex) { App.Log(ex); }
        try
        {
            windows = new Windows.UI.ViewManagement.UISettings();
            windowsOn = windows.AnimationsEnabled;
            // Raised on a worker thread (Windows 10 2004 and later, this app's minimum).
            windows.AnimationsEnabledChanged += (s, _) =>
            {
                bool on = s.AnimationsEnabled;
                queue.TryEnqueue(() => Update(fileOn, on));
            };
        }
        catch (Exception ex) { App.Log(ex); windowsOn = true; }
    }

    static void Update(bool file, bool system)
    {
        bool was = Enabled;
        fileOn = file;
        windowsOn = system;
        if (Enabled != was) Changed?.Invoke();
    }

    static bool? Read()
    {
        try { return File.Exists(SettingFile) ? ParseSetting(File.ReadAllText(SettingFile)) : null; }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    // The first start with no setting: off in a virtual machine (MotionTokens.cs says which).
    // Off there tells GlazeWM too; on is what it does anyway.
    static void WriteDefault()
    {
        bool vm = false;
        try
        {
            using var wmi = Wmi.Connect(@"root\cimv2");
            foreach (var pc in wmi.Query("SELECT Manufacturer, Model FROM Win32_ComputerSystem"))
                using (pc) vm |= IsVirtualMachine(pc["Manufacturer"] as string, pc["Model"] as string);
        }
        catch (Exception ex) { App.Log(ex); }
        if (Read() != null) return; // set meanwhile: that wins
        Write(!vm);
        if (vm) ReloadGlazeWm();
    }

    static void Write(bool on)
    {
        try
        {
            Directory.CreateDirectory(MenuModel.StateDir);
            File.WriteAllText(SettingFile, on ? "on" : "off");
        }
        catch (Exception ex) { App.Log(ex); }
    }

    // `omawin toggle animations [on|off|toggle|status]` (SystemMenu --toggle animations ...), on
    // the UI thread: the setting changes at once here, the note says what it is now.
    public static void Toggle(string? word)
    {
        if (word?.Trim().ToLowerInvariant() == "status") { Snip.Say(Status()); return; }
        if (Asked(fileOn, word) is not { } on) { Snip.Say("Animations: on, off, toggle or status"); return; }
        if (on != fileOn) Write(on);
        Update(on, windowsOn);
        ReloadGlazeWm();
        Snip.Say(Status());
    }

    static string Status() =>
        !fileOn ? "Animations: off"
        : windowsOn ? "Animations: on"
        : "Animations: on here, but off in Windows (Settings › Accessibility › Visual effects)";

    // GlazeWM's window animations follow the same file: render.ps1 fills it into the config
    // (animations.enabled) and reloads GlazeWM. In the background: the menu doesn't wait on it.
    static void ReloadGlazeWm()
    {
        try
        {
            var script = Path.Combine(MenuModel.ThemesDir, "render.ps1");
            if (!File.Exists(script)) return;
            Process.Start(new ProcessStartInfo("pwsh", $"-NoLogo -NoProfile -WindowStyle Hidden -File \"{script}\" -Reload") { UseShellExecute = false, CreateNoWindow = true })?.Dispose();
        }
        catch (Exception ex) { App.Log(ex); }
    }

    // --- the helpers ----------------------------------------------------------------------------
    public static CompositionEasingFunction EaseOut(Compositor c) =>
        c.CreateCubicBezierEasingFunction(new Vector2(EaseX1, EaseY1), new Vector2(EaseX2, EaseY2));

    // ease-in-out-sine, for what loops (a breath, a light drifting past): it neither arrives nor
    // leaves, so the one place an ease-in belongs.
    public static CompositionEasingFunction EaseInOut(Compositor c) =>
        c.CreateCubicBezierEasingFunction(new Vector2(0.37f, 0f), new Vector2(0.63f, 1f));

    // For XAML's implicit transitions (BrushTransition, Vector3Transition), where 0 is allowed.
    public static TimeSpan Or0(TimeSpan t) => Enabled ? t : TimeSpan.Zero;

    // Fades a visual in from nothing, after `delay` (rows arriving one after another).
    public static void FadeIn(Visual v, TimeSpan delay = default, TimeSpan? duration = null)
    {
        if (!Enabled) { v.StopAnimation("Opacity"); v.Opacity = 1; return; }
        var c = v.Compositor;
        var a = c.CreateScalarKeyFrameAnimation();
        a.InsertKeyFrame(0, 0);
        a.InsertKeyFrame(1, 1, EaseOut(c));
        a.Duration = duration ?? Enter;
        a.DelayTime = delay;
        a.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        v.StartAnimation("Opacity", a);
    }

    // Moves a visual's Translation from `from` to where it belongs. A XAML element's visual needs
    // ElementCompositionPreview.SetIsTranslationEnabled first.
    public static void MoveIn(Visual v, Vector3 from, TimeSpan delay = default, TimeSpan? duration = null)
    {
        if (!Enabled) { v.StopAnimation("Translation"); v.Properties.InsertVector3("Translation", Vector3.Zero); return; }
        var c = v.Compositor;
        var a = c.CreateVector3KeyFrameAnimation();
        a.InsertKeyFrame(0, from);
        a.InsertKeyFrame(1, Vector3.Zero, EaseOut(c));
        a.Duration = duration ?? Enter;
        a.DelayTime = delay;
        a.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
        v.StartAnimation("Translation", a);
    }

    // Arrives: fades in and rises `risePx` into place.
    public static void Arrive(Visual v, TimeSpan delay = default, float risePx = RisePx)
    {
        FadeIn(v, delay);
        MoveIn(v, new Vector3(0, risePx, 0), delay);
    }

    // Leaves: fades out and sinks `dropPx` over Exit, then `done` runs on the UI thread (at once
    // when animations are off). Cancel() on what it returns stops it and puts the visual back;
    // `done` then never runs. Call on the UI thread.
    public static Leaving Leave(Visual v, float dropPx, Action done)
    {
        var leaving = new Leaving(v, done);
        if (!Enabled) { leaving.Finish(); return leaving; }
        var c = v.Compositor;
        var fade = c.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1, 0, EaseOut(c));
        fade.Duration = Exit;
        var drop = c.CreateVector3KeyFrameAnimation();
        drop.InsertKeyFrame(1, new Vector3(0, dropPx, 0), EaseOut(c));
        drop.Duration = Exit;
        var batch = c.CreateScopedBatch(CompositionBatchTypes.Animation);
        v.StartAnimation("Opacity", fade);
        v.StartAnimation("Translation", drop);
        batch.End();
        var queue = DispatcherQueue.GetForCurrentThread();
        batch.Completed += (_, _) => queue.TryEnqueue(leaving.Finish);
        // And a little after it should have ended, whatever the compositor said: a window whose
        // frames stopped (another desktop, a locked screen) still gets hidden.
        var timer = queue.CreateTimer();
        timer.Interval = Exit + TimeSpan.FromMilliseconds(120);
        timer.IsRepeating = false;
        timer.Tick += (_, _) => leaving.Finish();
        timer.Start();
        leaving.Timer = timer;
        return leaving;
    }

    // An exit in progress. Finished once, by the compositor or the timer; cancelled by a key
    // that shows the window again.
    public sealed class Leaving(Visual visual, Action done)
    {
        bool over;
        public bool Over => over; // finished or cancelled: nothing left to wait for
        internal DispatcherQueueTimer? Timer; // held here, so it lives as long as the exit
        public void Finish()
        {
            if (over) return;
            over = true;
            Timer?.Stop();
            done();
        }
        public void Cancel()
        {
            over = true;
            Timer?.Stop();
            visual.StopAnimation("Opacity");
            visual.StopAnimation("Translation");
            visual.Opacity = 1;
            visual.Properties.InsertVector3("Translation", Vector3.Zero);
        }
    }

    // A light breathing (the bar's listening microphone): from full to `low` and back, forever;
    // still and full while animations are off.
    public static void Breathe(Visual v, float low)
    {
        v.StopAnimation("Opacity");
        v.Opacity = 1;
        if (!Enabled) return;
        var c = v.Compositor;
        var a = c.CreateScalarKeyFrameAnimation();
        var ease = EaseInOut(c); // a breath has no landing
        a.InsertKeyFrame(0.5f, low, ease);
        a.InsertKeyFrame(1, 1, ease);
        a.Duration = Pulse;
        a.IterationBehavior = AnimationIterationBehavior.Forever;
        v.StartAnimation("Opacity", a);
    }
}
