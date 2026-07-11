using System;
using System.IO;
using System.Reflection;

namespace System.Runtime.CompilerServices
{
    // net48 has no ModuleInitializerAttribute; declaring it locally makes the C# 9
    // [ModuleInitializer] feature work on .NET Framework.
    [AttributeUsage(AttributeTargets.Method, Inherited = false)]
    internal sealed class ModuleInitializerAttribute : Attribute { }
}

namespace OPBlocks.DWSIM
{
    /// <summary>
    /// DWSIM discovers unitops with Assembly.LoadFile, which does NOT probe the
    /// unitops folder for dependencies — only DWSIM's application base. Our
    /// OPBlocks.* dependencies live next to this adapter in the unitops folder,
    /// so resolve them from there. Assembly.LoadFile caches per path, so an
    /// assembly the scanner already loaded keeps a single identity.
    /// </summary>
    internal static class ModuleInit
    {
        private static int _installed;

        [System.Runtime.CompilerServices.ModuleInitializer]
        internal static void Install()
        {
            if (System.Threading.Interlocked.Exchange(ref _installed, 1) == 1) return;
            AppDomain.CurrentDomain.AssemblyResolve += ResolveFromOwnFolder;
        }

        private static Assembly ResolveFromOwnFolder(object sender, ResolveEventArgs args)
        {
            try
            {
                string name = new AssemblyName(args.Name).Name;
                if (!name.StartsWith("OPBlocks.", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(name, "CapeOpen", StringComparison.OrdinalIgnoreCase))
                    return null;

                string dir = Path.GetDirectoryName(typeof(ModuleInit).Assembly.Location);
                if (dir == null) return null;
                string candidate = Path.Combine(dir, name + ".dll");
                return File.Exists(candidate) ? Assembly.LoadFile(candidate) : null;
            }
            catch
            {
                return null; // never break the host's resolve chain
            }
        }
    }
}
