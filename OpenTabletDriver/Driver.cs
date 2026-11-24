using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using OpenTabletDriver.Devices.HidSharpBackend;
using OpenTabletDriver.Interop;
using OpenTabletDriver.Plugin;
using OpenTabletDriver.Plugin.Components;
using OpenTabletDriver.Plugin.Devices;
using OpenTabletDriver.Plugin.Tablet;

namespace OpenTabletDriver
{
    public class Driver : IDriver, IDisposable
    {
        public Driver(ICompositeDeviceHub deviceHub, IReportParserProvider reportParserProvider, IDeviceConfigurationProvider configurationProvider)
        {
            CompositeDeviceHub = deviceHub;
            _reportParserProvider = reportParserProvider;
            _deviceConfigurationProvider = configurationProvider;
        }

        private readonly IReportParserProvider _reportParserProvider;
        private readonly IDeviceConfigurationProvider _deviceConfigurationProvider;
        private readonly object _detectSync = new object();
        private ImmutableArray<InputDeviceTree> _inputDeviceTrees = ImmutableArray<InputDeviceTree>.Empty;

        public event EventHandler<IEnumerable<TabletReference>>? TabletsChanged;

        public ICompositeDeviceHub CompositeDeviceHub { get; }
        public ImmutableArray<InputDeviceTree> InputDevices => _inputDeviceTrees;
        public IEnumerable<TabletReference> Tablets => InputDevices.Select(c => c.CreateReference());

        private Dictionary<IDeviceEndpoint, Dictionary<byte, string?>> DeviceStringCache = [];

        public IReportParser<IDeviceReport> GetReportParser(DeviceIdentifier identifier)
        {
            return _reportParserProvider.GetReportParser(identifier.ReportParser);
        }

        public IEnumerable<int> KnownVendorIDs => (
            from configuration in _deviceConfigurationProvider.TabletConfigurations
            from identifier in configuration.DigitizerIdentifiers.Concat(configuration.AuxiliaryDeviceIdentifiers ??
                                                                         Enumerable.Empty<DeviceIdentifier>())
            select identifier.VendorID).Distinct();

        public virtual bool Detect()
        {
            lock (_detectSync)
            {
                DeviceStringCache = []; // reset cache on redetect

                bool success = false;

                Log.Write("Detect", "Searching for tablets...");

                var treeBuilder = ImmutableArray.CreateBuilder<InputDeviceTree>();
                foreach (var config in _deviceConfigurationProvider.TabletConfigurations)
                {
                    foreach (var tree in MatchAll(config))
                    {
                        success = true;
                        treeBuilder.Add(tree);

                        tree.Disconnected += (sender, e) =>
                        {
                            tree.OutputMode?.Dispose();

                            // save the immutable array for later use
                            Unsafe.SkipInit(out ImmutableArray<InputDeviceTree> updatedTrees);
                            ImmutableInterlocked.Update(ref _inputDeviceTrees, (trees, tree) =>
                            {
                                updatedTrees = trees.Remove(tree);
                                return updatedTrees;
                            }, tree);

                            // use here, we do this to avoid using an _inputDeviceTrees that may have been updated
                            TabletsChanged?.Invoke(this, updatedTrees.Select(c => c.CreateReference()));
                        };
                    }
                }

                // atomically update InputDevices
                var oldDevices = _inputDeviceTrees;
                _inputDeviceTrees = treeBuilder.ToImmutable();
                DisposeDevices(oldDevices);
                TabletsChanged?.Invoke(this, Tablets);

                if (!success)
                {
                    Log.Write("Detect", "No tablets were detected.");
                }

                return success;
            }
        }

        // TWO (OR MORE) OF THE SAME TABLET (2Twisted fork, upstream issue #1666). Upstream 0.6 took the first
        // matching device for each configuration and left the rest unused, so two identical tablets were one.
        // Here every physical device that matches a configuration becomes its own tablet:
        //   - the digitizer endpoints that match are grouped by physical device (IsSibling, as in 0.7), and each
        //     group yields one digitizer, chosen exactly as before (identifiers in order, the first that opens);
        //   - each tablet's auxiliary device is the one on the same physical device;
        //   - the first tablet keeps the configuration's name, so a single tablet (and its saved profile) is
        //     exactly as before; the others are "Name (2)", "Name (3)"... Profiles, the UX's tablet list and
        //     output-mode plugins all tell tablets apart by name, so each gets its own area and output mode.
        //   - the order is the device path's, which follows the USB port: the same tablets in the same ports
        //     keep their names across restarts.
        protected virtual IEnumerable<InputDeviceTree> MatchAll(TabletConfiguration config)
        {
            Log.Debug("Detect", $"Searching for tablet '{config.Name}'");
            var trees = new List<InputDeviceTree>();
            try
            {
                var physical = GroupBySibling(MatchingEndpoints(config, config.DigitizerIdentifiers));
                if (physical.Count > 1)
                    Log.Write("Detect", $"Found {physical.Count} '{config.Name}' tablets; each is its own tablet.");

                var auxCandidates = (config.AuxiliaryDeviceIdentifiers?.Count ?? 0) > 0
                    ? MatchingEndpoints(config, config.AuxiliaryDeviceIdentifiers!)
                    : new List<(IDeviceEndpoint Endpoint, DeviceIdentifier Identifier)>();
                var usedAux = new HashSet<IDeviceEndpoint>();

                foreach (var group in physical)
                {
                    var tabletConfig = trees.Count == 0 ? config : Renamed(config, $"{config.Name} ({trees.Count + 1})");
                    if (OpenFirst(tabletConfig, group) is not InputDevice digitizer)
                        continue;

                    Log.Write("Detect", $"Found tablet '{tabletConfig.Name}'");
                    var devices = new List<InputDevice> { digitizer };

                    if (auxCandidates.Count > 0)
                    {
                        var siblings = auxCandidates.Where(a => !usedAux.Contains(a.Endpoint) && group.Any(d => AreSiblings(d.Endpoint, a.Endpoint))).ToList();
                        // One tablet and no sibling information (an endpoint type that cannot say): the old rule,
                        // the first auxiliary device that matches.
                        if (siblings.Count == 0 && physical.Count == 1)
                            siblings = auxCandidates.ToList();

                        if (OpenFirst(tabletConfig, siblings) is InputDevice aux)
                        {
                            devices.Add(aux);
                            usedAux.Add(aux.Endpoint);
                        }
                        else
                            Log.Write("Detect", "Failed to find auxiliary device, express keys may be unavailable.", LogLevel.Warning);
                    }

                    trees.Add(new InputDeviceTree(tabletConfig, devices));
                }
            }
            catch (IOException iex) when (iex.Message.Contains("Unable to open HID class device")
                && SystemInterop.CurrentPlatform == PluginPlatform.Linux)
            {
                Log.Write(
                    "Driver",
                    "The current user does not have the permissions to open the device stream. " +
                    "Follow the instructions from https://opentabletdriver.net/Wiki/FAQ/Linux#fail-device-streams to resolve this issue.",
                    LogLevel.Error
                );
            }
            catch (ArgumentOutOfRangeException aex) when (aex.Message.Contains("Value range is [0, 15]")
                && SystemInterop.CurrentPlatform == PluginPlatform.Linux)
            {
                Log.Write(
                    "Driver",
                    "Device is currently in use by another kernel module. " +
                    "Follow the instructions from https://opentabletdriver.net/Wiki/FAQ/Linux#argumentoutofrangeexception to resolve this issue.",
                    LogLevel.Error
                );
            }
            catch (Exception ex)
            {
                Log.Exception(ex);
            }
            return trees;
        }

        // Every endpoint that matches, identifier by identifier in the configuration's order (an endpoint that
        // matches two identifiers is kept under the first, as before).
        private List<(IDeviceEndpoint Endpoint, DeviceIdentifier Identifier)> MatchingEndpoints(TabletConfiguration config, IList<DeviceIdentifier> identifiers)
        {
            var found = new List<(IDeviceEndpoint Endpoint, DeviceIdentifier Identifier)>();
            foreach (var identifier in identifiers)
                foreach (var device in GetMatchingDevices(config, identifier))
                    if (!found.Any(f => f.Endpoint == device))
                        found.Add((device, identifier));
            return found;
        }

        // The candidates, one group per physical device, each group in the candidates' order, the groups in
        // device-path order.
        internal static List<List<(IDeviceEndpoint Endpoint, DeviceIdentifier Identifier)>> GroupBySibling(
            List<(IDeviceEndpoint Endpoint, DeviceIdentifier Identifier)> candidates)
        {
            var groups = new List<List<(IDeviceEndpoint Endpoint, DeviceIdentifier Identifier)>>();
            foreach (var candidate in candidates)
            {
                var group = groups.FirstOrDefault(g => g.Any(c => AreSiblings(c.Endpoint, candidate.Endpoint)));
                if (group == null)
                    groups.Add(group = new List<(IDeviceEndpoint Endpoint, DeviceIdentifier Identifier)>());
                group.Add(candidate);
            }
            return groups.OrderBy(g => g.Min(c => c.Endpoint.DevicePath ?? string.Empty), StringComparer.Ordinal).ToList();
        }

        // Two endpoints of one physical device. HidSharp knows (the device's parent); for any other endpoint
        // type (WinUSB, plugin device hubs) only the very same endpoint counts, the way 0.7 treats WinUSB.
        internal static bool AreSiblings(IDeviceEndpoint a, IDeviceEndpoint b)
        {
            if (ReferenceEquals(a, b) || a.DevicePath == b.DevicePath)
                return true;
            if (a.VendorID != b.VendorID || a.ProductID != b.ProductID)
                return false;
            return a is HidSharpEndpoint ha && b is HidSharpEndpoint hb && ha.IsSiblingOf(hb);
        }

        // The first of these that opens, as the old MatchDevice did.
        private InputDevice? OpenFirst(TabletConfiguration config, IEnumerable<(IDeviceEndpoint Endpoint, DeviceIdentifier Identifier)> candidates)
        {
            foreach (var (endpoint, identifier) in candidates)
            {
                try
                {
                    return new InputDevice(this, endpoint, config, identifier);
                }
                catch (Exception ex)
                {
                    Log.Exception(ex, LogLevel.Warning);
                }
            }
            return null;
        }

        // The same configuration under another name, for the second and later tablets of one model.
        internal static TabletConfiguration Renamed(TabletConfiguration config, string name) => new TabletConfiguration
        {
            Name = name,
            Specifications = config.Specifications,
            DigitizerIdentifiers = config.DigitizerIdentifiers,
            AuxiliaryDeviceIdentifiers = config.AuxiliaryDeviceIdentifiers,
            Attributes = config.Attributes,
        };

        private IDeviceEndpoint[] GetMatchingDevices(TabletConfiguration configuration, DeviceIdentifier identifier)
        {
            return [.. from device in CompositeDeviceHub.GetDevices()
                   where identifier.VendorID == device.VendorID
                   where identifier.ProductID == device.ProductID
                   where device.CanOpen
                   where identifier.InputReportLength == null || identifier.InputReportLength == device.InputReportLength
                   where identifier.OutputReportLength == null || identifier.OutputReportLength == device.OutputReportLength
                   where identifier.FeatureReportLength == null || identifier.FeatureReportLength == device.FeatureReportLength
                   where DeviceMatchesStrings(device, identifier.DeviceStrings, DeviceStringCache)
                   where DeviceMatchesAttribute(device, identifier.Attributes, configuration.Attributes)
                   select device];
        }

        private static bool DeviceMatchesStrings(IDeviceEndpoint device, Dictionary<byte, string>? deviceStrings, Dictionary<IDeviceEndpoint, Dictionary<byte, string?>> stringCache)
        {
            if (deviceStrings == null || deviceStrings.Count == 0)
                return true;

            // Iterate through each device string, if one doesn't match then its the wrong configuration.
            foreach (var matchQuery in deviceStrings)
            {
                try
                {
                    string? deviceString = null;
                    if (stringCache.TryGetValue(device, out var deviceCachedStrings) && deviceCachedStrings.TryGetValue(matchQuery.Key, out var cacheDeviceString))
                    {
                        if (cacheDeviceString == null)
                        {
                            Log.Write("Detect", $"Cached null for index {matchQuery.Key}, skipping", LogLevel.Debug);
                            return false;
                        }
                        deviceString = cacheDeviceString;
                    }

                    deviceString ??= device.GetDeviceString(matchQuery.Key);

                    if (!stringCache.TryAdd(device, new Dictionary<byte, string?> { { matchQuery.Key, deviceString } }))
                        stringCache[device].TryAdd(matchQuery.Key, deviceString);

                    // nullcheck after cache update to ensure nulls are cached
                    if (deviceString == null)
                        throw new IOException($"Unable to look up string index {matchQuery.Key}");

                    var pattern = matchQuery.Value;
                    if (!Regex.IsMatch(deviceString, pattern))
                        return false;
                }
                catch (Exception ex)
                {
                    Log.Exception(ex, LogLevel.Debug);
                    return false;
                }
            }
            return true;
        }

        private static bool DeviceMatchesAttribute(IDeviceEndpoint device, Dictionary<string, string>? identifier_attributes, Dictionary<string, string>? config_attributes)
        {
            var attributes = new Dictionary<string, string>(identifier_attributes ?? Enumerable.Empty<KeyValuePair<string, string>>());

            if (config_attributes != null)
            {
                foreach (var kvp in config_attributes)
                    attributes.TryAdd(kvp.Key, kvp.Value);
            }

            // Windows only configuration attribute.
            if (SystemInterop.CurrentPlatform == PluginPlatform.Windows)
            {
                if (device is HidSharpEndpoint
                    && attributes.TryGetValue("WinUsage", out var winUsage)
                    && !Regex.IsMatch(device.DevicePath, $"&col{winUsage}"))
                {
                    // If it isn't a match there is no point proceeding.
                    return false;
                }
            }

            if (attributes.TryGetValue("HidReports", out var hidReports)
                && device.DeviceAttributes.TryGetValue("HID_REPORTS", out var usbHidReports)
                && !Regex.IsMatch(usbHidReports, hidReports))
            {
                return false; // HidReports specified and no HID Reports match.
            }

            if (!attributes.TryGetValue("Interface", out var identifierInterface))
                return true; // No interface specified, match.

            if (!device.DeviceAttributes.TryGetValue("USB_INTERFACE_NUMBER", out var usbInterface))
                return false; // Device doesn't have an interface number, not a match.

            return identifierInterface == usbInterface;
        }

        private static void DisposeDevices(ImmutableArray<InputDeviceTree> trees)
        {
            foreach (var tree in trees)
                foreach (var device in tree.InputDevices.ToArray())
                    device.Dispose();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private bool _isDisposed;

        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed) return;

            if (disposing)
            {
                DisposeDevices(_inputDeviceTrees);
            }

            _isDisposed = true;
        }
    }
}
