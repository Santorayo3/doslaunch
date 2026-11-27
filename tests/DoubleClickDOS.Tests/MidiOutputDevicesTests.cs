namespace DoubleClickDOS.Tests;

public sealed class MidiOutputDevicesTests
{
    [Theory]
    [InlineData("MT-32 Synth Emulator", true)]
    [InlineData("mt32emu", true)]
    [InlineData("Microsoft GS Wavetable Synth", false)]
    [InlineData("MIDI Mapper", false)]
    public void IsMuntDevice_RecognisesTheMuntMidiPort(string name, bool expected)
    {
        Assert.Equal(expected, MidiOutputDevices.IsMuntDevice(name));
    }

    [Fact]
    public void FindMuntDeviceIndex_ReadsTheWindowsMidiDeviceList()
    {
        int? index = MidiOutputDevices.FindMuntDeviceIndex();

        Assert.True(index is null or >= 0);
    }
}
