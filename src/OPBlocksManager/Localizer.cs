using System.Collections.Generic;
using System.Windows;

namespace OPBlocksManager
{
    /// <summary>
    /// Lightweight bilingual (English / Arabic) string provider. XAML binds text to
    /// <c>{Binding L[Key]}</c>; flipping <see cref="Lang"/> raises the indexer change
    /// so the whole UI re-reads its strings and flips layout direction (RTL for AR).
    /// </summary>
    public sealed class Localizer : ViewModelBase
    {
        private string _lang = "en";

        public string Lang
        {
            get => _lang;
            set
            {
                if (Set(ref _lang, value))
                {
                    Raise("Item[]");
                    Raise(nameof(IsArabic));
                    Raise(nameof(FlowDir));
                    Raise(nameof(LangButtonText));
                }
            }
        }

        public bool IsArabic => _lang == "ar";
        public FlowDirection FlowDir => IsArabic ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        public string LangButtonText => IsArabic ? "English" : "العربية";

        public void Toggle() => Lang = IsArabic ? "en" : "ar";

        public string this[string key]
        {
            get
            {
                if (Map.TryGetValue(key, out var pair))
                    return IsArabic ? pair.ar : pair.en;
                return key;
            }
        }

        private static readonly Dictionary<string, (string en, string ar)> Map =
            new Dictionary<string, (string, string)>
            {
                ["AppTitle"]          = ("ONE PROCESS Blocks", "بلوكات ون بروسيس"),
                ["AppSubtitle"]       = ("Install ONE PROCESS CAPE-OPEN blocks into Aspen Plus and DWSIM",
                                         "تثبيت بلوكات ون بروسيس (CAPE-OPEN) في Aspen Plus و DWSIM"),
                ["DetectedTitle"]     = ("Detected simulators", "المحاكيات المكتشَفة"),
                ["LibraryTitle"]      = ("Block library", "مكتبة البلوكات"),
                ["Refresh"]           = ("Refresh", "تحديث"),
                ["Install"]           = ("Install", "تثبيت"),
                ["Remove"]            = ("Remove", "إزالة"),
                ["InstallAll"]        = ("Install all", "تثبيت الكل"),
                ["RemoveAll"]         = ("Remove all", "إزالة الكل"),
                ["About"]             = ("About", "حول"),
                ["EnableAspen"]       = ("Enable in Aspen", "تفعيل في Aspen"),
                ["EnableDwsim"]       = ("Enable in DWSIM", "تفعيل في DWSIM"),
                ["DisableDwsim"]      = ("Disable in DWSIM", "تعطيل في DWSIM"),
                ["ActivityLog"]       = ("Activity log", "سجل النشاط"),
                ["ClearLog"]          = ("Clear", "مسح"),
                ["LibrarySubtitle"]   = ("Register a block to make it appear in the host's CAPE-OPEN palette.",
                                         "سجّل البلوك ليظهر في لوحة CAPE-OPEN داخل المحاكي."),
                ["RemoveAllTitle"]    = ("Remove all blocks?", "إزالة كل البلوكات؟"),
                ["RemoveAllPrompt"]   = ("This unregisters every ONE PROCESS block from Aspen (x64 + x86). Continue?",
                                         "سيؤدي هذا إلى إلغاء تسجيل كل بلوكات ون بروسيس من Aspen (x64 + x86). هل تريد المتابعة؟"),
                ["DwsimTitle"]        = ("ONE PROCESS — DWSIM", "ون بروسيس — DWSIM"),
                ["DwsimReadyTitle"]   = ("ONE PROCESS — DWSIM ready", "ون بروسيس — DWSIM جاهز"),
                ["Palette"]           = ("palette", "اللوحة"),
                ["StatusReady"]       = ("Ready.", "جاهز."),
                ["NotInstalled"]      = ("Not installed", "غير مثبّت"),
                ["InstalledBoth"]     = ("Installed (x64 + x86)", "مثبّت (x64 + x86)"),
                ["Partial"]           = ("Partially installed", "مثبّت جزئياً"),
                ["DllMissing"]        = ("DLL missing", "الملف مفقود"),
                ["Working"]           = ("Working…", "جارٍ العمل…"),
                ["AboutTitle"]        = ("About ONE PROCESS Blocks", "حول بلوكات ون بروسيس"),
                ["AboutMadeBy"]       = ("Designed & developed by Engineer Nawaf",
                                         "تصميم وتطوير: المهندس نواف"),
                ["AboutOrg"]          = ("ONE PROCESS Simulation", "ون بروسيس للمحاكاة"),
                ["AboutVersion"]      = ("Version", "الإصدار"),
                ["AboutClose"]        = ("Close", "إغلاق"),
                ["AboutTagline"]      = ("Custom CAPE-OPEN unit operations for process simulation.",
                                         "عمليات وحدات CAPE-OPEN مخصّصة لمحاكاة العمليات."),
            };
    }
}
