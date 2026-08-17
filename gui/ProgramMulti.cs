using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace InstagramProfileChecker
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    internal sealed class AccountProfile
    {
        public string Username;
        public bool Enabled = true;
        public bool? IsPrivate;
        public DateTime LastChecked = DateTime.MinValue;
        public DateTime NextCheck = DateTime.MinValue;
        public string LastError = "";
        public bool IsChecking;
    }

    internal sealed class CheckResult
    {
        public string Username;
        public bool IsPrivate;
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
        private readonly string accountsPath;
        private readonly string legacyStatePath;
        private readonly string eventsPath;
        private readonly JavaScriptSerializer json = new JavaScriptSerializer();
        private readonly List<AccountProfile> accounts = new List<AccountProfile>();
        private readonly Queue<string> scanQueue = new Queue<string>();
        private readonly HashSet<string> queuedAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly BackgroundWorker checker = new BackgroundWorker();
        private readonly Timer timer = new Timer();

        private DataGridView accountsGrid;
        private DataGridView ordersGrid;
        private Label configLabel;
        private Label accountCountLabel;
        private Label selectedUsernameLabel;
        private Label selectedStatusLabel;
        private Label selectedLastCheckLabel;
        private Label selectedNextCheckLabel;
        private RichTextBox activityLog;
        private Button checkSelectedButton;
        private Button checkAllButton;
        private Button demoButton;
        private Button removeAccountButton;

        private string apiKey = "";
        private int intervalMinutes = 30;
        private int simulatedQuantity = 1000;
        private int eventCooldownMinutes = 1440;
        private bool configIsValid;
        private bool rebuildingAccountGrid;

        public MainForm()
        {
            rootDirectory = Path.GetDirectoryName(Application.ExecutablePath);
            envPath = Path.Combine(rootDirectory, ".env");
            accountsPath = Path.Combine(rootDirectory, "accounts.json");
            legacyStatePath = Path.Combine(rootDirectory, "state.json");
            eventsPath = Path.Combine(rootDirectory, "events.jsonl");

            BuildInterface();
            LoadConfiguration();
            LoadAccounts();
            LoadOrderHistory();
            RefreshAccountGrid();

            checker.DoWork += CheckerDoWork;
            checker.RunWorkerCompleted += CheckerCompleted;
            timer.Interval = 1000;
            timer.Tick += TimerTick;
            timer.Start();

            Shown += delegate
            {
                if (accountsGrid.Rows.Count > 0)
                {
                    accountsGrid.Rows[0].Selected = true;
                    accountsGrid.CurrentCell = accountsGrid.Rows[0].Cells[1];
                }
                ordersGrid.ClearSelection();
                ordersGrid.CurrentCell = null;
                UpdateSelectedAccountCard();
            };
        }

        private void BuildInterface()
        {
            Text = "Instagram Multiaccount Monitor";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch { }
            StartPosition = FormStartPosition.CenterScreen;
            // The minimum keeps every card, button label and grid column usable.
            // Above that size the table layouts distribute all additional space.
            MinimumSize = new Size(980, 720);
            ClientSize = new Size(1120, 840);
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
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 98F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 34F));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 38F));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 28F));
            Controls.Add(root);

            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildOverview(), 0, 1);
            root.Controls.Add(BuildActions(), 0, 2);
            root.Controls.Add(BuildOrdersCard(), 0, 3);
            root.Controls.Add(BuildLogCard(), 0, 4);
        }

        private Control BuildHeader()
        {
            TableLayoutPanel header = new TableLayoutPanel();
            header.Dock = DockStyle.Fill;
            header.ColumnCount = 2;
            header.RowCount = 1;
            header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            header.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            TableLayoutPanel titleArea = new TableLayoutPanel();
            titleArea.Dock = DockStyle.Fill;
            titleArea.ColumnCount = 1;
            titleArea.RowCount = 2;
            titleArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            titleArea.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            titleArea.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            titleArea.Padding = new Padding(0, 3, 0, 0);
            Label title = NewLabel("Instagram Multiaccount Monitor", 22F, FontStyle.Bold, TextPrimary);
            title.AutoSize = true;
            title.Anchor = AnchorStyles.Left;
            title.Margin = new Padding(0);
            Label subtitle = NewLabel("Mehrere Profile automatisch über RapidAPI überwachen", 9.5F, FontStyle.Regular, TextMuted);
            subtitle.AutoSize = true;
            subtitle.Anchor = AnchorStyles.Left;
            subtitle.Margin = new Padding(2, 2, 0, 0);
            configLabel = NewLabel("Konfiguration wird geladen ...", 9F, FontStyle.Regular, Amber);
            configLabel.AutoSize = true;
            configLabel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            configLabel.Margin = new Padding(18, 17, 0, 0);
            titleArea.Controls.Add(title, 0, 0);
            titleArea.Controls.Add(subtitle, 0, 1);
            header.Controls.Add(titleArea, 0, 0);
            header.Controls.Add(configLabel, 1, 0);
            return header;
        }

        private Control BuildOverview()
        {
            TableLayoutPanel overview = new TableLayoutPanel();
            overview.Dock = DockStyle.Fill;
            overview.ColumnCount = 2;
            overview.RowCount = 1;
            overview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            overview.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 380F));
            overview.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            overview.Resize += delegate
            {
                int statusWidth = Math.Max(320, Math.Min(420, (int)(overview.ClientSize.Width * 0.36F)));
                overview.ColumnStyles[1].Width = statusWidth;
            };

            Panel accountsCard = NewCard();
            accountsCard.Margin = new Padding(0, 4, 6, 8);
            accountsCard.Padding = new Padding(14);

            TableLayoutPanel accountHeader = new TableLayoutPanel();
            accountHeader.Dock = DockStyle.Top;
            accountHeader.Height = 42;
            accountHeader.ColumnCount = 4;
            accountHeader.RowCount = 1;
            accountHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            accountHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            accountHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            accountHeader.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            accountHeader.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            Label accountTitle = NewLabel("Accounts", 11F, FontStyle.Bold, TextPrimary);
            accountTitle.AutoSize = true;
            accountTitle.Anchor = AnchorStyles.Left;
            accountTitle.Margin = new Padding(0, 0, 16, 4);
            accountCountLabel = NewLabel("0 Accounts", 9F, FontStyle.Regular, TextMuted);
            accountCountLabel.AutoSize = true;
            accountCountLabel.Anchor = AnchorStyles.Left;
            accountCountLabel.Margin = new Padding(0, 0, 12, 3);
            Button addButton = NewCompactButton("+ Hinzufügen", Purple, 118);
            addButton.Click += delegate { ShowAddAccountsDialog(); };
            removeAccountButton = NewCompactButton("Entfernen", SurfaceLight, 100);
            removeAccountButton.Click += delegate { RemoveSelectedAccount(); };
            FlowLayoutPanel accountButtons = new FlowLayoutPanel();
            accountButtons.AutoSize = true;
            accountButtons.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            accountButtons.WrapContents = false;
            accountButtons.FlowDirection = FlowDirection.LeftToRight;
            accountButtons.Anchor = AnchorStyles.Right;
            accountButtons.Margin = new Padding(0);
            addButton.Margin = new Padding(0, 0, 8, 0);
            removeAccountButton.Margin = new Padding(0);
            accountButtons.Controls.Add(addButton);
            accountButtons.Controls.Add(removeAccountButton);
            accountHeader.Controls.Add(accountTitle, 0, 0);
            accountHeader.Controls.Add(accountCountLabel, 1, 0);
            accountHeader.Controls.Add(new Panel(), 2, 0);
            accountHeader.Controls.Add(accountButtons, 3, 0);

            accountsGrid = NewDarkGrid(true);
            accountsGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            accountsGrid.MultiSelect = false;
            accountsGrid.EditMode = DataGridViewEditMode.EditOnEnter;
            AddAccountColumns();
            accountsGrid.SelectionChanged += delegate { if (!rebuildingAccountGrid) UpdateSelectedAccountCard(); };
            accountsGrid.CellContentClick += AccountsGridCellContentClick;
            accountsGrid.CellValueChanged += AccountsGridCellValueChanged;
            accountsCard.Controls.Add(accountsGrid);
            accountsCard.Controls.Add(accountHeader);

            Panel statusCard = NewCard();
            statusCard.Margin = new Padding(6, 4, 0, 8);
            statusCard.Padding = new Padding(22, 18, 22, 18);
            TableLayoutPanel statusLayout = new TableLayoutPanel();
            statusLayout.Dock = DockStyle.Fill;
            statusLayout.ColumnCount = 1;
            statusLayout.RowCount = 5;
            statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            statusLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            statusLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            statusLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            statusLayout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            statusLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            selectedUsernameLabel = NewLabel("Kein Account ausgewählt", 12F, FontStyle.Bold, TextMuted);
            selectedUsernameLabel.AutoSize = true;
            selectedUsernameLabel.Margin = new Padding(0, 0, 0, 8);
            selectedStatusLabel = NewLabel("UNBEKANNT", 24F, FontStyle.Bold, Amber);
            selectedStatusLabel.AutoSize = true;
            selectedStatusLabel.Margin = new Padding(0, 0, 0, 8);
            selectedLastCheckLabel = NewLabel("Letzter Check: -", 9.2F, FontStyle.Regular, TextMuted);
            selectedLastCheckLabel.AutoSize = true;
            selectedLastCheckLabel.Margin = new Padding(2, 0, 0, 5);
            selectedNextCheckLabel = NewLabel("Nächster Check: -", 9.2F, FontStyle.Bold, Purple);
            selectedNextCheckLabel.AutoSize = true;
            selectedNextCheckLabel.Margin = new Padding(2, 0, 0, 0);
            statusLayout.Controls.Add(selectedUsernameLabel, 0, 0);
            statusLayout.Controls.Add(selectedStatusLabel, 0, 1);
            statusLayout.Controls.Add(selectedLastCheckLabel, 0, 2);
            statusLayout.Controls.Add(selectedNextCheckLabel, 0, 3);
            statusCard.Controls.Add(statusLayout);

            overview.Controls.Add(accountsCard, 0, 0);
            overview.Controls.Add(statusCard, 1, 0);
            return overview;
        }

        private Control BuildActions()
        {
            TableLayoutPanel actions = new TableLayoutPanel();
            actions.Dock = DockStyle.Fill;
            actions.Padding = new Padding(0, 11, 0, 9);
            actions.BackColor = Background;
            actions.ColumnCount = 4;
            actions.RowCount = 1;
            for (int i = 0; i < 4; i++) actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
            actions.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            checkSelectedButton = NewButton("Ausgewählten prüfen", Purple);
            checkSelectedButton.Click += delegate { QueueSelectedAccount(); };
            checkAllButton = NewButton("Alle prüfen", SurfaceLight);
            checkAllButton.Click += delegate { QueueAllAccounts(); };
            demoButton = NewButton("Demo auslösen", SurfaceLight);
            demoButton.Click += delegate
            {
                AccountProfile selected = GetSelectedAccount();
                if (selected != null) CreateMockOrder(selected.Username, true);
            };
            Button settingsButton = NewButton("Einstellungen", SurfaceLight);
            settingsButton.Click += delegate { ShowSettingsDialog(); };

            Button[] buttons = new Button[] { checkSelectedButton, checkAllButton, demoButton, settingsButton };
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].Dock = DockStyle.Fill;
                buttons[i].Margin = new Padding(i == 0 ? 0 : 5, 0, i == buttons.Length - 1 ? 0 : 5, 0);
                actions.Controls.Add(buttons[i], i, 0);
            }
            return actions;
        }

        private Control BuildOrdersCard()
        {
            Panel card = NewCard();
            card.Padding = new Padding(16);
            Label title = NewLabel("Simulierte Panel-Orders", 11F, FontStyle.Bold, TextPrimary);
            title.Dock = DockStyle.Top;
            title.Height = 34;
            ordersGrid = NewDarkGrid(false);
            AddTextColumn(ordersGrid, "order_id", "Order-ID", 20F);
            AddTextColumn(ordersGrid, "target", "Ziel", 32F);
            AddTextColumn(ordersGrid, "quantity", "Menge", 11F);
            AddTextColumn(ordersGrid, "status", "Status", 12F);
            AddTextColumn(ordersGrid, "created_at", "Zeit", 18F);
            AddTextColumn(ordersGrid, "mode", "Modus", 9F);
            card.Controls.Add(ordersGrid);
            card.Controls.Add(title);
            return card;
        }

        private Control BuildLogCard()
        {
            Panel card = NewCard();
            card.Padding = new Padding(16);
            Label title = NewLabel("Aktivität", 11F, FontStyle.Bold, TextPrimary);
            title.Dock = DockStyle.Top;
            title.Height = 30;
            activityLog = new RichTextBox();
            activityLog.Dock = DockStyle.Fill;
            activityLog.ReadOnly = true;
            activityLog.BorderStyle = BorderStyle.None;
            activityLog.BackColor = Surface;
            activityLog.ForeColor = TextMuted;
            activityLog.Font = new Font("Consolas", 9.4F);
            activityLog.ScrollBars = RichTextBoxScrollBars.Vertical;
            activityLog.DetectUrls = false;
            card.Controls.Add(activityLog);
            card.Controls.Add(title);
            return card;
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
            button.Height = 40;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.BackColor = color;
            button.ForeColor = TextPrimary;
            button.Cursor = Cursors.Hand;
            button.Font = new Font("Segoe UI", 9.5F, FontStyle.Bold);
            return button;
        }

        private static Button NewCompactButton(string text, Color color, int width)
        {
            Button button = NewButton(text, color);
            button.Width = width;
            button.Height = 32;
            button.Font = new Font("Segoe UI", 8.7F, FontStyle.Bold);
            return button;
        }

        private static DataGridView NewDarkGrid(bool accountGrid)
        {
            DataGridView grid = new DataGridView();
            grid.Dock = DockStyle.Fill;
            grid.ReadOnly = !accountGrid;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            grid.RowHeadersVisible = false;
            grid.BorderStyle = BorderStyle.None;
            grid.BackgroundColor = Surface;
            grid.GridColor = SurfaceLight;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersHeight = 34;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.RowTemplate.Height = 32;
            grid.ScrollBars = ScrollBars.Vertical;
            grid.DefaultCellStyle.BackColor = Surface;
            grid.DefaultCellStyle.ForeColor = TextPrimary;
            grid.DefaultCellStyle.SelectionBackColor = accountGrid ? Color.FromArgb(67, 56, 96) : Surface;
            grid.DefaultCellStyle.SelectionForeColor = TextPrimary;
            grid.DefaultCellStyle.Padding = new Padding(5, 0, 5, 0);
            grid.DefaultCellStyle.Font = new Font("Segoe UI", 9F);
            grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(31, 34, 45);
            grid.ColumnHeadersDefaultCellStyle.BackColor = SurfaceLight;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = TextMuted;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = SurfaceLight;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 8.8F, FontStyle.Bold);
            grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(5, 0, 5, 0);
            return grid;
        }

        private void AddAccountColumns()
        {
            DataGridViewCheckBoxColumn enabled = new DataGridViewCheckBoxColumn();
            enabled.Name = "enabled";
            enabled.HeaderText = "Aktiv";
            enabled.FillWeight = 10F;
            accountsGrid.Columns.Add(enabled);
            AddTextColumn(accountsGrid, "username", "Account", 32F);
            AddTextColumn(accountsGrid, "status", "Status", 20F);
            AddTextColumn(accountsGrid, "last", "Letzter Check", 22F);
            AddTextColumn(accountsGrid, "next", "Nächster", 18F);
            for (int i = 1; i < accountsGrid.Columns.Count; i++) accountsGrid.Columns[i].ReadOnly = true;
        }

        private static void AddTextColumn(DataGridView grid, string name, string header, float weight)
        {
            DataGridViewTextBoxColumn column = new DataGridViewTextBoxColumn();
            column.Name = name;
            column.HeaderText = header;
            column.FillWeight = weight;
            column.SortMode = DataGridViewColumnSortMode.NotSortable;
            grid.Columns.Add(column);
        }

        private void LoadConfiguration()
        {
            Dictionary<string, string> values = ReadEnvFile(envPath);
            values.TryGetValue("RAPIDAPI_KEY", out apiKey);
            int parsed;
            string raw;
            if (values.TryGetValue("CHECK_INTERVAL_MINUTES", out raw) && int.TryParse(raw, out parsed) && parsed > 0)
                intervalMinutes = parsed;
            if (values.TryGetValue("PANEL_SIMULATED_QUANTITY", out raw) && int.TryParse(raw, out parsed) && parsed > 0)
                simulatedQuantity = parsed;
            if (values.TryGetValue("EVENT_COOLDOWN_MINUTES", out raw) && int.TryParse(raw, out parsed) && parsed > 0)
                eventCooldownMinutes = parsed;

            configIsValid = !string.IsNullOrWhiteSpace(apiKey) && apiKey != "dein_rapidapi_key";
            configLabel.Text = configIsValid ? "RapidAPI bereit" : ".env / RapidAPI-Key fehlt";
            configLabel.ForeColor = configIsValid ? Green : Amber;
            AppendLog(configIsValid
                ? "Konfiguration geladen. API-Key ist lokal vorhanden."
                : "Kein API-Key gefunden. Demo-Modus ist weiterhin verfügbar.");
            UpdateActionButtons();
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
                result[line.Substring(0, separator).Trim()] = line.Substring(separator + 1).Trim().Trim('"', '\'');
            }
            return result;
        }

        private void LoadAccounts()
        {
            accounts.Clear();
            if (File.Exists(accountsPath))
            {
                try
                {
                    object[] rows = json.DeserializeObject(File.ReadAllText(accountsPath, Encoding.UTF8)) as object[];
                    if (rows != null)
                    {
                        foreach (object raw in rows)
                        {
                            Dictionary<string, object> item = raw as Dictionary<string, object>;
                            if (item == null) continue;
                            string username = DictValue(item, "username").Trim().TrimStart('@');
                            if (!IsValidUsername(username) || FindAccount(username) != null) continue;
                            AccountProfile account = new AccountProfile();
                            account.Username = username;
                            object value;
                            if (item.TryGetValue("enabled", out value)) account.Enabled = Convert.ToBoolean(value);
                            if (item.TryGetValue("is_private", out value) && value != null) account.IsPrivate = Convert.ToBoolean(value);
                            DateTime parsedDate;
                            if (item.TryGetValue("last_checked", out value) && DateTime.TryParse(Convert.ToString(value), out parsedDate))
                                account.LastChecked = parsedDate.ToLocalTime();
                            accounts.Add(account);
                        }
                    }
                }
                catch (Exception ex)
                {
                    AppendLog("Accountliste konnte nicht gelesen werden: " + ex.Message);
                }
            }

            if (accounts.Count == 0)
            {
                Dictionary<string, string> env = ReadEnvFile(envPath);
                string legacyUsername;
                if (env.TryGetValue("INSTAGRAM_USERNAME", out legacyUsername))
                    AddAccountInternal(NormalizeUsername(legacyUsername));
                ImportLegacyState();
            }

            for (int i = 0; i < accounts.Count; i++)
                accounts[i].NextCheck = DateTime.Now.AddSeconds(3 + (i * 4));

            SaveAccounts();
            AppendLog(accounts.Count + " Account(s) geladen.");
        }

        private void ImportLegacyState()
        {
            if (!File.Exists(legacyStatePath)) return;
            try
            {
                Dictionary<string, object> state = json.Deserialize<Dictionary<string, object>>(File.ReadAllText(legacyStatePath));
                string username = NormalizeUsername(DictValue(state, "username"));
                if (!IsValidUsername(username)) return;
                AccountProfile account = FindAccount(username);
                if (account == null)
                {
                    AddAccountInternal(username);
                    account = FindAccount(username);
                }
                object privateValue;
                if (account != null && state.TryGetValue("is_private", out privateValue))
                    account.IsPrivate = Convert.ToBoolean(privateValue);
            }
            catch { }
        }

        private void SaveAccounts()
        {
            List<Dictionary<string, object>> data = new List<Dictionary<string, object>>();
            foreach (AccountProfile account in accounts)
            {
                Dictionary<string, object> item = new Dictionary<string, object>();
                item["username"] = account.Username;
                item["enabled"] = account.Enabled;
                item["is_private"] = account.IsPrivate.HasValue ? (object)account.IsPrivate.Value : null;
                item["last_checked"] = account.LastChecked == DateTime.MinValue ? "" : account.LastChecked.ToUniversalTime().ToString("o");
                data.Add(item);
            }
            File.WriteAllText(accountsPath, json.Serialize(data) + Environment.NewLine, new UTF8Encoding(false));
        }

        private void RefreshAccountGrid()
        {
            string selectedUsername = GetSelectedAccount() == null ? "" : GetSelectedAccount().Username;
            rebuildingAccountGrid = true;
            accountsGrid.Rows.Clear();
            foreach (AccountProfile account in accounts)
            {
                int index = accountsGrid.Rows.Add(
                    account.Enabled,
                    "@" + account.Username,
                    AccountStatusText(account),
                    FormatDate(account.LastChecked),
                    FormatCountdown(account));
                accountsGrid.Rows[index].Tag = account;
                if (string.Equals(account.Username, selectedUsername, StringComparison.OrdinalIgnoreCase))
                    accountsGrid.Rows[index].Selected = true;
            }
            rebuildingAccountGrid = false;
            accountCountLabel.Text = accounts.Count + (accounts.Count == 1 ? " Account" : " Accounts");
            if (accountsGrid.SelectedRows.Count == 0 && accountsGrid.Rows.Count > 0)
                accountsGrid.Rows[0].Selected = true;
            UpdateSelectedAccountCard();
            UpdateActionButtons();
        }

        private void UpdateAccountGridLive()
        {
            foreach (DataGridViewRow row in accountsGrid.Rows)
            {
                AccountProfile account = row.Tag as AccountProfile;
                if (account == null) continue;
                row.Cells[0].Value = account.Enabled;
                row.Cells[2].Value = AccountStatusText(account);
                row.Cells[3].Value = FormatDate(account.LastChecked);
                row.Cells[4].Value = FormatCountdown(account);
            }
            UpdateSelectedAccountCard();
        }

        private void AccountsGridCellContentClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex >= 0 && e.ColumnIndex == 0) accountsGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        }

        private void AccountsGridCellValueChanged(object sender, DataGridViewCellEventArgs e)
        {
            if (rebuildingAccountGrid || e.RowIndex < 0 || e.ColumnIndex != 0) return;
            AccountProfile account = accountsGrid.Rows[e.RowIndex].Tag as AccountProfile;
            if (account == null) return;
            account.Enabled = Convert.ToBoolean(accountsGrid.Rows[e.RowIndex].Cells[0].Value);
            if (account.Enabled) account.NextCheck = DateTime.Now.AddSeconds(3);
            SaveAccounts();
            UpdateSelectedAccountCard();
            AppendLog("@" + account.Username + (account.Enabled ? " aktiviert." : " pausiert."));
        }

        private void TimerTick(object sender, EventArgs e)
        {
            foreach (AccountProfile account in accounts)
            {
                if (account.Enabled && account.NextCheck != DateTime.MinValue && DateTime.Now >= account.NextCheck)
                    QueueAccount(account);
            }
            ProcessNextQueuedCheck();
            UpdateAccountGridLive();
            UpdateSimulatedOrderLifecycle();
        }

        private void QueueSelectedAccount()
        {
            AccountProfile selected = GetSelectedAccount();
            if (selected != null) QueueAccount(selected);
            ProcessNextQueuedCheck();
        }

        private void QueueAllAccounts()
        {
            foreach (AccountProfile account in accounts)
                if (account.Enabled) QueueAccount(account);
            ProcessNextQueuedCheck();
            AppendLog("Alle aktiven Accounts wurden zur Prüfung eingeplant.");
        }

        private void QueueAccount(AccountProfile account)
        {
            if (!configIsValid || account == null || !account.Enabled || queuedAccounts.Contains(account.Username)) return;
            scanQueue.Enqueue(account.Username);
            queuedAccounts.Add(account.Username);
            account.NextCheck = DateTime.Now.AddMinutes(intervalMinutes);
        }

        private void ProcessNextQueuedCheck()
        {
            if (checker.IsBusy || scanQueue.Count == 0) return;
            string username = scanQueue.Dequeue();
            AccountProfile account = FindAccount(username);
            if (account == null || !account.Enabled)
            {
                queuedAccounts.Remove(username);
                ProcessNextQueuedCheck();
                return;
            }
            account.IsChecking = true;
            account.LastError = "";
            AppendLog("Prüfe @" + username + " ...");
            UpdateAccountGridLive();
            checker.RunWorkerAsync(username);
        }

        private void CheckerDoWork(object sender, DoWorkEventArgs e)
        {
            string username = Convert.ToString(e.Argument);
            CheckResult result = new CheckResult();
            result.Username = username;
            result.IsPrivate = FetchProfileStatus(username);
            e.Result = result;
        }

        private void CheckerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            string username = "";
            CheckResult result = null;
            if (e.Error == null && !e.Cancelled) result = e.Result as CheckResult;
            if (result != null) username = result.Username;
            if (string.IsNullOrWhiteSpace(username))
            {
                foreach (AccountProfile candidate in accounts)
                    if (candidate.IsChecking) { username = candidate.Username; break; }
            }
            AccountProfile account = FindAccount(username);
            queuedAccounts.Remove(username);
            if (account != null) account.IsChecking = false;

            if (e.Error != null)
            {
                if (account != null)
                {
                    account.LastError = FriendlyError(e.Error);
                    account.NextCheck = DateTime.Now.AddMinutes(Math.Min(5, intervalMinutes));
                }
                AppendLog("Fehler bei @" + username + ": " + FriendlyError(e.Error));
            }
            else if (account != null && result != null)
            {
                bool becamePublic = account.IsPrivate.HasValue && account.IsPrivate.Value && !result.IsPrivate;
                account.IsPrivate = result.IsPrivate;
                account.LastChecked = DateTime.Now;
                account.NextCheck = DateTime.Now.AddMinutes(intervalMinutes);
                account.LastError = "";
                AppendLog("@" + username + ": is_private = " + result.IsPrivate.ToString().ToLowerInvariant());
                if (becamePublic)
                {
                    if (CreateMockOrder(username, false))
                    {
                        System.Media.SystemSounds.Exclamation.Play();
                        MessageBox.Show(this,
                            "@" + username + " ist jetzt öffentlich. Eine Demo-Order wurde erzeugt.",
                            "Profil ist öffentlich",
                            MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                    }
                }
            }

            SaveAccounts();
            RefreshAccountGrid();
            ProcessNextQueuedCheck();
        }

        private bool FetchProfileStatus(string username)
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

        private void ShowAddAccountsDialog()
        {
            Form dialog = NewDialog("Accounts hinzufügen", new Size(560, 430));
            Label heading = NewLabel("Accounts hinzufügen", 16F, FontStyle.Bold, TextPrimary);
            heading.Location = new Point(28, 22);
            heading.AutoSize = true;
            Label hint = NewLabel(
                "Nur das @ des Accounts einfügen, zum Beispiel @thunderceo.\nMehrere Accounts per Zeile, Leerzeichen, Komma oder Semikolon.",
                9F, FontStyle.Regular, TextMuted);
            hint.Location = new Point(30, 58);
            hint.Size = new Size(490, 45);
            hint.AutoSize = false;

            RichTextBox input = new RichTextBox();
            input.Location = new Point(30, 110);
            input.Size = new Size(500, 205);
            input.BackColor = SurfaceLight;
            input.ForeColor = TextPrimary;
            input.BorderStyle = BorderStyle.None;
            input.Font = new Font("Consolas", 10F);
            input.Text = "";

            Label resultLabel = NewLabel("", 9F, FontStyle.Regular, Amber);
            resultLabel.Location = new Point(30, 325);
            resultLabel.Size = new Size(300, 40);
            resultLabel.AutoSize = false;

            Button importButton = NewButton("TXT importieren", SurfaceLight);
            importButton.Location = new Point(30, 365);
            importButton.Size = new Size(165, 40);
            importButton.Click += delegate
            {
                using (OpenFileDialog picker = new OpenFileDialog())
                {
                    picker.Title = "Accountlisten auswählen";
                    picker.Filter = "Textdateien (*.txt)|*.txt|Alle Dateien (*.*)|*.*";
                    picker.Multiselect = true;
                    if (picker.ShowDialog(dialog) != DialogResult.OK) return;

                    StringBuilder imported = new StringBuilder();
                    foreach (string file in picker.FileNames)
                    {
                        if (imported.Length > 0) imported.AppendLine();
                        imported.Append(File.ReadAllText(file, Encoding.UTF8));
                    }
                    if (!string.IsNullOrWhiteSpace(input.Text)) input.AppendText(Environment.NewLine);
                    input.AppendText(imported.ToString());
                    resultLabel.Text = picker.FileNames.Length +
                        (picker.FileNames.Length == 1 ? " TXT-Datei geladen." : " TXT-Dateien geladen.");
                }
            };

            Button cancel = NewButton("Abbrechen", SurfaceLight);
            cancel.Location = new Point(210, 365);
            cancel.Size = new Size(145, 40);
            cancel.DialogResult = DialogResult.Cancel;
            Button add = NewButton("Hinzufügen", Purple);
            add.Location = new Point(365, 365);
            add.Size = new Size(165, 40);
            add.Click += delegate
            {
                List<string> parsed = ParseUsernames(input.Text);
                int added = 0;
                foreach (string username in parsed)
                    if (AddAccountInternal(username)) added++;
                if (added == 0)
                {
                    resultLabel.Text = "Keine neuen gültigen Accounts erkannt.";
                    return;
                }
                SaveAccounts();
                dialog.DialogResult = DialogResult.OK;
                dialog.Close();
                AppendLog(added + " Account(s) hinzugefügt.");
            };
            dialog.Controls.Add(heading);
            dialog.Controls.Add(hint);
            dialog.Controls.Add(input);
            dialog.Controls.Add(resultLabel);
            dialog.Controls.Add(importButton);
            dialog.Controls.Add(cancel);
            dialog.Controls.Add(add);
            dialog.AcceptButton = add;
            dialog.CancelButton = cancel;
            dialog.Shown += delegate { input.Focus(); };

            if (dialog.ShowDialog(this) == DialogResult.OK) RefreshAccountGrid();
            dialog.Dispose();
        }

        private void RemoveSelectedAccount()
        {
            AccountProfile selected = GetSelectedAccount();
            if (selected == null) return;
            DialogResult answer = MessageBox.Show(this,
                "@" + selected.Username + " aus der lokalen Scanliste entfernen?",
                "Account entfernen",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (answer != DialogResult.Yes) return;
            accounts.Remove(selected);
            SaveAccounts();
            RefreshAccountGrid();
            AppendLog("@" + selected.Username + " wurde aus der Scanliste entfernt.");
        }

        private void ShowSettingsDialog()
        {
            Form dialog = NewDialog("Lokale Einstellungen", new Size(520, 465));
            Label heading = NewLabel("Scanner-Einstellungen", 16F, FontStyle.Bold, TextPrimary);
            heading.Location = new Point(28, 22);
            heading.AutoSize = true;
            Label hint = NewLabel("Ein RapidAPI-Key gilt lokal für alle Accounts.", 9F, FontStyle.Regular, TextMuted);
            hint.Location = new Point(30, 57);
            hint.AutoSize = true;
            Label keyLabel = NewLabel("RapidAPI-Key", 9F, FontStyle.Bold, TextMuted);
            keyLabel.Location = new Point(30, 94);
            keyLabel.AutoSize = true;
            TextBox keyBox = NewSettingsTextBox();
            keyBox.Location = new Point(30, 117);
            keyBox.Width = 460;
            keyBox.UseSystemPasswordChar = true;
            keyBox.Text = apiKey ?? "";
            CheckBox showKey = new CheckBox();
            showKey.Text = "Key anzeigen";
            showKey.Location = new Point(30, 151);
            showKey.AutoSize = true;
            showKey.ForeColor = TextMuted;
            showKey.BackColor = Background;
            showKey.CheckedChanged += delegate { keyBox.UseSystemPasswordChar = !showKey.Checked; };
            Label intervalLabel = NewLabel("Scanintervall pro Account (Min.)", 9F, FontStyle.Bold, TextMuted);
            intervalLabel.Location = new Point(30, 193);
            intervalLabel.AutoSize = true;
            NumericUpDown intervalBox = NewSettingsNumber();
            intervalBox.Location = new Point(30, 216);
            intervalBox.Width = 215;
            intervalBox.Minimum = 1;
            intervalBox.Maximum = 10080;
            intervalBox.Value = Math.Max(1, Math.Min(10080, intervalMinutes));
            Label quantityLabel = NewLabel("Demo-Menge", 9F, FontStyle.Bold, TextMuted);
            quantityLabel.Location = new Point(270, 193);
            quantityLabel.AutoSize = true;
            NumericUpDown quantityBox = NewSettingsNumber();
            quantityBox.Location = new Point(270, 216);
            quantityBox.Width = 220;
            quantityBox.Maximum = 10000000;
            quantityBox.Value = Math.Max(1, Math.Min(10000000, simulatedQuantity));
            Label cooldownLabel = NewLabel("Event-Cooldown pro Account (Min.)", 9F, FontStyle.Bold, TextMuted);
            cooldownLabel.Location = new Point(30, 263);
            cooldownLabel.AutoSize = true;
            NumericUpDown cooldownBox = NewSettingsNumber();
            cooldownBox.Location = new Point(30, 286);
            cooldownBox.Width = 215;
            cooldownBox.Minimum = 1;
            cooldownBox.Maximum = 525600;
            cooldownBox.Value = Math.Max(1, Math.Min(525600, eventCooldownMinutes));
            Label quotaHint = NewLabel("Hinweis: Jeder aktive Account verbraucht einen Request pro Intervall.", 8.5F, FontStyle.Regular, Amber);
            quotaHint.Location = new Point(30, 335);
            quotaHint.AutoSize = true;
            Button cancel = NewButton("Abbrechen", SurfaceLight);
            cancel.Location = new Point(140, 397);
            cancel.Size = new Size(160, 40);
            cancel.DialogResult = DialogResult.Cancel;
            Button save = NewButton("Speichern", Purple);
            save.Location = new Point(320, 397);
            save.Size = new Size(170, 40);
            save.Click += delegate
            {
                string newKey = keyBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(newKey))
                {
                    MessageBox.Show(dialog, "Bitte den RapidAPI-Key eingeben.", "Fehlender Key",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                string fallbackUsername = accounts.Count > 0 ? accounts[0].Username : "instagram";
                string[] lines = new string[]
                {
                    "RAPIDAPI_KEY=" + newKey,
                    "INSTAGRAM_USERNAME=" + fallbackUsername,
                    "CHECK_INTERVAL_MINUTES=" + Convert.ToInt32(intervalBox.Value),
                    "PANEL_SIMULATED_QUANTITY=" + Convert.ToInt32(quantityBox.Value),
                    "EVENT_COOLDOWN_MINUTES=" + Convert.ToInt32(cooldownBox.Value)
                };
                File.WriteAllLines(envPath, lines, new UTF8Encoding(false));
                dialog.DialogResult = DialogResult.OK;
                dialog.Close();
            };
            dialog.Controls.Add(heading);
            dialog.Controls.Add(hint);
            dialog.Controls.Add(keyLabel);
            dialog.Controls.Add(keyBox);
            dialog.Controls.Add(showKey);
            dialog.Controls.Add(intervalLabel);
            dialog.Controls.Add(intervalBox);
            dialog.Controls.Add(quantityLabel);
            dialog.Controls.Add(quantityBox);
            dialog.Controls.Add(cooldownLabel);
            dialog.Controls.Add(cooldownBox);
            dialog.Controls.Add(quotaHint);
            dialog.Controls.Add(cancel);
            dialog.Controls.Add(save);
            dialog.AcceptButton = save;
            dialog.CancelButton = cancel;
            dialog.Shown += delegate { intervalBox.Focus(); };

            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                LoadConfiguration();
                foreach (AccountProfile account in accounts)
                    account.NextCheck = DateTime.Now.AddSeconds(3);
                AppendLog("Scanner-Einstellungen gespeichert.");
            }
            dialog.Dispose();
        }

        private static Form NewDialog(string title, Size size)
        {
            Form dialog = new Form();
            dialog.Text = title;
            dialog.StartPosition = FormStartPosition.CenterParent;
            dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
            dialog.MaximizeBox = false;
            dialog.MinimizeBox = false;
            dialog.ShowInTaskbar = false;
            dialog.ClientSize = size;
            dialog.BackColor = Background;
            dialog.ForeColor = TextPrimary;
            dialog.Font = new Font("Segoe UI", 10F);
            return dialog;
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

        private bool AddAccountInternal(string username)
        {
            username = NormalizeUsername(username);
            if (!IsValidUsername(username) || FindAccount(username) != null) return false;
            AccountProfile account = new AccountProfile();
            account.Username = username;
            account.NextCheck = DateTime.Now.AddSeconds(3 + (accounts.Count * 4));
            accounts.Add(account);
            return true;
        }

        private static List<string> ParseUsernames(string text)
        {
            List<string> result = new List<string>();
            HashSet<string> unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string rawLine in Regex.Split(text ?? "", @"\r?\n"))
            {
                string line = rawLine;
                int commentStart = line.IndexOf('#');
                if (commentStart >= 0) line = line.Substring(0, commentStart);
                foreach (string token in Regex.Split(line, @"[\s,;]+"))
                {
                    string username = NormalizeUsername(token);
                    if (IsValidUsername(username) && unique.Add(username)) result.Add(username);
                }
            }
            return result;
        }

        private static string NormalizeUsername(string value)
        {
            string username = (value ?? "").Trim();
            username = Regex.Replace(username, @"^https?://(www\.)?instagram\.com/", "", RegexOptions.IgnoreCase);
            username = username.Trim().TrimStart('@').Trim('/');
            int separator = username.IndexOfAny(new char[] { '/', '?', '#' });
            if (separator >= 0) username = username.Substring(0, separator);
            return username.Trim();
        }

        private static bool IsValidUsername(string username)
        {
            return Regex.IsMatch(username ?? "", @"^[A-Za-z0-9._]{1,30}$");
        }

        private AccountProfile FindAccount(string username)
        {
            foreach (AccountProfile account in accounts)
                if (string.Equals(account.Username, username, StringComparison.OrdinalIgnoreCase)) return account;
            return null;
        }

        private AccountProfile GetSelectedAccount()
        {
            if (accountsGrid == null || accountsGrid.SelectedRows.Count == 0) return null;
            return accountsGrid.SelectedRows[0].Tag as AccountProfile;
        }

        private void UpdateSelectedAccountCard()
        {
            AccountProfile selected = GetSelectedAccount();
            if (selected == null)
            {
                selectedUsernameLabel.Text = "Kein Account ausgewählt";
                selectedStatusLabel.Text = "UNBEKANNT";
                selectedStatusLabel.ForeColor = Amber;
                selectedLastCheckLabel.Text = "Letzter Check: -";
                selectedNextCheckLabel.Text = "Nächster Check: -";
            }
            else
            {
                selectedUsernameLabel.Text = "@" + selected.Username;
                selectedStatusLabel.Text = AccountStatusText(selected).ToUpperInvariant();
                selectedStatusLabel.ForeColor = selected.IsChecking ? Purple :
                    (!string.IsNullOrWhiteSpace(selected.LastError) ? Red :
                    (selected.IsPrivate.HasValue ? (selected.IsPrivate.Value ? Red : Green) : Amber));
                selectedLastCheckLabel.Text = "Letzter Check: " + FormatDate(selected.LastChecked);
                selectedNextCheckLabel.Text = "Nächster Check: " + FormatCountdown(selected);
            }
            UpdateActionButtons();
        }

        private void UpdateActionButtons()
        {
            if (checkSelectedButton == null) return;
            AccountProfile selected = GetSelectedAccount();
            checkSelectedButton.Enabled = configIsValid && selected != null && selected.Enabled;
            checkAllButton.Enabled = configIsValid && accounts.Count > 0;
            demoButton.Enabled = selected != null;
            removeAccountButton.Enabled = selected != null;
        }

        private static string AccountStatusText(AccountProfile account)
        {
            if (!account.Enabled) return "Pausiert";
            if (account.IsChecking) return "Wird geprüft";
            if (!string.IsNullOrWhiteSpace(account.LastError)) return "Fehler";
            if (!account.IsPrivate.HasValue) return "Unbekannt";
            return account.IsPrivate.Value ? "Privat" : "Öffentlich";
        }

        private static string FormatDate(DateTime date)
        {
            return date == DateTime.MinValue ? "-" : date.ToString("dd.MM. HH:mm");
        }

        private static string FormatCountdown(AccountProfile account)
        {
            if (!account.Enabled) return "pausiert";
            if (account.NextCheck == DateTime.MinValue) return "-";
            TimeSpan remaining = account.NextCheck - DateTime.Now;
            if (remaining.TotalSeconds < 0) remaining = TimeSpan.Zero;
            return string.Format("{0:00}:{1:00}", (int)remaining.TotalMinutes, remaining.Seconds);
        }

        private static string FriendlyError(Exception error)
        {
            WebException web = error as WebException;
            if (web != null && web.Response is HttpWebResponse)
                return "RapidAPI HTTP " + (int)((HttpWebResponse)web.Response).StatusCode;
            return error.Message;
        }

        private bool CreateMockOrder(string username, bool manual)
        {
            TimeSpan remaining;
            if (!manual && TryGetEventCooldown(username, out remaining))
            {
                int minutes = Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
                AppendLog("Event für @" + username + " unterdrückt: Cooldown noch " + minutes + " Min.");
                return false;
            }
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
            eventData["schema_version"] = 1;
            eventData["event_id"] = Guid.NewGuid().ToString();
            eventData["event"] = manual ? "manual_demo_order" : "profile_became_public";
            eventData["username"] = username;
            eventData["detected_at"] = DateTime.UtcNow.ToString("o");
            eventData["mode"] = "dry_run";
            eventData["cooldown_minutes"] = eventCooldownMinutes;
            eventData["mock_order"] = order;
            File.AppendAllText(eventsPath, json.Serialize(eventData) + Environment.NewLine, Encoding.UTF8);
            AddOrderToGrid(order);
            AppendLog("Demo-Order für @" + username + " erzeugt: " + DictValue(order, "order_id"));
            return true;
        }

        private bool TryGetEventCooldown(string username, out TimeSpan remaining)
        {
            remaining = TimeSpan.Zero;
            if (!File.Exists(eventsPath)) return false;
            DateTime latest = DateTime.MinValue;
            try
            {
                foreach (string line in File.ReadAllLines(eventsPath, Encoding.UTF8))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    Dictionary<string, object> item = json.Deserialize<Dictionary<string, object>>(line);
                    if (DictValue(item, "event") != "profile_became_public") continue;
                    if (!string.Equals(DictValue(item, "username").TrimStart('@'), username.TrimStart('@'),
                        StringComparison.OrdinalIgnoreCase)) continue;
                    DateTime detected;
                    if (!DateTime.TryParse(DictValue(item, "detected_at"), CultureInfo.InvariantCulture,
                        DateTimeStyles.RoundtripKind, out detected)) continue;
                    detected = detected.ToUniversalTime();
                    if (detected > latest) latest = detected;
                }
            }
            catch
            {
                return false;
            }
            if (latest == DateTime.MinValue) return false;
            remaining = latest.AddMinutes(eventCooldownMinutes) - DateTime.UtcNow;
            return remaining > TimeSpan.Zero;
        }

        private void UpdateSimulatedOrderLifecycle()
        {
            if (ordersGrid == null) return;
            foreach (DataGridViewRow row in ordersGrid.Rows)
            {
                Dictionary<string, object> order = row.Tag as Dictionary<string, object>;
                if (order == null || DictValue(order, "mode") != "dry_run") continue;
                DateTime created;
                if (!DateTime.TryParse(DictValue(order, "created_at"), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out created)) continue;
                double age = (DateTime.UtcNow - created.ToUniversalTime()).TotalSeconds;
                string nextStatus = age >= 30 ? "Completed" : age >= 8 ? "In Progress" : "Pending";
                if (DictValue(order, "status") == nextStatus) continue;
                order["status"] = nextStatus;
                row.Cells["status"].Value = nextStatus;
                Dictionary<string, object> eventData = new Dictionary<string, object>();
                eventData["schema_version"] = 1;
                eventData["event_id"] = Guid.NewGuid().ToString();
                eventData["event"] = "demo_order_status_changed";
                eventData["username"] = UsernameFromTarget(DictValue(order, "target"));
                eventData["detected_at"] = DateTime.UtcNow.ToString("o");
                eventData["mode"] = "dry_run";
                eventData["mock_order"] = new Dictionary<string, object>(order);
                File.AppendAllText(eventsPath, json.Serialize(eventData) + Environment.NewLine, Encoding.UTF8);
                AppendLog("Demo-Order " + DictValue(order, "order_id") + ": " + nextStatus);
            }
        }

        private static string UsernameFromTarget(string target)
        {
            if (string.IsNullOrWhiteSpace(target)) return "";
            return target.TrimEnd('/').Substring(target.TrimEnd('/').LastIndexOf('/') + 1);
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
                    if (order != null) AddOrderToGrid(order);
                }
            }
            catch (Exception ex)
            {
                AppendLog("Order-Verlauf konnte nicht gelesen werden: " + ex.Message);
            }
        }

        private void AddOrderToGrid(Dictionary<string, object> order)
        {
            string orderId = DictValue(order, "order_id");
            string createdAt = DictValue(order, "created_at");
            DateTime parsed;
            if (DateTime.TryParse(createdAt, out parsed)) createdAt = parsed.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss");
            DataGridViewRow existing = null;
            foreach (DataGridViewRow row in ordersGrid.Rows)
                if (Convert.ToString(row.Cells["order_id"].Value) == orderId) { existing = row; break; }
            if (existing == null)
            {
                ordersGrid.Rows.Insert(0,
                    orderId,
                    DictValue(order, "target"),
                    DictValue(order, "quantity"),
                    DictValue(order, "status"),
                    createdAt,
                    DictValue(order, "mode"));
                existing = ordersGrid.Rows[0];
            }
            else
            {
                existing.Cells["target"].Value = DictValue(order, "target");
                existing.Cells["quantity"].Value = DictValue(order, "quantity");
                existing.Cells["status"].Value = DictValue(order, "status");
                existing.Cells["created_at"].Value = createdAt;
                existing.Cells["mode"].Value = DictValue(order, "mode");
            }
            existing.Tag = new Dictionary<string, object>(order);
            ordersGrid.ClearSelection();
            ordersGrid.CurrentCell = null;
            if (ordersGrid.Rows.Count > 100) ordersGrid.Rows.RemoveAt(ordersGrid.Rows.Count - 1);
        }

        private static string DictValue(Dictionary<string, object> source, string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) && value != null ? Convert.ToString(value) : "";
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
