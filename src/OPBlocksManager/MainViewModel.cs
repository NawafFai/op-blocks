using System;
using System.Collections.ObjectModel;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using OPBlocksManager.Services;

namespace OPBlocksManager
{
    public sealed class MainViewModel : ViewModelBase
    {
        private readonly RegistryScanner _scanner = new RegistryScanner();
        private readonly BlockCatalog _catalog = new BlockCatalog();
        private readonly Registrar _registrar = new Registrar();

        public Localizer L { get; } = new Localizer();

        public ObservableCollection<SimulatorInfo> Simulators { get; } = new ObservableCollection<SimulatorInfo>();
        public ObservableCollection<BlockRowViewModel> Blocks { get; } = new ObservableCollection<BlockRowViewModel>();

        private readonly AspenTemplateInstaller _aspenTemplate = new AspenTemplateInstaller();

        public ICommand RefreshCommand { get; }
        public ICommand ToggleLanguageCommand { get; }
        public ICommand AboutCommand { get; }
        public ICommand EnableAspenCommand { get; }

        public string AppVersion
        {
            get
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                return v == null ? "1.0" : v.ToString(3);
            }
        }

        private string _blocksDirectory;
        public string BlocksDirectory { get => _blocksDirectory; set => Set(ref _blocksDirectory, value); }

        private string _log = "";
        public string Log { get => _log; private set => Set(ref _log, value); }

        private string _status;
        public string Status { get => _status; private set => Set(ref _status, value); }

        public MainViewModel()
        {
            RefreshCommand = new RelayCommand(_ => Refresh());
            ToggleLanguageCommand = new RelayCommand(_ => ToggleLanguage());
            AboutCommand = new RelayCommand(_ => ShowAbout());
            EnableAspenCommand = new RelayCommand(_ => EnableAspen());
            _status = L["StatusReady"];
            Refresh();
        }

        private void ToggleLanguage()
        {
            L.Toggle();
            // Localized, per-row status text needs to re-read.
            foreach (var row in Blocks) row.RaiseLocalized();
            Status = L["StatusReady"];
        }

        private void ShowAbout()
        {
            var about = new AboutWindow(L, AppVersion) { Owner = Application.Current.MainWindow };
            about.ShowDialog();
        }

        private void EnableAspen()
        {
            var r = _aspenTemplate.Install();
            AppendLog("Enable in Aspen: " + r.Message);
            Status = r.Success ? "CAPE-OPEN template installed for Aspen." : "Enable in Aspen failed — see log.";
            System.Windows.MessageBox.Show(r.Message,
                r.Success ? "ONE PROCESS — Aspen ready" : "ONE PROCESS",
                MessageBoxButton.OK, r.Success ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }

        public void Refresh()
        {
            Simulators.Clear();
            foreach (var s in _scanner.DetectSimulators()) Simulators.Add(s);
            if (Simulators.Count == 0)
                Simulators.Add(new SimulatorInfo { Kind = "—", Name = "No CAPE-OPEN host detected", Version = "", Bitness = "", Path = "", Found = false });

            BlocksDirectory = BlockCatalog.ResolveBlocksDirectory();
            Blocks.Clear();
            var manifests = _catalog.Load(BlocksDirectory);
            foreach (var m in manifests)
                foreach (var def in m.Blocks)
                    Blocks.Add(new BlockRowViewModel(this, m, def));

            foreach (var row in Blocks) row.RefreshState(_scanner);

            Status = $"{Blocks.Count} block(s) · {CountFound()} host(s) detected.";
            AppendLog($"Refreshed: {Blocks.Count} block(s), library at {BlocksDirectory}");
        }

        private int CountFound()
        {
            int n = 0;
            foreach (var s in Simulators) if (s.Found) n++;
            return n;
        }

        internal async void InstallRow(BlockRowViewModel row)
        {
            if (row.DllMissing) { AppendLog($"Cannot install {row.Def.Code}: DLL not found ({row.Manifest.DllPath})."); return; }
            row.Busy = true;
            Status = $"Installing {row.Def.Code} (approve the UAC prompt)…";
            var outcome = await Task.Run(() => _registrar.Install(row.Manifest));
            row.Busy = false;
            HandleOutcome(row, "Install", outcome);
        }

        internal async void RemoveRow(BlockRowViewModel row)
        {
            row.Busy = true;
            Status = $"Removing {row.Def.Code} (approve the UAC prompt)…";
            var outcome = await Task.Run(() => _registrar.Remove(row.Manifest));
            row.Busy = false;
            HandleOutcome(row, "Remove", outcome);
        }

        private void HandleOutcome(BlockRowViewModel row, string action, Registrar.Outcome outcome)
        {
            if (outcome.Cancelled)
            {
                AppendLog($"{action} {row.Def.Code}: cancelled at the UAC prompt.");
                Status = "Cancelled.";
            }
            else
            {
                AppendLog($"{action} {row.Def.Code}: {(outcome.Success ? "OK" : "FAILED")}");
                if (!string.IsNullOrWhiteSpace(outcome.Log)) AppendLog(outcome.Log.TrimEnd());
                Status = outcome.Success ? $"{action} complete." : $"{action} failed — see log.";
            }
            row.RefreshState(_scanner);
        }

        private void AppendLog(string text)
        {
            string stamped = DateTime.Now.ToString("HH:mm:ss") + "  " + text;
            Application.Current.Dispatcher.Invoke(() => Log = string.IsNullOrEmpty(Log) ? stamped : Log + "\n" + stamped);
        }
    }

    public sealed class BlockRowViewModel : ViewModelBase
    {
        private readonly MainViewModel _parent;

        public BlockManifest Manifest { get; }
        public BlockDef Def { get; }

        public ICommand InstallCommand { get; }
        public ICommand RemoveCommand { get; }

        public BlockRowViewModel(MainViewModel parent, BlockManifest manifest, BlockDef def)
        {
            _parent = parent;
            Manifest = manifest;
            Def = def;
            InstallCommand = new RelayCommand(_ => _parent.InstallRow(this), _ => CanInstall);
            RemoveCommand = new RelayCommand(_ => _parent.RemoveRow(this), _ => CanRemove);
        }

        public string Code => Def.Code;
        public string Name => Def.Name;
        public string Category => Def.Category;
        public string Description => Def.Description;

        public Uri IconUri
        {
            get
            {
                string dir = Services.BlockCatalog.ResolveIconsDirectory();
                string p = System.IO.Path.Combine(dir, Code + ".svg");
                return System.IO.File.Exists(p) ? new Uri(p) : null;
            }
        }
        public bool HasIcon => IconUri != null;
        public string Milestone => string.IsNullOrEmpty(Def.Milestone) ? "" : Def.Milestone;
        public bool DllMissing => Manifest.DllPath == null || !System.IO.File.Exists(Manifest.DllPath);

        private RegistrationState _state = RegistrationState.None;
        public RegistrationState State
        {
            get => _state;
            private set { if (Set(ref _state, value)) RaiseLocalized(); }
        }

        private bool _busy;
        public bool Busy
        {
            get => _busy;
            set { if (Set(ref _busy, value)) { Raise(nameof(CanInstall)); Raise(nameof(CanRemove)); Raise(nameof(StatusText)); } }
        }

        public bool IsInstalled => State != RegistrationState.None;
        public bool CanInstall => !Busy && State != RegistrationState.Both && !DllMissing;
        public bool CanRemove => !Busy && State != RegistrationState.None;

        /// <summary>Re-raise the localized / state-dependent bindings.</summary>
        public void RaiseLocalized()
        {
            Raise(nameof(StatusText));
            Raise(nameof(StatusBrush));
            Raise(nameof(IsInstalled));
            Raise(nameof(CanInstall));
            Raise(nameof(CanRemove));
        }

        public string StatusText
        {
            get
            {
                if (Busy) return _parent.L["Working"];
                if (DllMissing) return _parent.L["DllMissing"];
                switch (State)
                {
                    case RegistrationState.Both: return _parent.L["InstalledBoth"];
                    case RegistrationState.Partial: return _parent.L["Partial"];
                    default: return _parent.L["NotInstalled"];
                }
            }
        }

        public Brush StatusBrush
        {
            get
            {
                if (DllMissing) return Brushes.OrangeRed;
                switch (State)
                {
                    case RegistrationState.Both: return new SolidColorBrush(Color.FromRgb(0x0E, 0x7C, 0x66));
                    case RegistrationState.Partial: return Brushes.DarkOrange;
                    default: return Brushes.Gray;
                }
            }
        }

        public void RefreshState(RegistryScanner scanner)
        {
            State = scanner.GetRegistrationState(Def.Clsid);
        }
    }
}
