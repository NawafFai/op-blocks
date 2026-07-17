using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;

/// <summary>
/// Per-USER COM registration (HKCU\Software\Classes) for the OP-Blocks DLLs —
/// no elevation, fully reversible with /u. Redirects the predefined
/// HKEY_CLASSES_ROOT handle to HKCU\Software\Classes for this process, then
/// runs the normal .NET RegistrationServices (which also invokes the
/// CapeUnitBase [ComRegisterFunction] that writes the CAPE-OPEN CATID keys).
/// </summary>
static class RegisterUser
{
    [DllImport("advapi32.dll")]
    static extern int RegOverridePredefKey(IntPtr hKey, IntPtr hNewKey);
    // predefined registry handles are SIGN-EXTENDED 32-bit values on x64
    static readonly IntPtr HKCR = new IntPtr(unchecked((int)0x80000000));

    static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("usage: RegisterUser <assembly.dll> [/u]");
            return 2;
        }
        string dll = System.IO.Path.GetFullPath(args[0]);
        bool unreg = args.Length > 1 && args[1].Equals("/u", StringComparison.OrdinalIgnoreCase);

        RegistryKey target = Registry.CurrentUser.CreateSubKey(@"Software\Classes");
        int rc = RegOverridePredefKey(HKCR, target.Handle.DangerousGetHandle());
        if (rc != 0) { Console.Error.WriteLine("RegOverridePredefKey failed: " + rc); return 3; }

        try
        {
            Assembly asm = Assembly.LoadFrom(dll);
            var rs = new RegistrationServices();
            bool ok = unreg
                ? rs.UnregisterAssembly(asm)
                : rs.RegisterAssembly(asm, AssemblyRegistrationFlags.SetCodeBase);
            Console.WriteLine((unreg ? "UNREGISTER " : "REGISTER ") + (ok ? "OK  " : "FAIL ")
                              + System.IO.Path.GetFileName(dll));
            return ok ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("ERROR " + System.IO.Path.GetFileName(dll) + ": " + ex.Message);
            return 1;
        }
        finally
        {
            RegOverridePredefKey(HKCR, IntPtr.Zero);
            target.Close();
        }
    }
}
