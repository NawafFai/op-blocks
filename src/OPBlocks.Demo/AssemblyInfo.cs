using System.Runtime.InteropServices;

// Expose only the explicitly-implemented CAPE-OPEN interfaces over COM — never an
// auto-generated class dispatch interface. The auto (AutoDispatch) class interface
// surfaces every public member of the CO-LaN base classes, which include pairs that
// differ only by case (e.g. CapeObjectBase.Parameters vs ICapeUtilities.parameters).
// Late-binding hosts (DWSIM's VB delete path, PowerShell) choke on that collision
// with a NullReferenceException / CLS error. ClassInterfaceType.None removes the
// ambiguity; hosts still reach every CAPE-OPEN interface via QueryInterface.
[assembly: ClassInterface(ClassInterfaceType.None)]
