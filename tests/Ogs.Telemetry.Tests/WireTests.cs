using Ogs.Telemetry.Schema;
using Ogs.Telemetry.Wire;
using Xunit;

/// <summary>Canonical OGS encoding (the standard's implementation-capnproto.md,
/// Encoding rules): canonical form, zeroed unused storage, fixed field
/// presence — so equal content always yields equal bytes.</summary>
public class WireTests
{
    [Fact]
    public void Zero_Sized_Struct_Points_At_Offset_Minus_One()
    {
        // all-zero Vector3 truncates to 0 data words: struct pointer, offset −1
        Assert.Equal(new ulong[] { 0x0000_0000_FFFF_FFFC }, CanonicalEncoder.Encode(new Vector3()));
    }

    [Fact]
    public void Trailing_Zero_Data_Words_Are_Truncated()
    {
        // x = 1.0f in word 0; word 1 (z) is zero and truncated: data size 1
        Assert.Equal(new ulong[] { 0x0000_0001_0000_0000, 0x3F80_0000 },
                     CanonicalEncoder.Encode(new Vector3 { X = 1f }));
    }

    [Fact]
    public void Text_Is_A_Nul_Terminated_Byte_List()
    {
        var words = CanonicalEncoder.Encode(new NamedPoint { Name = "ab" });
        // root: 0 data words, 2 pointers — name, then position (always
        // written, so non-null even though it is all-zero)
        Assert.Equal(0x0002_0000_0000_0000UL, words[0]);
        Assert.Equal(1UL | (1UL << 2) | (2UL << 32) | (3UL << 35), words[1]); // text, 3 bytes
        Assert.Equal(0x0000_0000_FFFF_FFFCUL, words[2]);                    // zero-sized position
        Assert.Equal(0x62_61UL, words[3]);                                  // "ab\0"
        Assert.Equal(4, words.Length);
    }

    [Fact]
    public void Empty_List_Encodes_As_Null()
    {
        var withEmpty = new MotionObject { Name = "car", Channels = new List<ChannelValue>() };
        var withNull = new MotionObject { Name = "car" };
        Assert.Equal(CanonicalEncoder.Encode(withNull), CanonicalEncoder.Encode(withEmpty));
    }

    [Fact]
    public void Switching_Union_Variant_Clears_The_Old_Storage()
    {
        var reused = new ChannelValue { Id = 3, Number = 1234.5f };
        reused.Boolean = true;
        Assert.Equal(CanonicalEncoder.Encode(new ChannelValue { Id = 3, Boolean = true }),
                     CanonicalEncoder.Encode(reused));

        var ev = new Event { Id = 1, Name = "x" };
        ev.InitDirection().X = 1f;
        ev.SetNoDirection();
        Assert.Equal(CanonicalEncoder.Encode(new Event { Id = 1, Name = "x" }),
                     CanonicalEncoder.Encode(ev));
    }

    [Fact]
    public void Untouched_Optionality_Union_Is_Discriminant_Zero()
    {
        Assert.Equal(MotionObject.Variant.NoDynamics, new MotionObject().Which);
        Assert.Equal(Event.Variant.NoDirection, new Event().Which);
        Assert.Equal(ChannelDescriptor.Variant.NoRange, new ChannelDescriptor().Which);
    }

    [Fact]
    public void Text_And_Struct_Fields_Are_Always_Written()
    {
        // ObjectDescriptor: name, location, typeLabel, referencePoint are
        // always present even when empty; the lists after them are null
        var words = CanonicalEncoder.Encode(new ObjectDescriptor());
        ulong root = words[0];
        Assert.Equal(4UL, root >> 48); // pointer section truncated after referencePoint
        for (int i = 0; i < 4; i++)
            Assert.NotEqual(0UL, words[1 + (int)((root >> 32) & 0xFFFF) + i]);
    }

    [Fact]
    public void Equal_Content_Built_Differently_Encodes_Identically()
    {
        var a = new MotionObject { Name = "car", Position = { X = 1, Y = 2 } };
        a.InitBodyDynamics().SpecificForce.Y = 9.5f;

        var b = new MotionObject();
        b.InitKinematics().Velocity.X = 3;   // replaced below
        var bd = b.InitBodyDynamics();
        bd.SpecificForce.Y = 9.5f;
        b.Position.Y = 2; b.Position.X = 1;
        b.Name = "car";

        Assert.Equal(CanonicalEncoder.Encode(a), CanonicalEncoder.Encode(b));
    }
}
