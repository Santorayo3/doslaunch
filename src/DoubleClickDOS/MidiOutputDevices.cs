using System.Runtime.InteropServices;

namespace DoubleClickDOS;

/// <summary>Finds the Windows MIDI port that the MUNT driver exposes, so DOSBox can send its music to it.</summary>
internal static class MidiOutputDevices
{
    private const uint MmsyserrNoError = 0;

    /// <summary>
    /// Index of the MUNT ("MT-32 Synth Emulator") MIDI output device in the Windows Multimedia list,
    /// or <c>null</c> when the MUNT MIDI driver is not installed.
    /// </summary>
    public static int? FindMuntDeviceIndex()
    {
        uint count = midiOutGetNumDevs();
        for (uint index = 0; index < count; index++)
        {
            if (TryGetName(index, out string name) && IsMuntDevice(name))
            {
                Log.Write($"MUNT MIDI device found: \"{name}\" (index {index}).");
                return (int)index;
            }
        }

        return null;
    }

    /// <summary>The name the mt32emu WinMM driver registers itself under.</summary>
    internal static bool IsMuntDevice(string name) =>
        name.Contains("MT-32 Synth Emulator", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("mt32emu", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetName(uint deviceId, out string name)
    {
        var caps = new MidiOutCaps();
        uint result = midiOutGetDevCaps(new UIntPtr(deviceId), ref caps, (uint)Marshal.SizeOf<MidiOutCaps>());
        name = result == MmsyserrNoError ? caps.Name ?? string.Empty : string.Empty;
        return !string.IsNullOrWhiteSpace(name);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct MidiOutCaps
    {
        public ushort ManufacturerId;
        public ushort ProductId;
        public uint DriverVersion;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Name;

        public ushort Technology;
        public ushort Voices;
        public ushort Notes;
        public ushort ChannelMask;
        public uint Support;
    }

    [DllImport("winmm.dll")]
    private static extern uint midiOutGetNumDevs();

    [DllImport("winmm.dll", CharSet = CharSet.Ansi)]
    private static extern uint midiOutGetDevCaps(UIntPtr deviceId, ref MidiOutCaps caps, uint size);
}