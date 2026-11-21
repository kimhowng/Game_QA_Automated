using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

static class GameQA
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [STAThread] static void Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--self-test")
        {
            using (var screen = new Bitmap(80, 70))
            using (var patch = new Bitmap(16, 14))
            {
                using (var graphics = Graphics.FromImage(patch)) graphics.Clear(Color.Red);
                if (Matcher.Find(screen, patch).HasValue) Environment.Exit(1);
                using (var graphics = Graphics.FromImage(screen)) graphics.DrawImageUnscaled(patch, 37, 23);
                var match = Matcher.Find(screen, patch);
                if (match != new Point(45, 30)) Environment.Exit(2);
            }
            return;
        }
        SetProcessDPIAware();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new MainForm());
    }
}

public sealed class Suite
{
    public string Executable { get; set; }
    public string Arguments { get; set; }
    public List<Step> Steps { get; set; }
}

public sealed class Step
{
    public string Action { get; set; }
    public string Value { get; set; }
    public string Seconds { get; set; }
}

static class Desktop
{
    [StructLayout(LayoutKind.Sequential)]
    struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr window, ref Point point);
    [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] static extern void mouse_event(uint flags, uint x, uint y, uint data, UIntPtr extra);
    [DllImport("user32.dll")] static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);

    public static Rectangle Bounds(IntPtr window)
    {
        Rect rect;
        var origin = Point.Empty;
        if (!GetClientRect(window, out rect) || !ClientToScreen(window, ref origin) ||
            rect.Right <= 0 || rect.Bottom <= 0)
            throw new InvalidOperationException("The game window is unavailable.");
        return new Rectangle(origin, new Size(rect.Right, rect.Bottom));
    }

    public static Bitmap Capture(IntPtr window)
    {
        var area = Bounds(window);
        var image = new Bitmap(area.Width, area.Height, PixelFormat.Format32bppArgb);
        try
        {
            using (var graphics = Graphics.FromImage(image))
                graphics.CopyFromScreen(area.Location, Point.Empty, area.Size);
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    public static void Click(Point point)
    {
        if (!SetCursorPos(point.X, point.Y)) throw new InvalidOperationException("Could not move the cursor.");
        mouse_event(2, 0, 0, 0, UIntPtr.Zero);
        mouse_event(4, 0, 0, 0, UIntPtr.Zero);
    }

    public static void Transition(Keys key, bool down)
    {
        if (key == Keys.LButton || key == Keys.RButton)
        {
            uint flag = key == Keys.LButton ? (down ? 2u : 4u) : (down ? 8u : 16u);
            mouse_event(flag, 0, 0, 0, UIntPtr.Zero);
            return;
        }
        uint extended = key == Keys.RControlKey || key == Keys.RMenu || (key >= Keys.PageUp && key <= Keys.Down) || key == Keys.Insert || key == Keys.Delete ? 1u : 0u;
        keybd_event((byte)key, 0, extended | (down ? 0u : 2u), UIntPtr.Zero);
    }

    public static void Press(Keys key)
    {
        uint flags = key == Keys.Left || key == Keys.Right || key == Keys.Up || key == Keys.Down ||
            key == Keys.Insert || key == Keys.Delete || key == Keys.Home || key == Keys.End ||
            key == Keys.PageUp || key == Keys.PageDown || key == Keys.RControlKey ||
            key == Keys.RMenu ? 1u : 0u;
        keybd_event((byte)key, 0, flags, UIntPtr.Zero);
        Thread.Sleep(40);
        keybd_event((byte)key, 0, flags | 2u, UIntPtr.Zero);
    }
}

static class Matcher
{
    static byte[] Pixels(Bitmap input)
    {
        using (var bitmap = input.Clone(new Rectangle(Point.Empty, input.Size), PixelFormat.Format32bppArgb))
        {
            var data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size), ImageLockMode.ReadOnly,
                PixelFormat.Format32bppArgb);
            try
            {
                var bytes = new byte[bitmap.Width * bitmap.Height * 4];
                for (int y = 0; y < bitmap.Height; y++)
                    Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), bytes, y * bitmap.Width * 4, bitmap.Width * 4);
                return bytes;
            }
            finally { bitmap.UnlockBits(data); }
        }
    }

    static int Difference(byte[] source, int offset, byte[] template, int target)
    {
        return Math.Abs(source[offset] - template[target]) +
            Math.Abs(source[offset + 1] - template[target + 1]) +
            Math.Abs(source[offset + 2] - template[target + 2]);
    }

    public static Point? Find(Bitmap frame, Bitmap patch)
    {
        if (patch.Width > frame.Width || patch.Height > frame.Height) return null;
        var source = Pixels(frame);
        var template = Pixels(patch);
        int center = ((patch.Height / 2) * patch.Width + patch.Width / 2) * 4;
        int bestError = patch.Width * patch.Height * 30;
        Point? bestMatch = null;
        for (int y = 0; y <= frame.Height - patch.Height; y++)
        for (int x = 0; x <= frame.Width - patch.Width; x++)
        {
            int start = (y * frame.Width + x) * 4;
            int midpoint = start + ((patch.Height / 2) * frame.Width + patch.Width / 2) * 4;
            if (Difference(source, midpoint, template, center) > 75) continue;
            int error = 0;
            for (int dy = 0; dy < patch.Height && error <= bestError; dy++)
            for (int dx = 0; dx < patch.Width && error <= bestError; dx++)
                error += Difference(source, start + (dy * frame.Width + dx) * 4,
                    template, (dy * patch.Width + dx) * 4);
            var location = new Point(x + patch.Width / 2, y + patch.Height / 2);
            if (error == 0) return location;
            if (error < bestError)
            {
                bestError = error;
                bestMatch = location;
            }
        }
        return bestMatch;
    }
}

sealed class MainForm : Form
{
    static readonly string[] Actions = { "Wait for image", "Click image", "Click point", "Press key", "Wait", "Expect exit", "Input down", "Input up", "Move pointer" };
    readonly TextBox executable = new TextBox { Width = 570 };
    readonly TextBox arguments = new TextBox { Width = 570 };
    readonly DataGridView steps = new DataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = false,
        AllowUserToDeleteRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false };
    readonly TextBox results = new TextBox { Dock = DockStyle.Bottom, Height = 150, Multiline = true,
        ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    readonly FlowLayoutPanel toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 82, Padding = new Padding(8) };
    readonly FlowLayoutPanel profile = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 76, Padding = new Padding(8) };
    readonly List<Button> commands = new List<Button>();
    readonly System.Windows.Forms.Timer recordingTimer = new System.Windows.Forms.Timer { Interval = 20 };
    readonly HashSet<int> held = new HashSet<int>();
    readonly HashSet<Keys> playing = new HashSet<Keys>();
    readonly Label outcome = new Label { Dock = DockStyle.Bottom, Height = 34, TextAlign = ContentAlignment.MiddleLeft, Text = "Test result   |   Ready" };
    readonly Stopwatch recordingGap = new Stopwatch();
    readonly JavaScriptSerializer json = new JavaScriptSerializer();
    readonly Button stop;
    Process game;
    bool busy;
    bool cancelled;
    bool recording;
    string suitePath;

    public MainForm()
    {
        Text = "Game QA";
        ClientSize = new Size(1020, 600);
        MinimumSize = new Size(960, 440);
        Font = new Font("Segoe UI", 10);
        StartPosition = FormStartPosition.CenterScreen;
        KeyPreview = true;
        profile.Controls.Add(new Label { Text = "Game executable", Width = 130, TextAlign = ContentAlignment.MiddleLeft });
        profile.Controls.Add(executable);
        AddButton(profile, "Browse", BrowseGame);
        profile.SetFlowBreak(profile.Controls[profile.Controls.Count - 1], true);
        profile.Controls.Add(new Label { Text = "Launch arguments", Width = 130, TextAlign = ContentAlignment.MiddleLeft });
        profile.Controls.Add(arguments);
        steps.Columns.Add(new DataGridViewComboBoxColumn { HeaderText = "Action", Width = 155, DataSource = Actions });
        steps.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Target, key, or X,Y", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        steps.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Seconds", Width = 85 });
        foreach (DataGridViewColumn column in steps.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
        AddButton(toolbar, "Clear recording", () => {
            if (steps.Rows.Count > 0 && MessageBox.Show(this, "Clear all steps in this editor?", "Clear recording", MessageBoxButtons.YesNo) == DialogResult.Yes)
                steps.Rows.Clear();
        });
        AddButton(toolbar, "Open", OpenSuite);
        AddButton(toolbar, "Save", SaveSuite);
        AddButton(toolbar, "Add step", () => AddStep(Actions[0], "", "15"));
        AddButton(toolbar, "Remove", RemoveStep);
        AddButton(toolbar, "Move up", () => MoveStep(-1));
        AddButton(toolbar, "Move down", () => MoveStep(1));
        AddButton(toolbar, "Capture target", CaptureTarget);
        AddButton(toolbar, "Record", StartRecording);
        AddButton(toolbar, "Run suite", RunSuite);
        stop = new Button { Text = "Stop", AutoSize = true, Enabled = false };
        stop.Click += delegate { cancelled = true; StopRecording(); };
        toolbar.Controls.Add(stop);
        Controls.Add(steps);
        Controls.Add(outcome);
        Controls.Add(results);
        Controls.Add(toolbar);
        Controls.Add(profile);
        recordingTimer.Tick += delegate { Guard(RecordTick); };
        FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) { cancelled = true; e.Cancel = true; } };
        Log("Select a game executable, then record a flow or add a step.");
    }

    void AddButton(Control panel, string title, Action action)
    {
        var button = new Button { Text = title, AutoSize = true };
        button.Click += delegate { Guard(action); };
        panel.Controls.Add(button);
        commands.Add(button);
    }

    void Guard(Action action)
    {
        try { action(); }
        catch (Exception ex) { Log(ex.Message); }
    }

    void Log(string message) { results.AppendText(DateTime.Now.ToString("HH:mm:ss") + "  " + message + Environment.NewLine); }

    void BrowseGame()
    {
        using (var dialog = new OpenFileDialog { Filter = "Game executable (*.exe)|*.exe" })
            if (dialog.ShowDialog(this) == DialogResult.OK) executable.Text = dialog.FileName;
    }

    void AddStep(string action, string value, string seconds) { steps.Rows.Add(action, value, seconds); }

    void RemoveStep()
    {
        if (steps.CurrentRow != null) steps.Rows.Remove(steps.CurrentRow);
    }

    void MoveStep(int offset)
    {
        var row = steps.CurrentRow;
        if (row == null) return;
        int destination = row.Index + offset;
        if (destination < 0 || destination >= steps.Rows.Count) return;
        steps.EndEdit();
        steps.Rows.Remove(row);
        steps.Rows.Insert(destination, row);
        steps.CurrentCell = row.Cells[0];
    }

    string Resolve(string path)
    {
        if (Path.IsPathRooted(path) || String.IsNullOrEmpty(suitePath)) return path;
        return Path.Combine(Path.GetDirectoryName(suitePath), path);
    }

    static bool ImageAction(string action) { return action == Actions[0] || action == Actions[1]; }

    List<Step> ReadSteps()
    {
        steps.EndEdit();
        return steps.Rows.Cast<DataGridViewRow>().Select(row => new Step {
            Action = Convert.ToString(row.Cells[0].Value), Value = Convert.ToString(row.Cells[1].Value),
            Seconds = Convert.ToString(row.Cells[2].Value)
        }).ToList();
    }

    void OpenSuite()
    {
        using (var dialog = new OpenFileDialog { Filter = "QA suite (*.json)|*.json" })
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            var suite = json.Deserialize<Suite>(File.ReadAllText(dialog.FileName));
            if (suite == null || suite.Steps == null || suite.Steps.Any(step => step == null || !Actions.Contains(step.Action)))
                throw new InvalidDataException("The suite contains an unsupported step.");
            suitePath = dialog.FileName;
            executable.Text = Resolve(suite.Executable ?? "");
            arguments.Text = suite.Arguments ?? "";
            steps.Rows.Clear();
            foreach (var step in suite.Steps) AddStep(step.Action, step.Value, step.Seconds);
            Log("Opened " + suitePath);
        }
    }

    void SaveSuite()
    {
        using (var dialog = new SaveFileDialog { Filter = "QA suite (*.json)|*.json", FileName = suitePath ?? "suite.json" })
        {
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            var list = ReadSteps();
            var folder = Path.GetDirectoryName(dialog.FileName);
            var prefix = folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (var step in list.Where(step => ImageAction(step.Action) && !String.IsNullOrWhiteSpace(step.Value)))
            {
                var full = Path.GetFullPath(Resolve(step.Value));
                step.Value = full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? full.Substring(prefix.Length) : full;
            }
            File.WriteAllText(dialog.FileName, json.Serialize(new Suite { Executable = executable.Text,
                Arguments = arguments.Text, Steps = list }));
            suitePath = dialog.FileName;
            for (int i = 0; i < list.Count; i++) steps.Rows[i].Cells[1].Value = list[i].Value;
            Log("Saved " + suitePath);
        }
    }

    void SetBusy(bool value)
    {
        busy = value;
        foreach (var button in commands) button.Enabled = !value;
        executable.ReadOnly = arguments.ReadOnly = steps.ReadOnly = value;
        stop.Enabled = value;
    }

    void Pause(double seconds)
    {
        var time = Stopwatch.StartNew();
        do
        {
            Application.DoEvents();
            if (cancelled) throw new OperationCanceledException();
            if (time.Elapsed.TotalSeconds >= seconds) return;
            Thread.Sleep(20);
        } while (true);
    }

    IntPtr Window()
    {
        if (game == null || game.HasExited) return IntPtr.Zero;
        game.Refresh();
        return game.MainWindowHandle;
    }

    void Launch()
    {
        var path = Resolve(executable.Text.Trim());
        if (!File.Exists(path)) throw new FileNotFoundException("Select a valid game executable.", path);
        path = Path.GetFullPath(path);
        if (game != null)
        {
            if (!game.HasExited) throw new InvalidOperationException("Close the previous game session before starting a new run.");
            game.Dispose();
        }
        game = Process.Start(new ProcessStartInfo(path, arguments.Text) {
            WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(path)), UseShellExecute = false
        });
        Log("Launched " + Path.GetFileName(path));
    }

    IntPtr ReadyWindow(double seconds)
    {
        var timer = Stopwatch.StartNew();
        do
        {
            Pause(0);
            if (game.HasExited) throw new InvalidOperationException("The game exited with code " + game.ExitCode + ".");
            var window = Window();
            if (window != IntPtr.Zero) return window;
            Pause(0.1);
        } while (timer.Elapsed.TotalSeconds < seconds);
        throw new TimeoutException("The game did not open a window.");
    }

    void FocusGame(IntPtr window)
    {
        if (Desktop.GetForegroundWindow() == window) return;
        Desktop.SetForegroundWindow(window);
        Pause(0.15);
        if (Desktop.GetForegroundWindow() != window)
            throw new InvalidOperationException("The game needs foreground focus. Bring it forward and retry.");
    }

    void StartRecording()
    {
        cancelled = false;
        SetBusy(true);
        try
        {
            if (Window() == IntPtr.Zero) Launch();
            FocusGame(ReadyWindow(30));
            held.Clear();
            recordingGap.Restart();
            recording = true;
            recordingTimer.Start();
            Log("Recording clicks and key taps. Press F10 to stop.");
        }
        finally { if (!recording) SetBusy(false); }
    }

    void StopRecording()
    {
        if (!recording) return;
        foreach (int key in held.ToArray()) RecordInput("Input up", ((Keys)key).ToString());
        held.Clear();
        recording = false;
        recordingTimer.Stop();
        SetBusy(false);
        Log("Recording stopped. Add image checkpoints to verify the flow.");
    }

    void RecordTick()
    {
        var window = Window();
        if (window == IntPtr.Zero) { StopRecording(); return; }
        bool focused = Desktop.GetForegroundWindow() == window;
        if (!focused)
        {
            foreach (int key in held.ToArray()) RecordInput("Input up", ((Keys)key).ToString());
            held.Clear();
            return;
        }
        if (Desktop.GetAsyncKeyState((int)Keys.F10) < 0) { StopRecording(); return; }
        for (int key = 1; key < 255; key++)
        {
            if ((key > 2 && key <= 6) || key == 16 || key == 17 || key == 18 ||
                key == (int)Keys.F10 || !Enum.IsDefined(typeof(Keys), key)) continue;
            bool down = Desktop.GetAsyncKeyState(key) < 0;
            if (down == held.Contains(key)) continue;
            if (down && key <= 2)
            {
                var bounds = Desktop.Bounds(window);
                var point = Cursor.Position;
                if (!bounds.Contains(point)) continue;
                RecordInput("Move pointer", (point.X - bounds.X) + "," + (point.Y - bounds.Y));
            }
            if (down) held.Add(key); else held.Remove(key);
            RecordInput(down ? "Input down" : "Input up", ((Keys)key).ToString());
        }
    }
    void RecordInput(string action, string value)
    {
        double gap = recordingGap.Elapsed.TotalSeconds;
        recordingGap.Restart();
        if (gap >= 0.01)
            AddStep(Actions[4], "", Math.Min(gap, 600).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
        AddStep(action, value, "");
    }

    void CaptureTarget()
    {
        cancelled = false;
        SetBusy(true);
        Bitmap snapshot = null;
        try
        {
            if (Window() == IntPtr.Zero) Launch();
            var window = ReadyWindow(30);
            FocusGame(window);
            snapshot = Desktop.Capture(window);
        }
        catch (OperationCanceledException) { return; }
        finally { SetBusy(false); }
        using (snapshot)
        using (var picker = new TargetPicker(snapshot))
        {
            if (picker.ShowDialog(this) != DialogResult.OK) return;
            using (var dialog = new SaveFileDialog { Filter = "PNG image (*.png)|*.png", FileName = "target.png" })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                using (var crop = snapshot.Clone(picker.RegionInImage, PixelFormat.Format32bppArgb))
                    crop.Save(dialog.FileName, ImageFormat.Png);
                if (steps.CurrentRow != null && ImageAction(Convert.ToString(steps.CurrentRow.Cells[0].Value)))
                    steps.CurrentRow.Cells[1].Value = dialog.FileName;
                else AddStep(Actions[0], dialog.FileName, "15");
                Log("Captured " + dialog.FileName);
            }
        }
    }

    static double Duration(Step step)
    {
        double seconds;
        if (!Double.TryParse(step.Seconds, System.Globalization.NumberStyles.AllowDecimalPoint,
            System.Globalization.CultureInfo.InvariantCulture, out seconds) || seconds < 0 || seconds > 600)
            throw new InvalidDataException("Use a duration from 0 to 600 seconds.");
        return seconds;
    }

    void Execute(Step step)
    {
        if (step.Action == Actions[4]) { Pause(Duration(step)); return; }
        if (step.Action == Actions[5])
        {
            var limit = Duration(step);
            var timer = Stopwatch.StartNew();
            while (!game.HasExited && timer.Elapsed.TotalSeconds < limit) Pause(0.05);
            if (!game.HasExited) throw new TimeoutException("The game did not exit.");
            if (game.ExitCode != 0) throw new InvalidOperationException("The game exited with code " + game.ExitCode + ".");
            return;
        }

        var window = ReadyWindow(30);
        FocusGame(window);
        if (step.Action == Actions[3])
        {
            Keys key;
            if (!Enum.TryParse(step.Value, true, out key) || (int)key <= 6 || (int)key > 254)
                throw new InvalidDataException("Enter a key such as Escape, Enter, or W.");
            Desktop.Press(key);
            return;
        }
        if (step.Action == "Input down" || step.Action == "Input up")
        {
            Keys key;
            if (!Enum.TryParse(step.Value, true, out key) || (int)key < 1 || (int)key > 254)
                throw new InvalidDataException("Invalid input name.");
            bool down = step.Action == "Input down";
            if (down) playing.Add(key); else if (!playing.Remove(key)) return;
            Desktop.Transition(key, down);
            return;
        }
        if (step.Action == Actions[2] || step.Action == "Move pointer")
        {
            var parts = step.Value.Split(',');
            int x, y;
            var area = Desktop.Bounds(window);
            if (parts.Length != 2 || !Int32.TryParse(parts[0], out x) || !Int32.TryParse(parts[1], out y) ||
                x < 0 || y < 0 || x >= area.Width || y >= area.Height)
                throw new InvalidDataException("Enter X,Y coordinates inside the game window.");
            var point = new Point(area.X + x, area.Y + y);
            if (step.Action == "Move pointer") Cursor.Position = point;
            else Desktop.Click(point);
            return;
        }

        var timeout = Duration(step);
        using (var target = new Bitmap(Resolve(step.Value)))
        {
            var timer = Stopwatch.StartNew();
            do
            {
                Pause(0);
                if (game.HasExited) throw new InvalidOperationException("The game exited with code " + game.ExitCode + ".");
                if (Desktop.GetForegroundWindow() != window) throw new InvalidOperationException("The game lost foreground focus.");
                Point? match;
                using (var frame = Desktop.Capture(window)) match = Matcher.Find(frame, target);
                if (match.HasValue)
                {
                    if (step.Action == Actions[1])
                    {
                        if (Desktop.GetForegroundWindow() != window) throw new InvalidOperationException("The game lost foreground focus.");
                        var area = Desktop.Bounds(window);
                        Desktop.Click(new Point(area.X + match.Value.X, area.Y + match.Value.Y));
                    }
                    return;
                }
                if (timer.Elapsed.TotalSeconds < timeout) Pause(0.15);
            } while (timer.Elapsed.TotalSeconds < timeout);
        }
        throw new TimeoutException("Image not found: " + step.Value);
    }

    void RunSuite()
    {
        var list = ReadSteps();
        if (list.Count == 0) throw new InvalidOperationException("Add at least one step before running.");
        cancelled = false;
        SetBusy(true);
        outcome.BackColor = SystemColors.Control;
        outcome.Text = "Test result   |   Running";
        int index = 0;
        try
        {
            Launch();
            for (; index < list.Count; index++)
            {
                var step = list[index];
                Log(String.Format("Step {0}: {1} {2}", index + 1, step.Action, step.Value));
                Execute(step);
                Pause(0);
                bool expectsExitNext = index + 1 < list.Count && list[index + 1].Action == Actions[5];
                if (step.Action != Actions[5] && !expectsExitNext && game.HasExited)
                    throw new InvalidOperationException("The game exited unexpectedly with code " + game.ExitCode + ".");
            }
            outcome.BackColor = Color.PaleGreen;
            outcome.Text = "Test result   |   PASS";
            Log("PASS — " + list.Count + " steps completed.");
        }
        catch (OperationCanceledException) { outcome.Text = "Test result   |   Stopped"; Log("Run stopped."); }
        catch (Exception ex)
        {
            outcome.BackColor = Color.LightCoral;
            outcome.Text = "Test result   |   FAIL   |   Step " + (index + 1);
            Log("FAIL — step " + (index + 1) + ": " + ex.Message);
            Guard(delegate {
                var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "GameQA", "Failures", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(directory);
                var report = Path.Combine(directory, "run.txt");
                File.WriteAllText(report, results.Text);
                Log("Failure report: " + report);
                var window = Window();
                if (window != IntPtr.Zero && Desktop.GetForegroundWindow() == window)
                {
                    var file = Path.Combine(directory, "game.png");
                    using (var frame = Desktop.Capture(window)) frame.Save(file, ImageFormat.Png);
                    Log("Failure screenshot: " + file);
                }
            });
        }
        finally {
            foreach (var key in playing) Desktop.Transition(key, false);
            playing.Clear();
            SetBusy(false);
        }
    }
}

sealed class TargetPicker : Form
{
    readonly Bitmap snapshot;
    readonly Rectangle preview;
    Point start;
    Point current;
    bool dragging;
    public Rectangle RegionInImage { get; private set; }

    public TargetPicker(Bitmap image)
    {
        snapshot = image;
        Text = "Drag around a distinctive game element";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        DoubleBuffered = true;
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        double scale = Math.Min(1, Math.Min((area.Width - 100.0) / image.Width, (area.Height - 120.0) / image.Height));
        preview = new Rectangle(0, 0, Math.Max(1, (int)(image.Width * scale)), Math.Max(1, (int)(image.Height * scale)));
        ClientSize = preview.Size;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.DrawImage(snapshot, preview);
        if (!dragging) return;
        using (var pen = new Pen(Color.Cyan, 2))
            e.Graphics.DrawRectangle(pen, Rectangle.FromLTRB(Math.Min(start.X, current.X), Math.Min(start.Y, current.Y),
                Math.Max(start.X, current.X), Math.Max(start.Y, current.Y)));
    }

    Point ImagePoint(Point point)
    {
        return new Point(Math.Max(0, Math.Min(snapshot.Width, (int)Math.Round(point.X * (double)snapshot.Width / preview.Width))),
            Math.Max(0, Math.Min(snapshot.Height, (int)Math.Round(point.Y * (double)snapshot.Height / preview.Height))));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        start = current = e.Location;
        dragging = true;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!dragging) return;
        current = e.Location;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!dragging || e.Button != MouseButtons.Left) return;
        dragging = false;
        Capture = false;
        var first = ImagePoint(start);
        var last = ImagePoint(new Point(e.X + 1, e.Y + 1));
        RegionInImage = Rectangle.FromLTRB(Math.Min(first.X, last.X), Math.Min(first.Y, last.Y),
            Math.Max(first.X, last.X), Math.Max(first.Y, last.Y));
        if (RegionInImage.Width >= 8 && RegionInImage.Height >= 8) DialogResult = DialogResult.OK;
        else Invalidate();
    }
}
