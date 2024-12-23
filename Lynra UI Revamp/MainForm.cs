using ForlornApi;
using Guna.UI2.WinForms;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Linq;
using lynraApi;
using System.Text.Json;
using ScriptBloxAPI.Methods;
using System.Net.Http;
using System.Threading;
using System.IO.Pipes;
using System.Text;
using System.Diagnostics;

namespace LynraNamespace
{
    public partial class MainForm : Form
    {
        private Point lastPoint;
        private System.Windows.Forms.Timer tweenTimer;
        private System.Windows.Forms.Timer injectionTimer;
        private System.Windows.Forms.Timer saveTimer;
        private Point targetLocation;
        private DateTime lastSaveTime;
        private Guna2Button activeButton;
        private Guna2Button activeSButton;
        private Guna2Button activeScButton;
        private Guna2Button selectedTabButton;
        private const int cooldownTime = 30;
        private const double tweenSpeed = 0.2;
        private bool InjectionTimerDebounce = false;
        private bool isInjected = false;
        private readonly Properties.Settings DS;
        private string selectedTab;
        private string selectedApi;
        private bool recentlyLoaded;
        private readonly List<Guna2Panel> activeNotifications = new List<Guna2Panel>();
        private CancellationTokenSource _cancellationTokenSource;

        public MainForm()
        {
            activeButton = HomeButton;
            activeSButton = GeneralSBtn;
            activeScButton = BuiltInScBtn;
            
            recentlyLoaded = true;

            InitializeComponent();
            
            string tabsDirectory = Path.Combine(Application.StartupPath, "bin", "tabs");
            if (!Directory.Exists(tabsDirectory)) Directory.CreateDirectory(tabsDirectory);

            tweenTimer = new System.Windows.Forms.Timer { Interval = 10 };
            tweenTimer.Tick += TweenTimer_Tick;

            injectionTimer = new System.Windows.Forms.Timer { Interval = 500 };
            injectionTimer.Tick += InjectionTimer_Tick;
            injectionTimer.Start();

            Editor.EnsureCoreWebView2Async();

            string mainTabPath = Path.Combine(Application.StartupPath, "bin", "tabs", "Main Tab.lua");

            if (!File.Exists(mainTabPath)) File.Create(mainTabPath);

            DS = Properties.Settings.Default;
            selectedTab = "";
            LoadTabs();

            int retryCount = 0;
            while (DS == null && retryCount < 10)
            {
                System.Threading.Thread.Sleep(1000);
                DS = Properties.Settings.Default;
                retryCount++;
            }

            if (DS != null)
            {
                DS.TimesLynraOpened += 1;
                DS.Save();

                TIValue.Text = $"Total Injections: {DS.Injections}";
                TLOValue.Text = $"Times Lynra Opened: {DS.TimesLynraOpened}";

                if (DS.Theme == "Dark") DarkButton.PerformClick();
                else if (DS.Theme == "Light") LightButton.PerformClick();

                Task.Delay(1000);

                try
                {
                    int interval = DS.AutoSaveInterval;

                    if (interval <= 0)
                    {
                        interval = 5;
                    }

                    saveTimer = new System.Windows.Forms.Timer { Interval = interval * 60000 };
                    saveTimer.Tick += SaveTimer_Tick;
                    saveTimer.Start();
                }
                catch { }
            }
            else
            {
                ShowNotification("Failed to load settings after multiple attempts.", "Error", 5000, 9);
            }
        }

        // \\

        private async void Execute(string script)
        {
            using (NamedPipeClientStream pipeClient = new NamedPipeClientStream(".", "SkibidiToiletSaidAUWAndNowSkibidiToiletSad", PipeDirection.Out))
            {
                try
                {
                    await pipeClient.ConnectAsync();

                    byte[] scriptBytes = Encoding.UTF8.GetBytes(script);

                    await pipeClient.WriteAsync(scriptBytes, 0, scriptBytes.Length);

                    pipeClient.Close();
                }
                catch (Exception) { }
            }
        }

        // \\
        private void AddLog(string message) => Terminal.AppendText($"{DateTime.Now:HH:mm:ss} - {message}\r\n");

        private void LoadTabs()
        {
            string tabsDirectory = Path.Combine(Application.StartupPath, "bin", "tabs");
            var tabFiles = Directory.GetFiles(tabsDirectory, "*.lua");

            foreach (var tabFile in tabFiles)
            {
                string tabName = Path.GetFileNameWithoutExtension(tabFile);
                string tabExtension = Path.GetExtension(tabFile);
                if (tabExtension == ".lua") if (!TabsHolder.Controls.OfType<Guna2Button>().Any(btn => btn.Name == $"Tab{tabName}Button")) CreateNewTab(tabName, false);
            }

            RearrangeTabs();
        }

        private async void SaveTimer_Tick(object sender, EventArgs e)
        {
            if (!string.IsNullOrEmpty(selectedTab))
            {
                string currentFilePath = Path.Combine(Application.StartupPath, "bin", "tabs", $"{selectedTab}.lua");
                string currentText = await GetText();

                try
                {
                    using (StreamWriter writer = new StreamWriter(currentFilePath))
                    {
                        await writer.WriteAsync(currentText);
                    }
                }
                catch (Exception) { }
            }

            ShowNotification($"Auto-saved successfully!", "Info", 2000, 9);
        }

        public void ShowNotification(string message, string type = "Info", int duration = 3000, int fontSize = 10)
        {
            var notificationPanel = new Guna2Panel
            {
                Size = new Size(250, 60),
                BorderRadius = 10,
                BorderColor = DS.Theme == "Dark" ? Color.FromArgb(50, 50, 50) : Color.FromArgb(180, 180, 180),
                BorderThickness = 2,
                FillColor = DS.Theme == "Dark" ? Color.FromArgb(40, 40, 40) : Color.FromArgb(200, 200, 200),
                BackColor = Color.Transparent,
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };

            var iconBox = new PictureBox
            {
                Size = new Size(40, 40),
                Location = new Point(10, 10),
                SizeMode = PictureBoxSizeMode.Zoom,
            };

            if (type == "Error") iconBox.Image = Properties.Resources.ErrorIcon;
            else if (type == "Info") iconBox.Image = Properties.Resources.InfoIcon;

            var messageLabel = new Label
            {
                Text = message,
                ForeColor = Color.White,
                Font = new Font("Segoe UI", fontSize, FontStyle.Bold),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.None,
                Location = new Point(60, 15),
                Size = new Size(180, 30)
            };

            notificationPanel.Controls.Add(iconBox);
            notificationPanel.Controls.Add(messageLabel);
            Controls.Add(notificationPanel);
            activeNotifications.Add(notificationPanel);

            notificationPanel.BringToFront();
            ArrangeNotifications();

            var timer = new System.Windows.Forms.Timer { Interval = duration };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                timer.Dispose();

                Controls.Remove(notificationPanel);
                activeNotifications.Remove(notificationPanel);
                ArrangeNotifications();
            };
            timer.Start();
        }

        private void ArrangeNotifications()
        {
            int startX = ClientSize.Width - 270;
            int startY = 15;
            int spacing = 10;

            for (int i = 0; i < activeNotifications.Count; i++)
            {
                var panel = activeNotifications[i];
                panel.Location = new Point(startX, startY + i * (panel.Height + spacing));
            }
        }

        private void UpperPanel_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                lastPoint = e.Location;
                tweenTimer.Stop();
            }
        }

        private void UpperPanel_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                targetLocation = new Point(
                    Location.X + e.X - lastPoint.X,
                    Location.Y + e.Y - lastPoint.Y
                );

                if (!tweenTimer.Enabled) tweenTimer.Start();
            }
        }

        private void TweenTimer_Tick(object sender, EventArgs e)
        {
            int newX = (int)(Location.X + (targetLocation.X - Location.X) * tweenSpeed);
            int newY = (int)(Location.Y + (targetLocation.Y - Location.Y) * tweenSpeed);

            Location = new Point(newX, newY);

            if (Math.Abs(Location.X - targetLocation.X) < 1 &&
                Math.Abs(Location.Y - targetLocation.Y) < 1)
            {
                Location = targetLocation;
                tweenTimer.Stop();
            }
        }

        private void InjectionTimer_Tick(object sender, EventArgs e)
        {
            if (!Lynra.IsRobloxOpen()) isInjected = false;

            if (Api.IsInjected() || isInjected)
            {
                if (!InjectionTimerDebounce)
                {
                    InjectionTimerDebounce = true;
                    DS.Injections += 1;
                    DS.Save();
                    TIValue.Text = $"Total Attaches: {DS.Injections.ToString()}";
                    AddLog("Attached to Roblox");
                }
            }
            else InjectionTimerDebounce = false;

            // We've already put credits, plus it can be distracting

            try
            {
                if (DS.API == "lynraApi") Execute("local hui = gethui() if hui:FindFirstChild(\"mgfdkkgkgidkfk\") then hui:WaitForChild(\"mgfdkkgkgidkfk\"):Destroy() end");
            }
            catch { }
        }

        public async void SetText(string text)
        {
            string encodedText = string.Empty;

            if (!string.IsNullOrEmpty(text)) encodedText = JavaScriptEncoder.Default.Encode(text);

            string script = $@"
        var model = editor.getModel();
        var currentText = model.getValue();
        editor.executeEdits('', [{{
            range: model.getFullModelRange(),
            text: '{encodedText}'
        }}]);
    ";
            await Editor.EnsureCoreWebView2Async();
            await Editor.CoreWebView2.ExecuteScriptAsync(script);
        }

        public async Task<string> GetText()
        {
            await Editor.EnsureCoreWebView2Async();
            var result = await Editor.CoreWebView2.ExecuteScriptAsync("GetText();");

            if (result != null)
            {
                return JsonSerializer.Deserialize<string>(result);
            }

            return "null";
        }

        private async void MainForm_Load(object sender, EventArgs e)
        {
            AddLog("Lynra loaded");
            selectedApi = DS.API;

            InnerElipse.TargetControl = Inner;

            AutoAttachToggle.Checked = DS.AutoAttach;
            AutoSaveIntervalSlider.Value = DS.AutoSaveInterval;
            AutoSaveIntervalValue.Text = $"{DS.AutoSaveInterval} mins";

            if (DS.API == "Forlorn") Api.SetAutoInject(AutoAttachToggle.Checked);

            APIDropdown.SelectedItem = DS.API;

            ArrangeNotifications();

            SettingsPanel.BringToFront();
            PlayerPanel.BringToFront();
            ScriptsPanel.BringToFront();
            MainPanel.BringToFront();
            HomePanel.BringToFront();
            Inner.SendToBack();

            ResetButtonStyles(true, false);
            ResetButtonStyles(false, true);

            ShowPanel(HomePanel, HomeButton);
            ShowPanel(GeneralSPanel, GeneralSBtn, true, false);
            ShowPanel(BuiltInScPanel, BuiltInScBtn, false, true);

            await Editor.EnsureCoreWebView2Async();
            await Task.Delay(500);

            if (!string.IsNullOrEmpty(selectedTab))
            {
                string currentFilePath = Path.Combine(Application.StartupPath, "bin", "tabs", $"{selectedTab}.lua");
                string currentText = await GetText();
                SetText(currentText);
            };

            recentlyLoaded = false;

            string monacoPath = Path.Combine(Application.StartupPath, "bin", "DebugMonaco", "monaco.html");

            if (File.Exists(monacoPath)) Editor.Source = new Uri(monacoPath);
        }

        private void MainForm_Resize(object sender, EventArgs e)
        {
            
        }

        private void AttachButton_Click(object sender, EventArgs e)
        {
            AddLog("Tried to attach");

            if (!Lynra.IsRobloxOpen())
            {
                ShowNotification("Roblox is not open!", "Error", 3000, 12);
            }
            else
            {
                if (selectedApi == "lynraApi")
                {
                    Process.Start(Path.Combine(Application.StartupPath, "bin", "Injector.exe"));
                    isInjected = true;
                }
                else if (selectedApi == "Forlorn") Api.Inject();
            }
        }

        private async void ExecuteButton_Click(object sender, EventArgs e)
        {
            var FetchedText = await GetText();

            if (!string.IsNullOrWhiteSpace(FetchedText))
            {
                if (selectedApi == "lynraApi") Execute(FetchedText);
                else if (selectedApi == "Forlorn") Api.ExecuteScript(FetchedText);

                ShowNotification("Executed script!", "Info", 3000, 16);
                AddLog("Executed a script");
            }
        }

        private void ClearButton_Click(object sender, EventArgs e)
        {
            SetText("");
            AddLog("Cleared the monaco editor");
        }

        private void Exit_Click(object sender, EventArgs e) => Application.Exit();

        private void Minimize_Click(object sender, EventArgs e) => WindowState = FormWindowState.Minimized;

        private void ShowPanel(Panel panel, Guna2Button button, bool isSPanel = false, bool isScPanel = false)
        {
            ResetButtonStyles(isSPanel, isScPanel);

            button.FillColor = DS.Theme == "Dark"
                ? Color.FromArgb(70, 70, 70)
                : Color.FromArgb(255, 87, 34);

            button.BorderColor = DS.Theme == "Dark"
                ? Color.FromArgb(0, 150, 136)
                : Color.FromArgb(255, 87, 34);

            if (!isSPanel && !isScPanel)
            {
                MainPanel.Visible = false;
                SettingsPanel.Visible = false;
                PlayerPanel.Visible = false;
                ScriptsPanel.Visible = false;
                HomePanel.Visible = false;
                ThemesPanel.Visible = false;
            }
            else if (isSPanel)
            {
                GeneralSPanel.Visible = false;
                UISPanel.Visible = false;
                OtherSPanel.Visible = false;

                activeSButton = button;
            }
            else if (isScPanel)
            {
                BuiltInScPanel.Visible = false;
                CloudScPanel.Visible = false;

                activeScButton = button;
            }

            panel.Visible = true;
            activeButton = button;

            UpdateTheme();
        }
        private void ResetButtonStyles(bool isSPanel = false, bool isScPanel = false)
        {
            if (isSPanel)
            {
                GeneralSBtn.FillColor = Color.Transparent;
                UISBtn.FillColor = Color.Transparent;
                OtherSBtn.FillColor = Color.Transparent;

                GeneralSBtn.BorderColor = Color.Transparent;
                UISBtn.BorderColor = Color.Transparent;
                OtherSBtn.BorderColor = Color.Transparent;
            }
            else if (isScPanel)
            {
                BuiltInScBtn.FillColor = Color.Transparent;
                CloudScBtn.FillColor = Color.Transparent;

                BuiltInScBtn.BorderColor = Color.Transparent;
                CloudScBtn.BorderColor = Color.Transparent;
            }
            else
            {
                ExecutorButton.FillColor = Color.Transparent;
                SettingsButton.FillColor = Color.Transparent;
                PlayerButton.FillColor = Color.Transparent;
                ScriptsButton.FillColor = Color.Transparent;
                HomeButton.FillColor = Color.Transparent;
                ThemesButton.FillColor = Color.Transparent;

                ExecutorButton.BorderColor = Color.Transparent;
                SettingsButton.BorderColor = Color.Transparent;
                PlayerButton.BorderColor = Color.Transparent;
                ScriptsButton.BorderColor = Color.Transparent;
                HomeButton.BorderColor = Color.Transparent;
                ThemesButton.BorderColor = Color.Transparent;
            }
        }

        private void UpdateTheme()
        {
            foreach (Control control in Controls)
            {
                if (control is Guna2Button gunaButton)
                    ApplyButtonTheme(gunaButton, BackColor, ForeColor, DS.Theme == "Dark");
                else if (control is Guna2Panel gunaPanel && control != UpperPanel)
                    ApplyPanelTheme(gunaPanel, BackColor, ForeColor, DS.Theme == "Dark");
                else
                    ApplyControlTheme(control, BackColor, ForeColor, DS.Theme == "Dark");
            }
        }


        private void ApplyButtonTheme(Guna2Button button, Color backgroundColor, Color textColor, bool isDarkMode)
        {
            //button.FillColor = isDarkMode ? Color.FromArgb(255, 150, 0) : Color.FromArgb(255, 200, 150);
            button.ForeColor = textColor;
        }

        private void ApplyPanelTheme(Guna2Panel panel, Color backgroundColor, Color textColor, bool isDarkMode)
        {
            panel.FillColor = backgroundColor;
            panel.BorderColor = isDarkMode ? Color.FromArgb(20, 20, 20) : Color.FromArgb(180, 180, 180);
        }

        private void ApplyControlTheme(Control control, Color backgroundColor, Color textColor, bool isDarkMode)
        {
            if (control is Label || control is CheckBox || control is RadioButton)
            {
                control.ForeColor = textColor;
            }

            if (control.HasChildren)
            {
                foreach (Control childControl in control.Controls) ApplyControlTheme(childControl, backgroundColor, textColor, isDarkMode);
            }
        }

        private void DarkButton_Click(object sender, EventArgs e)
        {
            ApplyTheme(
                Color.FromArgb(22, 22, 23),
                Color.White,
                Color.FromArgb(40, 40, 40),
                true
            );

            DS.Theme = "Dark";
            DS.Save();

            SetMonacoDarkTheme();
            ResetButtonStyles();
            HighlightActiveButtons();
            UpdateTheme();
            UpdateTabSeparators(true);
            AddLog("Changed to dark theme");
        }

        private void LightButton_Click(object sender, EventArgs e)
        {
            ApplyTheme(
                Color.FromArgb(225, 225, 225),
                Color.Black,
                Color.FromArgb(180, 180, 180),
                false
            );

            DS.Theme = "Light";
            DS.Save();

            SetMonacoLightTheme();
            ResetButtonStyles();
            HighlightActiveButtons();
            UpdateTheme();
            UpdateTabSeparators(false);
            AddLog("Changed to light theme");
        }

        private void UpdateTabSeparators(bool isDarkMode)
        {
            if (selectedTabButton != null)
            {
                foreach (Control control in TabsHolder.Controls)
                {
                    if (control is Guna2Separator separator && control.Name.Contains("Separator"))
                    {
                        if (control.Name.Replace("Separator", "") == selectedTabButton.Name.Replace("Button", ""))
                        {
                            separator.FillColor = isDarkMode ? Color.FromArgb(255, 255, 255) : Color.FromArgb(60, 60, 60);
                        }
                        else separator.FillColor = isDarkMode ? Color.FromArgb(150, 150, 150) : Color.FromArgb(255, 255, 255);
                    }
                }
            }    
        }

        private void HighlightActiveButtons()
        {
            HighlightButton(activeButton);
            HighlightButton(activeSButton);
            HighlightButton(activeScButton);
        }

        private void HighlightButton(Guna2Button button)
        {
            if (button != null)
            {
                button.FillColor = DS.Theme == "Dark"
                    ? Color.FromArgb(70, 70, 70)
                    : Color.FromArgb(255, 87, 34);

                button.BorderColor = DS.Theme == "Dark"
                    ? Color.FromArgb(0, 150, 136)
                    : Color.FromArgb(255, 87, 34);
            }
        }


        private async void SetMonacoLightTheme()
        {
            if (Editor == null || Editor.CoreWebView2 == null)
            {
                return;
            }

            try
            {
                await Editor.EnsureCoreWebView2Async();
                await Task.Delay(200);

                string script = @"
        if (typeof monaco !== 'undefined') {
            monaco.editor.setTheme('vs');
        }
        ";

                await Editor.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error occurred while setting theme: " + ex.Message);
            }
        }

        private async void SetMonacoDarkTheme()
        {
            if (Editor == null || Editor.CoreWebView2 == null)
            {
                return;
            }

            try
            {
                await Editor.EnsureCoreWebView2Async();
                await Task.Delay(200);

                string script = @"
        if (typeof monaco !== 'undefined') {
            monaco.editor.setTheme('Dark');
        }
        ";

                await Editor.CoreWebView2.ExecuteScriptAsync(script);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error occurred while setting theme: " + ex.Message);
            }
        }

        private void ExecutorButton_Click(object sender, EventArgs e)
        {
            ShowPanel(MainPanel, ExecutorButton);
            if (DS.Theme == "Dark") SetMonacoDarkTheme();
            else if (DS.Theme == "Light") SetMonacoLightTheme();
        }

        private void SettingsButton_Click(object sender, EventArgs e) => ShowPanel(SettingsPanel, SettingsButton);

        private void PlayerButton_Click(object sender, EventArgs e) => ShowPanel(PlayerPanel, PlayerButton);

        private void ScriptsButton_Click(object sender, EventArgs e) => ShowPanel(ScriptsPanel, ScriptsButton);

        private void HomeButton_Click(object sender, EventArgs e) => ShowPanel(HomePanel, HomeButton);

        private void ThemesButton_Click(object sender, EventArgs e) => ShowPanel(ThemesPanel, ThemesButton);

        private void AutoAttachToggle_CheckedChanged(object sender, EventArgs e)
        {
            Api.SetAutoInject(AutoAttachToggle.Checked);
            DS.AutoAttach = AutoAttachToggle.Checked;
            DS.Save();
            AddLog($"Turned {(AutoAttachToggle.Checked ? "on" : "off")} auto attach");
        }

        private void KillRobloxButton_Click(object sender, EventArgs e) => Lynra.KillRoblox();

        private void TopMostToggle_CheckedChanged(object sender, EventArgs e)
        {
            TopMost = TopMostToggle.Checked;
            DS.TopMost = TopMostToggle.Checked;
            DS.Save();
        }

        private async void SaveButton_Click(object sender, EventArgs e)
        {
            if ((DateTime.Now - lastSaveTime).TotalSeconds < cooldownTime)
            {
                int remainingTime = cooldownTime - (int)(DateTime.Now - lastSaveTime).TotalSeconds;

                ShowNotification($"Not saved. You are on cooldown! ({remainingTime}s)", "Error", 2000, 9);
            }
            else
            {
                await SaveDataAsync();
                lastSaveTime = DateTime.Now;

                ShowNotification("Saved successfully!", "Info", 2000, 9);
            }
        }

        private async Task SaveDataAsync()
        {
            string text = await GetText();

            if (!string.IsNullOrEmpty(selectedTab))
            {
                string currentFilePath = Path.Combine(Application.StartupPath, "bin", "tabs", $"{selectedTab}.lua");

                try
                {
                    using (StreamWriter writer = new StreamWriter(currentFilePath))
                    {
                        await writer.WriteAsync(text);
                    }
                }
                catch (Exception) { }
            }
        }

        private void AutoSaveIntervalSlider_Scroll(object sender, ScrollEventArgs e) => AutoSaveIntervalValue.Text = $"{e.NewValue.ToString()} mins";

        private void AutoSaveInternalSaveButton_Click(object sender, EventArgs e)
        {
            DS.AutoSaveInterval = AutoSaveIntervalSlider.Value;
            DS.Save();

            RestartSaveTimer(DS.AutoSaveInterval * 60000);
            ShowNotification($"Auto-save interval updated to {DS.AutoSaveInterval} mins.", "Info", 3000, 9);
        }

        private void RestartSaveTimer(int newInterval)
        {
            saveTimer.Stop();
            saveTimer.Dispose();

            saveTimer = new System.Windows.Forms.Timer { Interval = newInterval };
            saveTimer.Tick += SaveTimer_Tick;
            saveTimer.Start();
        }

        private void ResetDefaultsButton_Click(object sender, EventArgs e)
        {
            WalkSpeedSlider.Value = 16;
            JumpPowerSlider.Value = 50;

            WalkSpeedValue.Text = "16";
            JumpPowerValue.Text = "50";

            Execute("game.Players.LocalPlayer.Character.Humanoid.WalkSpeed = 16");
            Execute("game.Players.LocalPlayer.Character.Humanoid.JumpPower = 50");
        }

        private void JumpPowerSlider_Scroll(object sender, ScrollEventArgs e)
        {
            JumpPowerValue.Text = e.NewValue.ToString();
            Execute($"game.Players.LocalPlayer.Character.Humanoid.JumpPower = {e.NewValue}");
        }

        private void WalkSpeedSlider_Scroll(object sender, ScrollEventArgs e)
        {
            WalkSpeedValue.Text = e.NewValue.ToString();
            Execute($"game.Players.LocalPlayer.Character.Humanoid.WalkSpeed = {e.NewValue}");
        }

        private void IYExecute_Click(object sender, EventArgs e) => Execute("loadstring(game:HttpGet(\"https://raw.githubusercontent.com/EdgeIY/infiniteyield/master/source\"))()");

        private void OrcaExecute_Click(object sender, EventArgs e) => Execute("loadstring(game:HttpGetAsync(\"https://raw.githubusercontent.com/richie0866/orca/master/public/latest.lua\"))()");

        private void ApplyBaseTheme(Control control, Color backgroundColor, Color textColor, Color borderColor, bool isDarkMode, Guna2Button activeButton = null)
        {
            if (control is Label)
            {
                if (control == WindowsIndicator) ForeColor = isDarkMode ? Color.White : Color.Black;
                else control.ForeColor = textColor;
            }

            if (control is Guna2ComboBox guna2ComboBox)
            {
                if (isDarkMode)
                {
                    guna2ComboBox.BackColor = Color.FromArgb(40, 40, 40);
                    guna2ComboBox.ForeColor = Color.White;
                    guna2ComboBox.BorderColor = Color.FromArgb(80, 80, 80);
                    guna2ComboBox.FillColor = Color.FromArgb(50, 50, 50);
                    guna2ComboBox.HoverState.FillColor = Color.FromArgb(60, 60, 60);
                    guna2ComboBox.HoverState.BorderColor = Color.FromArgb(255, 150, 0);
                }
                else
                {
                    guna2ComboBox.BackColor = Color.White;
                    guna2ComboBox.ForeColor = Color.Black;
                    guna2ComboBox.BorderColor = Color.FromArgb(200, 200, 200);
                    guna2ComboBox.FillColor = Color.FromArgb(245, 245, 245);
                    guna2ComboBox.HoverState.FillColor = Color.FromArgb(230, 230, 230);
                    guna2ComboBox.HoverState.BorderColor = Color.FromArgb(0, 120, 215);
                }
            }

            if (control is Guna2TextBox guna2TextBox)
            {
                if (isDarkMode)
                {
                    guna2TextBox.ForeColor = Color.White;
                    guna2TextBox.FillColor = Color.FromArgb(26, 27, 27);
                    guna2TextBox.BorderColor = Color.FromArgb(80, 80, 80);
                    guna2TextBox.PlaceholderForeColor = Color.FromArgb(150, 150, 150);
                }
                else
                {
                    guna2TextBox.ForeColor = Color.Black;
                    guna2TextBox.FillColor = Color.FromArgb(245, 245, 245);
                    guna2TextBox.BorderColor = Color.FromArgb(200, 200, 200);
                    guna2TextBox.PlaceholderForeColor = Color.Gray;
                }
            }

            if (control is Guna2Button guna2Button)
            {
                guna2Button.ForeColor = textColor;
                guna2Button.BorderColor = borderColor;
                if (guna2Button.Name.StartsWith("Tab")) return;
                if (guna2Button != activeButton)
                {
                    if (guna2Button == AttachButton) guna2Button.FillColor = isDarkMode ? Color.FromArgb(255, 150, 0) : Color.FromArgb(255, 200, 150);
                    else if (guna2Button == ExecuteButton) guna2Button.FillColor = isDarkMode ? Color.FromArgb(0, 255, 125) : Color.FromArgb(100, 255, 175);
                    else if (guna2Button == IYExecute || guna2Button == OrcaExecute) guna2Button.FillColor = isDarkMode ? Color.FromArgb(0, 255, 125) : Color.FromArgb(100, 255, 175);
                    else if (guna2Button == Minimize || guna2Button == Exit) guna2Button.FillColor = isDarkMode ? Color.FromArgb(35, 35, 35) : Color.FromArgb(215, 215, 215);
                    //else guna2Button.FillColor = isDarkMode ? Color.FromArgb(40, 40, 40) : Color.FromArgb(255, 230, 230, 230);
                }
            }

            if (control is Guna2TrackBar gunaTrackBar)
            {
                gunaTrackBar.FillColor = isDarkMode ? Color.FromArgb(193, 200, 207) : Color.FromArgb(50, 50, 50);
                gunaTrackBar.ThumbColor = isDarkMode ? Color.FromArgb(255, 150, 0) : Color.FromArgb(0, 150, 136);
            }

            if (control is Guna2Panel guna2Panel)
            {
                guna2Panel.BorderColor = borderColor;

                if (guna2Panel == UpperPanel) guna2Panel.FillColor = isDarkMode ? Color.FromArgb(28, 28, 28) : Color.FromArgb(200, 200, 200);
            }

            if (control.HasChildren) foreach (Control childControl in control.Controls) ApplyBaseTheme(childControl, backgroundColor, textColor, borderColor, isDarkMode, activeButton);
        }

        private void ApplyTheme(Color backgroundColor, Color textColor, Color borderColor, bool isDarkMode)
        {
            BackColor = backgroundColor;

            foreach (Control control in Controls) ApplyBaseTheme(control, backgroundColor, textColor, borderColor, isDarkMode);
        }

        private int TabCount = 0;

        private void CreateTab_Click(object sender, EventArgs e) => ShowTabNamingOverlay();

        private void ShowTabNamingOverlay()
        {
            var overlayPanel = new Guna2Panel
            {
                Size = new Size(ClientSize.Width, ClientSize.Height),
                BackColor = Color.FromArgb(20, 20, 20),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Location = new Point(0, 0),
                Name = "TabNamingOverlay"
            };

            var namingContainer = new Guna2Panel
            {
                Size = new Size(300, 150),
                BackColor = Color.Transparent,
                FillColor = DS.Theme == "Dark" ? Color.FromArgb(30, 30, 30) : Color.FromArgb(210, 210, 210),
                BorderRadius = 10,
                BorderColor = DS.Theme == "Dark" ? Color.FromArgb(50, 50, 50) : Color.FromArgb(180, 180, 180),
                BorderThickness = 2,
                Anchor = AnchorStyles.None,
                Location = new Point((ClientSize.Width - 300) / 2, (ClientSize.Height - 150) / 2),
                Name = "NamingContainer"
            };

            var label = new Label
            {
                Text = "Enter Tab Name",
                ForeColor = DS.Theme == "Dark" ? Color.White : Color.Black,
                BackColor = Color.Transparent,
                Location = new Point(5, 6),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Size = new Size(new Point(130, 30)),
                Cursor = Cursors.IBeam
            };

            var inputBox = new Guna2TextBox
            {
                PlaceholderText = "Tab Name...",
                Font = new Font("Segoe UI", 10),
                ForeColor = DS.Theme == "Dark" ? Color.White : Color.Black,
                PlaceholderForeColor = DS.Theme == "Dark" ? Color.FromArgb(150, 150, 150) : Color.FromArgb(40, 40, 40),
                FillColor = DS.Theme == "Dark" ? Color.FromArgb(40, 40, 40) : Color.FromArgb(220, 220, 220),
                BorderColor = DS.Theme == "Dark" ? Color.FromArgb(80, 80, 80) : Color.FromArgb(210, 210, 210),
                BorderRadius = 5,
                Size = new Size(260, 36),
                Location = new Point(20, 50)
            };

            var confirmButton = new Guna2Button
            {
                Text = "Confirm",
                ForeColor = Color.White,
                FillColor = Color.FromArgb(0, 150, 136),
                BorderRadius = 5,
                Size = new Size(100, 36),
                Location = new Point(100, 100),
                Animated = true,
                Cursor = Cursors.Hand
            };

            confirmButton.Click += (s, args) =>
            {
                var tabName = inputBox.Text.Trim();

                if (TabsHolder.Controls.OfType<Guna2Button>().Any(btn => btn.Text.Equals(tabName, StringComparison.OrdinalIgnoreCase)))
                {
                    ShowNotification($"Tab '{tabName}' already exists!", "Error", 3000, 9);
                    return;
                }

                if (string.IsNullOrWhiteSpace(tabName)) tabName = $"Tab {TabCount + 1}";
                CreateNewTab(tabName, true);
                Controls.Remove(overlayPanel);
            };

            namingContainer.Controls.Add(label);
            namingContainer.Controls.Add(inputBox);
            namingContainer.Controls.Add(confirmButton);

            overlayPanel.Controls.Add(namingContainer);
            Controls.Add(overlayPanel);
            overlayPanel.BringToFront();
        }

        private void RearrangeTabs()
        {
            int currentX = 3;
            foreach (Control control in TabsHolder.Controls)
            {
                if (control is Guna2Button tabButton && control.Name.StartsWith("Tab"))
                {
                    tabButton.Location = new Point(currentX, 3);
                    currentX += tabButton.Width + 5;

                    if (TabsHolder.Controls.IndexOf(control) < TabsHolder.Controls.Count - 1)
                    {
                        Guna2Separator separator = (Guna2Separator)TabsHolder.Controls[TabsHolder.Controls.IndexOf(control) + 1];
                        separator.Location = new Point(tabButton.Location.X, tabButton.Location.Y + tabButton.Height);
                    }
                }
            }

            CreateTab.Location = new Point(currentX + 2, 5);
        }

        private void ShowDeleteConfirmationDialog(string tabName, Guna2Button tabButton, Guna2Separator tabSeparator, Guna2Button deleteButton)
        {
            var overlayPanel = new Guna2Panel
            {
                Size = new Size(ClientSize.Width, ClientSize.Height),
                BackColor = Color.FromArgb(20, 20, 20),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
                Location = new Point(0, 0),
                Name = "DeleteConfirmationOverlay"
            };

            var confirmationContainer = new Guna2Panel
            {
                Size = new Size(300, 150),
                BackColor = Color.Transparent,
                FillColor = DS.Theme == "Dark" ? Color.FromArgb(30, 30, 30) : Color.FromArgb(210, 210, 210),
                BorderRadius = 10,
                BorderColor = DS.Theme == "Dark" ? Color.FromArgb(50, 50, 50) : Color.FromArgb(180, 180, 180),
                BorderThickness = 2,
                Anchor = AnchorStyles.None,
                Location = new Point((ClientSize.Width - 300) / 2, (ClientSize.Height - 150) / 2),
                Name = "ConfirmationContainer"
            };

            var label = new Label
            {
                Text = $"Are you sure you want to delete '{tabName}'?",
                ForeColor = DS.Theme == "Dark" ? Color.White : Color.Black,
                BackColor = Color.Transparent,
                Location = new Point(0, 6),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Size = new Size(new Point(confirmationContainer.Width, 40)),
                Cursor = Cursors.IBeam
            };

            var confirmButton = new Guna2Button
            {
                Text = "Yes",
                ForeColor = DS.Theme == "Dark" ? Color.White : Color.Black,
                FillColor = DS.Theme == "Dark" ? Color.FromArgb(40, 40, 40) : Color.FromArgb(200, 200, 200),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                BorderRadius = 5,
                Size = new Size(100, 36),
                Location = new Point(155, 100),
                Animated = true,
                Cursor = Cursors.Hand
            };

            var cancelButton = new Guna2Button
            {
                Text = "No",
                ForeColor = DS.Theme == "Dark" ? Color.White : Color.Black,
                FillColor = DS.Theme == "Dark" ? Color.FromArgb(40, 40, 40) : Color.FromArgb(200, 200, 200),
                Font = new Font("Segoe UI", 10, FontStyle.Bold),
                BorderRadius = 5,
                Size = new Size(100, 36),
                Location = new Point(45, 100),
                Animated = true,
                Cursor = Cursors.Hand
            };

            confirmButton.Click += (s, args) =>
            {
                AddLog($"Deleted {tabName}");
                string filePath = Path.Combine(Application.StartupPath, "bin", "tabs", $"{tabName}.lua");
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                Controls.Remove(overlayPanel);

                if (TabCount <= 1)
                {
                    ShowNotification("You cannot delete the last tab!", "Error", 3000, 8);
                    return;
                }

                TabsHolder.Controls.Remove(tabButton);
                TabsHolder.Controls.Remove(tabSeparator);
                TabsHolder.Controls.Remove(deleteButton);

                ShowNotification($"Tab '{tabName}' deleted!", "Info", 2000, 9);

                TabCount--;

                RearrangeTabs();

                if (TabCount > 0)
                {
                    Guna2Button lastTabButton = null;
                    for (int i = TabsHolder.Controls.Count - 1; i >= 0; i--)
                    {
                        if (TabsHolder.Controls[i] is Guna2Button && TabsHolder.Controls[i].Name.StartsWith("Tab"))
                        {
                            lastTabButton = (Guna2Button)TabsHolder.Controls[i];
                            break;
                        }
                    }

                    Guna2Separator lastTabSeparator = null;
                    if (lastTabButton != null)
                    {
                        int lastTabIndex = TabsHolder.Controls.IndexOf(lastTabButton);
                        if (lastTabIndex >= 0 && lastTabIndex < TabsHolder.Controls.Count - 1)
                        {
                            lastTabSeparator = (Guna2Separator)TabsHolder.Controls[lastTabIndex + 1];
                        }
                    }

                    if (lastTabSeparator != null && lastTabButton != null)
                    {
                        string lastTabName = lastTabButton.Text;
                        SelectTab(lastTabName, lastTabSeparator, lastTabButton);
                    }
                    else
                    {
                        SetText("");
                        selectedTab = "";
                    }
                }
            };

            cancelButton.Click += (s, args) => Controls.Remove(overlayPanel);

            confirmationContainer.Controls.Add(label);
            confirmationContainer.Controls.Add(confirmButton);
            confirmationContainer.Controls.Add(cancelButton);

            overlayPanel.Controls.Add(confirmationContainer);
            Controls.Add(overlayPanel);
            overlayPanel.BringToFront();
        }

        private async void SelectTab(string tabName, Guna2Separator newTabSeparator, Guna2Button newTabButton)
        {
            if (!string.IsNullOrEmpty(selectedTab))
            {
                await SaveDataAsync();
            }

            foreach (Control control in TabsHolder.Controls)
            {
                if (control is Guna2Separator separator && control.Name.StartsWith("Tab") && control != newTabSeparator)
                {
                    separator.FillColor = DS.Theme == "Dark" ? Color.FromArgb(150, 150, 150) : Color.FromArgb(255, 255, 255);
                }
            }

            newTabSeparator.FillColor = DS.Theme == "Dark" ? Color.FromArgb(255, 255, 255) : Color.FromArgb(60, 60, 60);

            string filePath = Path.Combine(Application.StartupPath, "bin", "tabs", $"{tabName}.lua");

            if (File.Exists(filePath)) SetText(File.ReadAllText(filePath));

            selectedTab = tabName;
            selectedTabButton = newTabButton;
        }

        private async void CreateNewTab(string tabName, bool selectNewTab = false)
        {
            AddLog($"Created {tabName}");
            int maxWidth = TabsHolder.Width;

            int totalTabWidth = 0;
            foreach (Control control in TabsHolder.Controls) if (control is Guna2Button && control.Name.StartsWith("Tab")) totalTabWidth += control.Width + 5;

            int tabWidth = 120;
            int padding = 30;

            var font = new Font("Segoe UI", 9.75F, FontStyle.Bold);
            int textWidth = TextRenderer.MeasureText(tabName, font).Width + padding;

            if (textWidth > tabWidth)
            {
                tabName = TruncateText(tabName, font, tabWidth - padding);
                textWidth = tabWidth;
            }

            if (totalTabWidth + textWidth + CreateTab.Width + 7 > maxWidth)
            {
                ShowNotification("No more tabs can fit!", "Error", 5000, 9);

                return;
            }

            int newTabX = totalTabWidth + 3;

            TabCount++;

            Guna2Button newTabButton = new Guna2Button
            {
                Animated = true,
                BackColor = Color.Transparent,
                BorderRadius = 6,
                FillColor = Color.Transparent,
                BorderColor = Color.FromArgb(40, 40, 40),
                BorderThickness = 1,
                Font = font,
                ForeColor = DS.Theme == "Dark" ? Color.White : Color.Black,
                Image = Properties.Resources.TabIcon,
                ImageSize = new Size(18, 18),
                Location = new Point(newTabX, 3),
                Name = $"Tab{TabCount}Button",
                Size = new Size(textWidth + 30, 31),
                Text = tabName,
                TextAlign = HorizontalAlignment.Center,
                ImageAlign = HorizontalAlignment.Center,
                TextOffset = new Point(-7, 0),
                ImageOffset = new Point(-8, 0),
                Cursor = Cursors.Hand
            };

            Guna2Button deleteTabButton = new Guna2Button
            {
                Text = "X",
                BorderRadius = 6,
                Animated = true,
                Size = new Size(32, 25),
                Dock = DockStyle.Right,
                FillColor = Color.Transparent,
                BackColor = Color.Transparent,
                ForeColor = Color.FromArgb(150, 150, 150),
                Name = $"Delete{TabCount}Button",
                Cursor = Cursors.Hand
            };

            Guna2Separator newTabSeparator = new Guna2Separator
            {
                Location = new Point(newTabX, newTabButton.Location.Y + newTabButton.Height),
                Name = $"Tab{TabCount}Separator",
                Size = new Size(newTabButton.Width, 2),
                FillColor = DS.Theme == "Dark" ? Color.FromArgb(150, 150, 150) : Color.FromArgb(255, 255, 255),
            };

            string filePath = Path.Combine(Application.StartupPath, "bin", "tabs", $"{tabName}.lua");

            newTabButton.Click += (s, args) => SelectTab(tabName, newTabSeparator, newTabButton);

            if (selectNewTab) SelectTab(tabName, newTabSeparator, newTabButton);

            deleteTabButton.Click += (s, args) => ShowDeleteConfirmationDialog(tabName, newTabButton, newTabSeparator, deleteTabButton);

            TabsHolder.Controls.Add(newTabButton);
            TabsHolder.Controls.Add(newTabSeparator);
            newTabButton.Controls.Add(deleteTabButton);

            RearrangeTabs();

            SetText("print(\"Hello world!\")");

            await SaveDataAsync();
        }

        private string TruncateText(string text, Font font, int maxWidth)
        {
            while (TextRenderer.MeasureText(text + "...", font).Width > maxWidth) text = text.Substring(0, text.Length - 1);

            return text + "...";
        }

        private async void SaveFile_Click(object sender, EventArgs e)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog()
            {
                Filter = "Lua Files (*.lua)|*.lua|Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                Title = "Save the monaco editor's text",
            };

            if (saveFileDialog.ShowDialog() == DialogResult.OK)
            {
                string filePath = saveFileDialog.FileName;
                string fileName = Path.GetFileName(filePath);

                try
                {
                    using (StreamWriter writer = new StreamWriter(filePath))
                    {
                        string text = await GetText();
                        await writer.WriteAsync(text);
                        AddLog($"Saved current text to {fileName}");
                    }
                }
                catch (Exception) { }
            }
        }


        private void OpenFile_Click(object sender, EventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();

            openFileDialog.Filter = "Lua Files (*.lua)|*.lua|Text Files (*.txt)|*.txt|All Files (*.*)|*.*";
            openFileDialog.Title = "Open a file";

            if (openFileDialog.ShowDialog() == DialogResult.OK)
            {
                string filePath = openFileDialog.FileName;
                string fileName = Path.GetFileName(filePath);
                string fileContent = File.ReadAllText(filePath);
                SetText(fileContent);

                AddLog($"{fileName} opened with open file");
            }
        }

        private void APIDropdown_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (!recentlyLoaded)
            {
                selectedApi = APIDropdown.SelectedItem.ToString();
                DS.API = selectedApi;
                DS.Save();

                Lynra.KillRoblox();
                MessageBox.Show("Restart Lynra for changes to take effect", "Lynra");
                this.Close();
            }
        }

        private void GeneralSBtn_Click(object sender, EventArgs e) => ShowPanel(GeneralSPanel, GeneralSBtn, true);

        private void UISBtn_Click(object sender, EventArgs e) => ShowPanel(UISPanel, UISBtn, true);

        private void OtherSBtn_Click(object sender, EventArgs e) => ShowPanel(OtherSPanel, OtherSBtn, true);

        private void BuiltInScBtn_Click(object sender, EventArgs e) => ShowPanel(BuiltInScPanel, BuiltInScBtn, false, true);

        private void CloudScBtn_Click(object sender, EventArgs e) => ShowPanel(CloudScPanel, CloudScBtn, false, true);

        private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                if (_cancellationTokenSource != null)
                {
                    _cancellationTokenSource.Cancel();
                    _cancellationTokenSource = null;   
                }

                _cancellationTokenSource = new CancellationTokenSource();
                string query = SearchBox.Text;

                try
                {
                    var scripts = await ScriptsMethods.GetScriptsFromQueryAsync(query, 15);

                    for (int i = CloudScPanel.Controls.Count - 1; i >= 0; i--)
                    {
                        var control = CloudScPanel.Controls[i];
                        if (control != SearchBox) CloudScPanel.Controls.RemoveAt(i);
                    }

                    int xOffset = 10;
                    int yOffset = 65;
                    int panelWidth = (CloudScPanel.ClientSize.Width - (xOffset * 5)) / 2;
                    int panelHeight = 120;
                    int columns = 2;

                    for (int i = 0; i < scripts.Count; i++)
                    {
                        var script = scripts[i];
                        var scriptPanel = new Guna2Panel
                        {
                            Width = panelWidth,
                            Height = panelHeight,
                            BorderRadius = 10,
                            BorderThickness = 1,
                            BorderColor = Color.LightGray,
                            FillColor = Color.Transparent,
                            Margin = new Padding(5),
                            BackgroundImageLayout = ImageLayout.Zoom,
                            BackgroundImage = Properties.Resources.PlaceholderImage
                        };

                        int row = i / columns;
                        int column = i % columns;

                        try
                        {
                            var imageUrl = script.Game.Thumbnail.Replace("https://scriptblox.com", "");
                            scriptPanel.BackgroundImage = await LoadImageAsync(imageUrl);
                        }
                        catch
                        {
                            scriptPanel.BackgroundImage = Properties.Resources.PlaceholderImage;
                        }

                        scriptPanel.Top = yOffset + row * (panelHeight + 15);
                        scriptPanel.Left = xOffset + column * (panelWidth + xOffset);

                        var executeButton = new Guna2Button
                        {
                            Text = "Execute",
                            Font = new Font("Segoe UI", 10F, FontStyle.Regular),
                            ForeColor = DS.Theme == "Dark" ? Color.White : Color.Black,
                            FillColor = DS.Theme == "Dark" ? Color.FromArgb(40, 40, 40) : Color.FromArgb(255, 200, 150),
                            BorderRadius = 10,
                            Size = new Size(80, 30),
                            Location = new Point(scriptPanel.Width - 90, scriptPanel.Height - 42),
                            Animated = true
                        };

                        executeButton.Click += (v1, v2) =>
                        {
                            if (DS.API == "lynraApi") Execute(script.ExecutingScript);
                            else if (DS.API == "Forlorn") Api.ExecuteScript(script.ExecutingScript);
                        };

                        scriptPanel.Controls.Add(executeButton);
                        CloudScPanel.Controls.Add(scriptPanel);
                    }
                }
                catch (OperationCanceledException) { } // nuh uh
            }
        }

        private async Task<Image> LoadImageAsync(string imageUrl)
        {
            try
            {
                if (Uri.IsWellFormedUriString(imageUrl, UriKind.Absolute))
                {
                    using (var client = new HttpClient())
                    {
                        var stream = await client.GetStreamAsync(imageUrl);
                        return Image.FromStream(stream);
                    }
                }
                else if (File.Exists(imageUrl)) return Image.FromFile(imageUrl);
            }
            catch
            {
                return Properties.Resources.PlaceholderImage;
            }

            return Properties.Resources.PlaceholderImage;
        }

        private void MainPanel_Paint(object sender, PaintEventArgs e)
        {

        }
    }
}