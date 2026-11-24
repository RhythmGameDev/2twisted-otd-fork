using System;
using System.Collections.Generic;
using System.Linq;
using OpenTabletDriver.Plugin.Devices;
using OpenTabletDriver.Plugin.Tablet;
using Xunit;

namespace OpenTabletDriver.Tests
{
    // Two (or more) of the same tablet (2Twisted fork, upstream issue #1666): the pieces Driver.MatchAll is
    // built from. The whole path (opening devices) is exercised end to end with emulated tablets instead.
    public class IdenticalTabletsTest
    {
        private sealed class FakeEndpoint : IDeviceEndpoint
        {
            public FakeEndpoint(string path, int vid = 0x256C, int pid = 0x006D) { DevicePath = path; VendorID = vid; ProductID = pid; }
            public int ProductID { get; }
            public int VendorID { get; }
            public int InputReportLength => 10;
            public int OutputReportLength => 0;
            public int FeatureReportLength => 0;
            public string? Manufacturer => "Fake";
            public string? ProductName => "Fake tablet";
            public string? FriendlyName => "Fake tablet";
            public string? SerialNumber => null;
            public string DevicePath { get; }
            public bool CanOpen => true;
            public IDictionary<string, string> DeviceAttributes { get; } = new Dictionary<string, string>();
            public IDeviceEndpointStream Open() => throw new NotSupportedException();
            public string? GetDeviceString(byte index) => null;
            public void OpenAndSendFeatureInit(byte[] initData) { }
            public void OpenAndSendOutputInit(byte[] initData) { }
        }

        private static readonly DeviceIdentifier First = new() { VendorID = 0x256C, ProductID = 0x006D };
        private static readonly DeviceIdentifier Second = new() { VendorID = 0x256C, ProductID = 0x006D, InputReportLength = 10 };

        private static List<(IDeviceEndpoint Endpoint, DeviceIdentifier Identifier)> Candidates(params (IDeviceEndpoint, DeviceIdentifier)[] items)
            => items.ToList();

        [Fact]
        public void OneTablet_IsOneGroup_InTheCandidatesOrder()
        {
            var a = new FakeEndpoint("path-a");
            var groups = Driver.GroupBySibling(Candidates((a, First), (a, Second)));
            Assert.Single(groups);
            Assert.Equal(new[] { First, Second }, groups[0].Select(c => c.Identifier));
        }

        [Fact]
        public void TwoIdenticalTablets_AreTwoGroups_InDevicePathOrder()
        {
            var b = new FakeEndpoint("path-b");
            var a = new FakeEndpoint("path-a");
            var groups = Driver.GroupBySibling(Candidates((b, First), (a, First)));
            Assert.Equal(2, groups.Count);
            Assert.Same(a, groups[0][0].Endpoint);
            Assert.Same(b, groups[1][0].Endpoint);
        }

        [Fact]
        public void Siblings_AreOnlyTheSameEndpoint_ForEndpointsThatCannotSay()
        {
            var a = new FakeEndpoint("path-a");
            Assert.True(Driver.AreSiblings(a, a));
            Assert.True(Driver.AreSiblings(a, new FakeEndpoint("path-a")));
            Assert.False(Driver.AreSiblings(a, new FakeEndpoint("path-b")));
            Assert.False(Driver.AreSiblings(new FakeEndpoint("x", pid: 1), new FakeEndpoint("y", pid: 2)));
        }

        [Fact]
        public void Renamed_KeepsEverythingButTheName()
        {
            var config = new TabletConfiguration
            {
                Name = "Gaomon S56K",
                Specifications = new TabletSpecifications
                {
                    Digitizer = new DigitizerSpecifications { MaxX = 2000, MaxY = 2000, Width = 20, Height = 20 },
                    Pen = new PenSpecifications(),
                },
                DigitizerIdentifiers = new List<DeviceIdentifier> { First },
                AuxiliaryDeviceIdentifiers = new List<DeviceIdentifier> { Second },
                Attributes = new Dictionary<string, string> { ["k"] = "v" },
            };
            var second = Driver.Renamed(config, "Gaomon S56K (2)");
            Assert.Equal("Gaomon S56K (2)", second.Name);
            Assert.Equal("Gaomon S56K", config.Name);
            Assert.Same(config.Specifications, second.Specifications);
            Assert.Same(config.DigitizerIdentifiers, second.DigitizerIdentifiers);
            Assert.Same(config.AuxiliaryDeviceIdentifiers, second.AuxiliaryDeviceIdentifiers);
            Assert.Same(config.Attributes, second.Attributes);
        }
    }
}
