using Microsoft.Extensions.Logging;
using SwiftlyS2.Shared;
using SwiftlyS2.Shared.Memory;
using SwiftlyS2.Shared.Plugins;
using SwiftlyS2.Shared.Sounds;

namespace HostageRescue;

[PluginMetadata(
    Id = "k4.betterhostages",
    Version = "1.0.0",
    Name = "K4 - Better Hostages",
    Author = "K4ryuu",
    Description = "Enables both Ts and CTs to pick up and drop hostages, allowing their positions to be tactically rearranged during gameplay."
)]
public partial class HostageRescuePlugin(ISwiftlyCore core) : BasePlugin(core)
{
    public const float PICKUP_RANGE = 62.0f;
    public const float PICKUP_DURATION = 1.0f;
    public const float DROP_DURATION = 1.0f;
    public const float MINIMUM_SAFE_DISTANCE = 35f;

    internal IUnmanagedFunction<CHostageFollowDelegate>? _cHostageFollow;
    internal IUnmanagedFunction<CHostageDropDelegate>? _cHostageDrop;

    internal readonly Dictionary<int, CancellationTokenSource> _playerProgressTimers = [];
    internal readonly Dictionary<int, CancellationTokenSource> _playerValidationTimers = [];
    internal readonly Dictionary<int, ActionState> _playerActionState = [];

    internal readonly SoundEvent _hostagePickupSound = new() { Name = "sounds/vo/hostage/hostage_led_by_ct.vsnd_c", Volume = 1.0f };
    internal readonly SoundEvent _hostageDropSound = new() { Name = "sounds/vo/hostage/hostage_being_rescued.vsnd_c", Volume = 1.0f };

    public override void Load(bool hotReload)
    {
        InitializeNativeFunctions();
        Core.Event.OnClientKeyStateChanged += OnKeyStateChange;
    }

    private void InitializeNativeFunctions()
    {
        try
        {
            var followAddr = Core.GameData.GetSignature("CHostage::Follow");
            if (followAddr != nint.Zero)
                _cHostageFollow = Core.Memory.GetUnmanagedFunctionByAddress<CHostageFollowDelegate>(followAddr);
            else
                Core.Logger.LogWarning("Could not find CHostage::Follow signature");

            var dropAddr = Core.GameData.GetSignature("CHostage::DropHostage");
            if (dropAddr != nint.Zero)
                _cHostageDrop = Core.Memory.GetUnmanagedFunctionByAddress<CHostageDropDelegate>(dropAddr);
            else
                Core.Logger.LogWarning("Could not find CHostage::DropHostage signature");
        }
        catch (Exception ex)
        {
            Core.Logger.LogWarning($"Error initializing native functions: {ex.Message}");
        }
    }

    public override void Unload()
    {
        foreach (var timer in _playerProgressTimers.Values)
            timer.Cancel();

        foreach (var timer in _playerValidationTimers.Values)
            timer.Cancel();

        _playerProgressTimers.Clear();
        _playerValidationTimers.Clear();
        _playerActionState.Clear();
    }
}
