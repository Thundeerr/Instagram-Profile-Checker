using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace InstagramProfileChecker
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            // .NET Framework 4 defaults to legacy TLS on some systems. RapidAPI
            // requires TLS 1.2, whose enum value is 3072 on older frameworks.
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    internal sealed class MainForm : Form
    {
        private static readonly Color Background = Color.FromArgb(18, 20, 27);
        private static readonly Color Surface = Color.FromArgb(28, 31, 41);
        private static readonly Color SurfaceLight = Color.FromArgb(39, 43, 56);
        private static readonly Color TextPrimary = Color.FromArgb(240, 242, 247);
        private static readonly Color TextMuted = Color.FromArgb(160, 166, 180);
        private static readonly Color Purple = Color.FromArgb(145, 92, 246);
        private static readonly Color Green = Color.FromArgb(52, 211, 153);
        private static readonly Color Red = Color.FromArgb(248, 113, 113);
        private static readonly Color Amber = Color.FromArgb(251, 191, 36);

        private readonly string rootDirectory;
        private readonly string envPath;
        private readonly string statePath;
        private readonly string eventsPath;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private readonly BackgroundWorker checker = new BackgroundWorker();
        private readonly Timer timer = new Timer();

        private Label usernameLabel;
        private Label statusLabel;
        private Label lastCheckLabel;
        private Label countdownLabel;
        private Label configLabel;
        private Button checkButton;
        private Button demoButton;
        private DataGridView ordersGrid;
        private RichTextBox activityLog;

        private string apiKey = "";
        private string username = "instagram";
        private int intervalMinutes = 30;
        private int simulatedQuantity = 1000;
        private bool configIsValid;
        private bool? previousPrivate;
        private DateTime nextCheck;

        public MainForm()
        {
            rootDirectory = Path.GetDirectoryName(Application.ExecutablePath);
            envPath = Path.Combine(rootDirectory, ".env");
            statePath = Path.Combine(rootDirectory, "state.json");
            eventsPath = Path.Combine(rootDirectory, "events.jsonl");

            BuildInterface();
            LoadConfiguration();
            LoadPreviousState();
            LoadOrderHistory();
            Shown += delegate
            {
                ordersGrid.ClearSelection();
                ordersGrid.CurrentCell = null;
            };

            checker.DoWork += CheckerDoWork;
            checker.RunWorkerCompleted += CheckerCompleted;

            timer.Interval = 1000;
            timer.Tick += TimerTick;
            timer.Start();
            nextCheck = DateTime.Now.AddSeconds(3);
            UpdateCountdown();
        }

        private void BuildInterface()
        {
            Text = "Instagram Public Monitor";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(840, 650);
            ClientSize = new Size(980, 740);
            BackColor = Background;
            ForeColor = TextPrimary;
            Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point);
            AutoScaleMode = AutoScaleMode.Dpi;

            TableLayoutPanel root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.Padding = new Padding(22);
            root.BackColor = Background;
            root.ColumnCount = 1;
            root.RowCount = 5;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 88F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 166F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 58F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 42F));
            Controls.Add(root);

            Panel header = new Panel();
            header.Dock = DockStyle.Fill;
            Label title = NewLabel("Instagram Public Monitor", 22F, FontStyle.Bold, TextPrimary);
            title.Location = new Point(0, 5);
            title.AutoSize = true;
            Label subtitle = NewLabel("Lokaler Privacy-Check via RapidAPI", 9.5F, FontStyle.Regular, TextMuted);
            subtitle.Location = new Point(2, 44);
            subtitle.AutoSize = true;
            configLabel = NewLabel("Konfiguration wird geladen ...", 9F, FontStyle.Regular, Amber);
            configLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            configLabel.AutoSize = true;
            configLabel.Location = new Point(700, 15);
            header.Controls.Add(title);
            header.Controls.Add(subtitle);
            header.Controls.Add(configLabel);
            header.Resize += delegate
            {
                configLabel.Left = Math.Max(0, header.ClientSize.Width - configLabel.Width);
            };
            root.Controls.Add(header, 0, 0);

            Panel statusCard = NewCard();
            statusCard.Padding = new Padding(24, 18, 24, 18);
            usernameLabel = NewLabel("@instagram", 13F, FontStyle.Bold, TextMuted);
            usernameLabel.Location = new Point(24, 19);
            usernameLabel.AutoSize = true;
            statusLabel = NewLabel("NOCH NICHT GEPRUEFT", 25F, FontStyle.Bold, Amber);
            statusLabel.Location = new Point(22, 51);
            statusLabel.AutoSize = true;
            lastCheckLabel = NewLabel("Letzter Check: -", 9.5F, FontStyle.Regular, TextMuted);
            lastCheckLabel.Location = new Point(25, 104);
            lastCheckLabel.AutoSize = true;
            countdownLabel = NewLabel("Naechster Check: -", 10F, FontStyle.Bold, Purple);
            countdownLabel.AutoSize = true;
            countdownLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            countdownLabel.Location = new Point(650, 24);
            statusCard.Controls.Add(usernameLabel);
            statusCard.Controls.Add(statusLabel);
            statusCard.Controls.Add(lastCheckLabel);
            statusCard.Controls.Add(countdownLabel);
            statusCard.Resize += delegate
            {
                countdownLabel.Left = Math.Max(25, statusCard.ClientSize.Width - countdownLabel.Width - 24);
            };
            root.Controls.Add(statusCard, 0, 1);

            TableLayoutPanel actions = new TableLayoutPanel();
            actions.Dock = DockStyle.Fill;
            actions.Padding = new Padding(0, 11, 0, 9);
            actions.BackColor = Background;
            actions.ColumnCount = 4;
            actions.RowCount = 1;
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            checkButton = NewButton("Jetzt prüfen", Purple);
            checkButton.Click += delegate { StartCheck(); };
            demoButton = NewButton("Demo auslösen", SurfaceLight);
            demoButton.Click += delegate { CreateMockOrder(true); };
            Button settingsButton = NewButton("Einstellungen", SurfaceLight);
            settingsButton.Click += delegate { ShowSettingsDialog(); };
            Button folderButton = NewButton("Ordner öffnen", SurfaceLight);
            folderButton.Click += delegate { Process.Start("explorer.exe", rootDirectory); };
            Button[] actionButtons = new Button[] { checkButton, demoButton, settingsButton, folderButton };
            for (int i = 0; i < actionButtons.Length; i++)
            {
                actionButtons[i].Dock = DockStyle.Fill;
                actionButtons[i].Margin = new Padding(i == 0 ? 0 : 5, 0, i == actionButtons.Length - 1 ? 0 : 5, 0);
                actions.Controls.Add(actionButtons[i], i, 0);
            }
            root.Controls.Add(actions, 0, 2);

            Panel ordersCard = NewCard();
            ordersCard.Padding = new Padding(16);
            Label ordersTitle = NewLabel("Simulierte Panel-Orders", 11F, FontStyle.Bold, TextPrimary);
            ordersTitle.Dock = DockStyle.Top;
            ordersTitle.Height = 34;
            ordersGrid = new DataGridView();
            ordersGrid.Dock = DockStyle.Fill;
            ordersGrid.ReadOnly = true;
            ordersGrid.AllowUserToAddRows = false;
            ordersGrid.AllowUserToDeleteRows = false;
            ordersGrid.AllowUserToResizeRows = false;
            ordersGrid.MultiSelect = false;
            ordersGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            ordersGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            ordersGrid.RowHeadersVisible = false;
            ordersGrid.BorderStyle = BorderStyle.None;
            ordersGrid.BackgroundColor = Surface;
            ordersGrid.GridColor = SurfaceLight;
            ordersGrid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            ordersGrid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            ordersGrid.EnableHeadersVisualStyles = false;
            ordersGrid.ColumnHeadersHeight = 36;
            ordersGrid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            ordersGrid.RowTemplate.Height = 34;
            ordersGrid.ScrollBars = ScrollBars.Vertical;
            ordersGrid.DefaultCellStyle.BackColor = Surface;
            ordersGrid.DefaultCellStyle.ForeColor = TextPrimary;
            ordersGrid.DefaultCellStyle.SelectionBackColor = Surface;
            ordersGrid.DefaultCellStyle.SelectionForeColor = TextPrimary;
            ordersGrid.DefaultCellStyle.Padding = new Padding(5, 0, 5, 0);
            ordersGrid.DefaultCellStyle.Font = new Font("Segoe UI", 9.2F);
            ordersGrid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(31, 34, 45);
            ordersGrid.ColumnHeadersDefaultCellStyle.BackColor = SurfaceLight;
            ordersGrid.ColumnHeadersDefaultCellStyle.ForeColor = TextMuted;
            ordersGrid.ColumnHeadersDefaultCellStyle.SelectionBackColor = SurfaceLight;
            ordersGrid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9F, FontStyle.Bold);
            ordersGrid.ColumnHeadersDefaultCellStyle.Padding = new Padding(5, 0, 5, 0);
            AddGridColumn("order_id", "Order-ID", 23F);
            AddGridColumn("target", "Ziel", 34F);
            AddGridColumn("quantity", "Menge", 12F);
            AddGridColumn("status", "Status", 12F);
            AddGridColumn("created_at", "Zeit", 19F);
            ordersCard.Controls.Add(ordersGrid);
            ordersCard.Controls.Add(ordersTitle);
            root.Controls.Add(ordersCard, 0, 3);

            Panel logCard = NewCard();
            logCard.Padding = new Padding(16);
            Label logTitle = NewLabel("Aktivitaet", 11F, FontStyle.Bold, TextPrimary);
            logTitle.Dock = DockStyle.Top;
            logTitle.Height = 30;
            activityLog = new RichTextBox();
            activityLog.Dock = DockStyle.Fill;
            activityLog.ReadOnly = true;
            activityLog.BorderStyle = BorderStyle.None;
            activityLog.BackColor = Surface;
            activityLog.ForeColor = TextMuted;
            activityLog.Font = new Font("Consolas", 9.5F);
            activityLog.ScrollBars = RichTextBoxScrollBars.None;
            activityLog.DetectUrls = false;
            logCard.Controls.Add(activityLog);
            logCard.Controls.Add(logTitle);
            root.Controls.Add(logCard, 0, 4);
        }

        private static Panel NewCard()
        {
            Panel panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.Margin = new Padding(0, 4, 0, 8);
            panel.BackColor = Surface;
            return panel;
        }

        private static Label NewLabel(string text, float size, FontStyle style, Color color)
        {
            Label label = new Label();
            label.Text = text;
            label.Font = new Font("Segoe UI", size, style, GraphicsUnit.Point);
            label.ForeColor = color;
            label.BackColor = Color.Transparent;
            return label;
        }

        private static Button NewButton(string text, Color color)
        {
            Button button = new Button();
            button.Text = text;
            button.Width = 150;
            button.Height = 39;
            button.Margin = new Padding(0, 0, 10, 0);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = color;
            button.ForeColor = TextPrimary;
            button.Cursor = Cursors.Hand;
            button.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            return button;
        }

        private void AddGridColumn(string name, string header, float fillWeight)
        {
            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
            column.Name = name;
            column.HeaderText = header;
            column.FillWeight = fillWeight;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            ordersGrid.Columns.Add(column);
        }

        private void LoadConfiguration()
        {
            Dictionary<string, string> values = ReadEnvFile(envPath);
            values.TryGetValue("RAPIDAPI_KEY", out apiKey);
            string configuredUsername;
            if (values.TryGetValue("INSTAGRAM_USERNAME", out configuredUsername))
                username = configuredUsername.Trim().TrimStart('@');

            string rawInterval;
            int parsedInterval;
            if (values.TryGetValue("CHECK_INTERVAL_MINUTES", out rawInterval) &&
                int.TryParse(rawInterval, out parsedInterval) && parsedInterval > 0)
                intervalMinutes = parsedInterval;

            string rawQuantity;
            int parsedQuantity;
            if (values.TryGetValue("PANEL_SIMULATED_QUANTITY", out rawQuantity) &&
                int.TryParse(rawQuantity, out parsedQuantity) && parsedQuantity > 0)
                simulatedQuantity = parsedQuantity;

            configIsValid = !string.IsNullOrWhiteSpace(apiKey) && apiKey != "dein_rapidapi_key";
            usernameLabel.Text = "@" + username;
            configLabel.Text = configIsValid ? "RapidAPI bereit" : ".env / RapidAPI-Key fehlt";
            configLabel.ForeColor = configIsValid ? Green : Amber;
            checkButton.Enabled = configIsValid;
            AppendLog(configIsValid
                ? "Konfiguration geladen. API-Key ist lokal vorhanden."
                : "Kein API-Key gefunden. Demo-Modus ist weiterhin verfuegbar.");
        }

        private static Dictionary<string, string> ReadEnvFile(string path)
        {
            Dictionary<string, string> result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path)) return result;
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || !line.Contains("=")) continue;
                int separator = line.IndexOf('=');
                string key = line.Substring(0, separator).Trim();
                string value = line.Substring(separator + 1).Trim().Trim('"', '\'');
                result[key] = value;
            }
            return result;
        }

        private void ShowSettingsDialog()
        {
            Form dialog = new Form();
            dialog.Text = "Lokale Einstellungen";
            dialog.StartPosition = FormStartPosition.CenterParent;
            dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
            dialog.MaximizeBox = false;
            dialog.MinimizeBox = false;
            dialog.ShowInTaskbar = false;
            dialog.ClientSize = new Size(520, 390);
            dialog.BackColor = Background;
            dialog.ForeColor = TextPrimary;
            dialog.Font = new Font("Segoe UI", 10F);

            Label heading = NewLabel("RapidAPI lokal einrichten", 16F, FontStyle.Bold, TextPrimary);
            heading.Location = new Point(28, 22);
            heading.AutoSize = true;
            dialog.Controls.Add(heading);

            Label hint = NewLabel(
                "Der Key wird maskiert und ausschließlich lokal\nin der .env-Datei gespeichert.",
                9F,
                FontStyle.Regular,
                TextMuted);
            hint.Location = new Point(30, 57);
            hint.Size = new Size(455, 38);
            hint.AutoSize = false;
            dialog.Controls.Add(hint);

            Label keyLabel = NewLabel("RapidAPI-Key", 9F, FontStyle.Bold, TextMuted);
            keyLabel.Location = new Point(30, 101);
            keyLabel.AutoSize = true;
            dialog.Controls.Add(keyLabel);

            TextBox keyBox = NewSettingsTextBox();
            keyBox.Location = new Point(30, 124);
            keyBox.Width = 460;
            keyBox.UseSystemPasswordChar = true;
            keyBox.Text = apiKey ?? "";
            dialog.Controls.Add(keyBox);

            CheckBox showKey = new CheckBox();
            showKey.Text = "Key anzeigen";
            showKey.Location = new Point(30, 158);
            showKey.AutoSize = true;
            showKey.ForeColor = TextMuted;
            showKey.BackColor = Background;
            showKey.CheckedChanged += delegate { keyBox.UseSystemPasswordChar = !showKey.Checked; };
            dialog.Controls.Add(showKey);

            Label usernameSettingsLabel = NewLabel("Instagram-Benutzername", 9F, FontStyle.Bold, TextMuted);
            usernameSettingsLabel.Location = new Point(30, 193);
            usernameSettingsLabel.AutoSize = true;
            dialog.Controls.Add(usernameSettingsLabel);

            TextBox usernameBox = NewSettingsTextBox();
            usernameBox.Location = new Point(30, 216);
            usernameBox.Width = 215;
            usernameBox.Text = username;
            dialog.Controls.Add(usernameBox);

            Label intervalLabel = NewLabel("Intervall (Min.)", 9F, FontStyle.Bold, TextMuted);
            intervalLabel.Location = new Point(270, 193);
            intervalLabel.AutoSize = true;
            dialog.Controls.Add(intervalLabel);

            NumericUpDown intervalBox = NewSettingsNumber();
            intervalBox.Location = new Point(270, 216);
            intervalBox.Width = 220;
            intervalBox.Minimum = 1;
            intervalBox.Maximum = 10080;
            intervalBox.Value = Math.Max(1, Math.Min(10080, intervalMinutes));
            dialog.Controls.Add(intervalBox);

            Label quantityLabel = NewLabel("Demo-Menge", 9F, FontStyle.Bold, TextMuted);
            quantityLabel.Location = new Point(30, 260);
            quantityLabel.AutoSize = true;
            dialog.Controls.Add(quantityLabel);

            NumericUpDown quantityBox = NewSettingsNumber();
            quantityBox.Location = new Point(30, 283);
            quantityBox.Maximum = 10000000;
            quantityBox.Value = Math.Max(1, Math.Min(10000000, simulatedQuantity));
            dialog.Controls.Add(quantityBox);

            Button saveButton = NewButton("Speichern", Purple);
            saveButton.Location = new Point(320, 325);
            saveButton.Width = 170;
            saveButton.Click += delegate
            {
                string newKey = keyBox.Text.Trim();
                string newUsername = usernameBox.Text.Trim().TrimStart('@');
                if (string.IsNullOrWhiteSpace(newKey))
                {
                    MessageBox.Show(dialog, "Bitte den RapidAPI-Key eingeben.", "Fehlender Key",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                if (string.IsNullOrWhiteSpace(newUsername))
                {
                    MessageBox.Show(dialog, "Bitte einen Instagram-Benutzernamen eingeben.", "Fehlender Benutzername",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string[] lines = new string[]
                {
                    "RAPIDAPI_KEY=" + newKey,
                    "INSTAGRAM_USERNAME=" + newUsername,
                    "CHECK_INTERVAL_MINUTES=" + Convert.ToInt32(intervalBox.Value),
                    "PANEL_SIMULATED_QUANTITY=" + Convert.ToInt32(quantityBox.Value)
                };
                File.WriteAllLines(envPath, lines, new UTF8Encoding(false));
                dialog.DialogResult = DialogResult.OK;
                dialog.Close();
            };
            dialog.AcceptButton = saveButton;
            dialog.Controls.Add(saveButton);

            Button cancelButton = NewButton("Abbrechen", SurfaceLight);
            cancelButton.Location = new Point(140, 325);
            cancelButton.Width = 160;
            cancelButton.DialogResult = DialogResult.Cancel;
            dialog.CancelButton = cancelButton;
            dialog.Controls.Add(cancelButton);

            dialog.Shown += delegate
            {
                usernameBox.Focus();
                usernameBox.SelectionStart = usernameBox.TextLength;
                usernameBox.SelectionLength = 0;
            };

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                LoadConfiguration();
                nextCheck = DateTime.Now.AddSeconds(3);
                AppendLog("Lokale Einstellungen gespeichert. Der Key wird nicht angezeigt.");
            }
            dialog.Dispose();
        }

        private static TextBox NewSettingsTextBox()
        {
            TextBox box = new TextBox();
            box.Height = 29;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.BackColor = SurfaceLight;
            box.ForeColor = TextPrimary;
            box.Font = new Font("Segoe UI", 10F);
            return box;
        }

        private static NumericUpDown NewSettingsNumber()
        {
            NumericUpDown number = new NumericUpDown();
            number.Width = 190;
            number.Height = 29;
            number.BackColor = SurfaceLight;
            number.ForeColor = TextPrimary;
            number.BorderStyle = BorderStyle.FixedSingle;
            number.ThousandsSeparator = true;
            return number;
        }

        private void LoadPreviousState()
        {
            if (!File.Exists(statePath)) return;
            try
            {
                Dictionary<string, object> state = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(statePath));
                object stateUsername;
                object privateValue;
                if (state.TryGetValue("username", out stateUsername) &&
                    string.Equals(Convert.ToString(stateUsername), username, StringComparison.OrdinalIgnoreCase) &&
                    state.TryGetValue("is_private", out privateValue))
                {
                    previousPrivate = Convert.ToBoolean(privateValue);
                    SetStatus(previousPrivate.Value);
                    object checkedAt;
                    if (state.TryGetValue("checked_at", out checkedAt))
                        lastCheckLabel.Text = "Letzter Check: " + Convert.ToString(checkedAt);
                }
            }
            catch (Exception ex)
            {
                AppendLog("Statusdatei konnte nicht gelesen werden: " + ex.Message);
            }
        }

        private void LoadOrderHistory()
        {
            if (!File.Exists(eventsPath)) return;
            try
            {
                foreach (string line in File.ReadAllLines(eventsPath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    Dictionary<string, object> eventData = json.Deserialize<Dictionary<string, object>>(line);
                    object orderObject;
                    if (!eventData.TryGetValue("mock_order", out orderObject)) continue;
                    Dictionary<string, object> order = orderObject as Dictionary<string, object>;
                    if (order != null) AddOrderToList(order);
                }
            }
            catch (Exception ex)
            {
                AppendLog("Order-Verlauf konnte nicht gelesen werden: " + ex.Message);
            }
        }

        private void TimerTick(object sender, EventArgs e)
        {
            UpdateCountdown();
            if (configIsValid && !checker.IsBusy && DateTime.Now >= nextCheck)
                StartCheck();
        }

        private void UpdateCountdown()
        {
            TimeSpan remaining = nextCheck - DateTime.Now;
            if (remaining.TotalSeconds < 0) remaining = TimeSpan.Zero;
            countdownLabel.Text = string.Format("Naechster Check: {0:00}:{1:00}",
                (int)remaining.TotalMinutes, remaining.Seconds);
            countdownLabel.Left = Math.Max(25, countdownLabel.Parent.ClientSize.Width - countdownLabel.Width - 24);
        }

        private void StartCheck()
        {
            if (!configIsValid || checker.IsBusy) return;
            checkButton.Enabled = false;
            statusLabel.Text = "WIRD GEPRUEFT ...";
            statusLabel.ForeColor = Purple;
            AppendLog("Pruefe @" + username + " ...");
            checker.RunWorkerAsync();
        }

        private void CheckerDoWork(object sender, DoWorkEventArgs e)
        {
            e.Result = FetchProfileStatus();
        }

        private void CheckerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            checkButton.Enabled = configIsValid;
            nextCheck = DateTime.Now.AddMinutes(intervalMinutes);

            if (e.Error != null)
            {
                statusLabel.Text = "CHECK FEHLGESCHLAGEN";
                statusLabel.ForeColor = Red;
                AppendLog("Fehler: " + e.Error.Message);
                return;
            }

            bool isPrivate = (bool)e.Result;
            bool becamePublic = previousPrivate.HasValue && previousPrivate.Value && !isPrivate;
            previousPrivate = isPrivate;
            SetStatus(isPrivate);
            lastCheckLabel.Text = "Letzter Check: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
            SaveState(isPrivate);
            AppendLog("Antwort erhalten: is_private = " + isPrivate.ToString().ToLowerInvariant());

            if (becamePublic)
            {
                System.Media.SystemSounds.Exclamation.Play();
                CreateMockOrder(false);
                MessageBox.Show(
                    "@" + username + " ist jetzt oeffentlich. Eine Demo-Order wurde erzeugt.",
                    "Profil ist oeffentlich",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
        }

        private bool FetchProfileStatus()
        {
            string query = "username=" + Uri.EscapeDataString(username) +
                "&fields=" + Uri.EscapeDataString("status,username,is_private");
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(
                "https://instagram-looter2.p.rapidapi.com/profile2?" + query);
            request.Method = "GET";
            request.Accept = "application/json";
            request.Timeout = 20000;
            request.Headers.Add("x-rapidapi-key", apiKey);
            request.Headers.Add("x-rapidapi-host", "instagram-looter2.p.rapidapi.com");

            string responseText;
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream()))
                responseText = reader.ReadToEnd();

            Dictionary<string, object> data = json.Deserialize<Dictionary<string, object>>(responseText);
            object wrappedBody;
            if (data.TryGetValue("body", out wrappedBody))
            {
                Dictionary<string, object> wrapped = wrappedBody as Dictionary<string, object>;
                if (wrapped != null) data = wrapped;
            }

            object privateValue;
            if (!data.TryGetValue("is_private", out privateValue))
                throw new InvalidDataException("Das Feld 'is_private' fehlt in der API-Antwort.");
            return Convert.ToBoolean(privateValue);
        }

        private void SetStatus(bool isPrivate)
        {
            statusLabel.Text = isPrivate ? "PRIVAT" : "ÖFFENTLICH";
            statusLabel.ForeColor = isPrivate ? Red : Green;
        }

        private void SaveState(bool isPrivate)
        {
            Dictionary<string, object> state = new Dictionary<string, object>();
            state["username"] = username;
            state["is_private"] = isPrivate;
            state["checked_at"] = DateTime.UtcNow.ToString("o");
            File.WriteAllText(statePath, json.Serialize(state) + Environment.NewLine, Encoding.UTF8);
        }

        private void CreateMockOrder(bool manuallyTriggered)
        {
            Random random = new Random(Guid.NewGuid().GetHashCode());
            Dictionary<string, object> order = new Dictionary<string, object>();
            order["order_id"] = "JAP-DEMO-" + random.Next(100000, 999999);
            order["provider"] = "justanotherpanel";
            order["service"] = "Instagram Followers (Simulation)";
            order["target"] = "https://instagram.com/" + username;
            order["quantity"] = simulatedQuantity;
            order["status"] = "Pending";
            order["mode"] = "dry_run";
            order["created_at"] = DateTime.UtcNow.ToString("o");

            Dictionary<string, object> eventData = new Dictionary<string, object>();
            eventData["event"] = manuallyTriggered ? "manual_demo_order" : "profile_became_public";
            eventData["username"] = username;
            eventData["detected_at"] = DateTime.UtcNow.ToString("o");
            eventData["mode"] = "dry_run";
            eventData["mock_order"] = order;

            File.AppendAllText(eventsPath, json.Serialize(eventData) + Environment.NewLine, Encoding.UTF8);
            AddOrderToList(order);
            AppendLog("Demo-Order erzeugt: " + Convert.ToString(order["order_id"]));
        }

        private void AddOrderToList(Dictionary<string, object> order)
        {
            string createdAt = Value(order, "created_at");
            DateTime parsed;
            if (DateTime.TryParse(createdAt, out parsed)) createdAt = parsed.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");

            ordersGrid.Rows.Insert(0,
                Value(order, "order_id"),
                Value(order, "target"),
                Value(order, "quantity"),
                Value(order, "status"),
                createdAt);
            ordersGrid.ClearSelection();
            ordersGrid.CurrentCell = null;
            if (ordersGrid.Rows.Count > 100)
                ordersGrid.Rows.RemoveAt(ordersGrid.Rows.Count - 1);
        }

        private static string Value(Dictionary<string, object> source, string key)
        {
            object value;
            return source.TryGetValue(key, out value) ? Convert.ToString(value) : "";
        }

        private void AppendLog(string text)
        {
            if (activityLog == null) return;
            activityLog.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + text + Environment.NewLine);
            activityLog.SelectionStart = activityLog.TextLength;
            activityLog.ScrollToCaret();
        }
    }
}
