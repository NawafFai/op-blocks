using System;
using System.Reflection;

/// <summary>
/// Live COM activation gate: creates every OP-Blocks ProgID through the real
/// COM path (registry CLSID -> mscoree -> codebase assembly), then reads the
/// CAPE-OPEN identification and port/parameter counts through the object —
/// the same activation route Aspen Plus uses.
/// </summary>
static class ActivationGate
{
    static int Main(string[] args)
    {
        string[] progIds = {
            "OPBlocks.RO", "OPBlocks.NF", "OPBlocks.UF", "OPBlocks.FO", "OPBlocks.PRO",
            "OPBlocks.EvapPond", "OPBlocks.MembraneDistillation", "OPBlocks.MED",
            "OPBlocks.MSF", "OPBlocks.MVC",
            "OPBlocks.ED", "OPBlocks.EDI", "OPBlocks.CDI", "OPBlocks.ChlorAlkali", "OPBlocks.IX",
            "OPBlocks.DLE", "OPBlocks.SX", "OPBlocks.GAC", "OPBlocks.Cryst", "OPBlocks.PPT",
            "OPBlocks.PEM", "OPBlocks.AEL", "OPBlocks.FuelCell", "OPBlocks.RPB", "OPBlocks.UVAOP",
        };
        int ok = 0, fail = 0;
        foreach (string progId in progIds)
        {
            try
            {
                Type t = Type.GetTypeFromProgID(progId, throwOnError: true);
                object o = Activator.CreateInstance(t);
                string name = (string)o.GetType().InvokeMember("ComponentName",
                    BindingFlags.GetProperty, null, o, null);
                object ports = o.GetType().InvokeMember("Ports",
                    BindingFlags.GetProperty, null, o, null);
                int nPorts = (int)ports.GetType().InvokeMember("Count",
                    BindingFlags.GetProperty, null, ports, null);
                object pars = o.GetType().InvokeMember("Parameters",
                    BindingFlags.GetProperty, null, o, null);
                int nPars = (int)pars.GetType().InvokeMember("Count",
                    BindingFlags.GetProperty, null, pars, null);
                Console.WriteLine("ACTIVATED  {0,-30} -> {1,-12} ports={2} params={3}",
                    progId, name, nPorts, nPars);
                ok++;
            }
            catch (Exception ex)
            {
                Console.WriteLine("FAILED     {0,-30} -> {1}", progId,
                    ex.InnerException != null ? ex.InnerException.Message : ex.Message);
                fail++;
            }
        }
        Console.WriteLine("=== COM activation gate: {0} OK, {1} FAILED of {2} ===", ok, fail, ok + fail);
        return fail == 0 ? 0 : 1;
    }
}
