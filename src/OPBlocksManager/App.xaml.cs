using System;
using System.IO;
using System.Text;
using System.Windows;
using OPBlocksManager.Services;

namespace OPBlocksManager
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Elevated re-entry: this instance was launched with runas to do the
            // actual COM registration. Run headless, write the result, exit.
            if (RegistrationWorker.IsWorkerInvocation(e.Args))
            {
                int code = RegistrationWorker.Run(e.Args);
                Shutdown(code);
                return;
            }

            // Headless self-check of the detection/catalog/status logic (used in CI
            // and during development): --selftest <outfile>.
            if (Array.IndexOf(e.Args, "--selftest") >= 0)
            {
                RunSelfTest(e.Args);
                Shutdown(0);
                return;
            }

            // Rasterise the equipment SVGs to PNGs (for the in-host WinForms editor,
            // which must not load WPF): --rasterize-icons <svgDir> <outDir> [size].
            if (Array.IndexOf(e.Args, "--rasterize-icons") >= 0)
            {
                RasterizeIcons(e.Args);
                Shutdown(0);
                return;
            }

            var window = new MainWindow();
            window.Show();
        }

        private static void RunSelfTest(string[] args)
        {
            var sb = new StringBuilder();
            try
            {
                var scanner = new RegistryScanner();
                sb.AppendLine("== Detected simulators ==");
                foreach (var s in scanner.DetectSimulators())
                    sb.AppendLine($"  [{s.Kind}] {s.Name} | {s.Version} | {s.Bitness} | {s.Path}");

                string dir = BlockCatalog.ResolveBlocksDirectory();
                sb.AppendLine($"== Block library: {dir} ==");
                foreach (var m in new BlockCatalog().Load(dir))
                    foreach (var b in m.Blocks)
                        sb.AppendLine($"  {b.Code} ({b.Name}) -> registration: {scanner.GetRegistrationState(b.Clsid)} | dll: {m.DllPath}");
            }
            catch (Exception ex)
            {
                sb.AppendLine("SELFTEST ERROR: " + ex);
            }

            int idx = Array.IndexOf(args, "--selftest");
            string outFile = (idx >= 0 && idx + 1 < args.Length) ? args[idx + 1] : null;
            if (!string.IsNullOrEmpty(outFile)) File.WriteAllText(outFile, sb.ToString());
        }

        private static void RasterizeIcons(string[] args)
        {
            int i = Array.IndexOf(args, "--rasterize-icons");
            string svgDir = (i + 1 < args.Length) ? args[i + 1] : null;
            string outDir = (i + 2 < args.Length) ? args[i + 2] : null;
            int size = (i + 3 < args.Length && int.TryParse(args[i + 3], out int s)) ? s : 128;
            if (svgDir == null || outDir == null) return;
            Directory.CreateDirectory(outDir);

            var log = new StringBuilder();
            int ok = 0;
            var settings = new SharpVectors.Renderers.Wpf.WpfDrawingSettings { IncludeRuntime = false, TextAsGeometry = true };
            var reader = new SharpVectors.Converters.FileSvgReader(settings);
            foreach (string svg in Directory.GetFiles(svgDir, "*.svg"))
            {
                try
                {
                    System.Windows.Media.DrawingGroup dg = reader.Read(svg);
                    var di = new System.Windows.Media.DrawingImage(dg);
                    var vis = new System.Windows.Media.DrawingVisual();
                    using (var dc = vis.RenderOpen())
                        dc.DrawImage(di, new System.Windows.Rect(0, 0, size, size));
                    var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(size, size, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                    rtb.Render(vis);
                    var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
                    enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
                    string png = Path.Combine(outDir, Path.GetFileNameWithoutExtension(svg) + ".png");
                    using (var fs = File.Create(png)) enc.Save(fs);
                    ok++;
                }
                catch (Exception ex) { log.AppendLine(Path.GetFileName(svg) + " : " + ex.GetType().Name + " " + ex.Message); }
            }
            try { File.WriteAllText(Path.Combine(outDir, "_rasterize.log"), "ok=" + ok + "\n" + log); } catch { }
        }
    }
}
