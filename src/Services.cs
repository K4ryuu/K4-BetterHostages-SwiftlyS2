using SwiftlyS2.Shared.SchemaDefinitions;
using SwiftlyS2.Shared.Natives;
using System.Runtime.InteropServices;

namespace HostageRescue;

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate void CHostageFollowDelegate(nint hostagePtr, nint playerPawnPtr);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
internal delegate bool CHostageDropDelegate(nint hostagePtr, ref Vector dropPos, bool unknown);

internal class ActionState
{
    public ActionType Type { get; set; }
    public CHostage? TargetHostage { get; set; }
}

internal enum ActionType
{
    PickingUpHostage,
    DroppingHostage
}
