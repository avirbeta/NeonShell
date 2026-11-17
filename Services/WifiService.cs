using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace NeonShell.Services
{
    public class WifiNetwork
    {
        public string Ssid { get; set; } = "";
        public int Signal { get; set; }
        public bool Secure { get; set; }
        public bool IsSaved { get; set; }
        public string AuthName { get; set; } = "";
        public string SignalText { get; set; } = "▂▂▂▂";
    }

    /// <summary>
    /// Управление Wi-Fi через штатный Windows API (wlanapi.dll):
    /// список сетей, уровень сигнала, подключение (в т.ч. с паролем),
    /// отключение и забывание профилей. Без прав администратора.
    /// </summary>
    public class WifiService : IDisposable
    {
        public bool Available { get; private set; }
        private IntPtr _client = IntPtr.Zero;

        public WifiService()
        {
            try
            {
                uint result = NativeWifi.WlanOpenHandle(2, IntPtr.Zero, out _, out _client);
                Available = result == 0 && _client != IntPtr.Zero;
            }
            catch { Available = false; }
        }

        public void Dispose()
        {
            try
            {
                if (_client != IntPtr.Zero) NativeWifi.WlanCloseHandle(_client, IntPtr.Zero);
            }
            catch { }
            _client = IntPtr.Zero;
        }

        private Guid GetInterfaceGuid()
        {
            if (NativeWifi.WlanEnumInterfaces(_client, IntPtr.Zero, out IntPtr listPtr) != 0)
                return Guid.Empty;
            try
            {
                var list = Marshal.PtrToStructure<NativeWifi.WLAN_INTERFACE_INFO_LIST>(listPtr);
                if (list.dwNumberOfItems == 0) return Guid.Empty;
                var info = Marshal.PtrToStructure<NativeWifi.WLAN_INTERFACE_INFO>(list.InterfaceInfo);
                return info.InterfaceGuid;
            }
            finally
            {
                NativeWifi.WlanFreeMemory(listPtr);
            }
        }

        /// <summary>Список доступных сетей (сигнал, защита, сохранённость).</summary>
        public List<WifiNetwork> GetNetworks()
        {
            var result = new List<WifiNetwork>();
            if (!Available) return result;
            try
            {
                Guid iface = GetInterfaceGuid();
                if (iface == Guid.Empty) return result;
                var saved = new HashSet<string>(GetSavedProfiles(), StringComparer.OrdinalIgnoreCase);

                if (NativeWifi.WlanGetAvailableNetworkList(_client, ref iface, 0, IntPtr.Zero, out IntPtr listPtr) != 0)
                    return result;
                try
                {
                    var list = Marshal.PtrToStructure<NativeWifi.WLAN_AVAILABLE_NETWORK_LIST>(listPtr);
                    int size = Marshal.SizeOf<NativeWifi.WLAN_AVAILABLE_NETWORK>();
                    for (uint i = 0; i < list.dwNumberOfItems; i++)
                    {
                        var net = Marshal.PtrToStructure<NativeWifi.WLAN_AVAILABLE_NETWORK>(
                            IntPtr.Add(list.Network, (int)(i * size)));
                        string ssid = GetSsid(net.dot11Ssid);
                        if (string.IsNullOrEmpty(ssid)) continue;
                        result.Add(new WifiNetwork
                        {
                            Ssid = ssid,
                            Signal = (int)net.wlanSignalQuality,
                            Secure = net.bSecurityEnabled,
                            IsSaved = saved.Contains(ssid),
                            AuthName = AuthNameOf(net.dot11DefaultAuthAlgorithm, net.bSecurityEnabled),
                            SignalText = SignalBars(net.wlanSignalQuality),
                        });
                    }
                }
                finally
                {
                    NativeWifi.WlanFreeMemory(listPtr);
                }
            }
            catch { }
            return result;
        }

        public List<string> GetSavedProfiles()
        {
            var result = new List<string>();
            if (!Available) return result;
            try
            {
                Guid iface = GetInterfaceGuid();
                if (iface == Guid.Empty) return result;
                if (NativeWifi.WlanGetProfileList(_client, ref iface, IntPtr.Zero, out IntPtr listPtr) != 0)
                    return result;
                try
                {
                    var list = Marshal.PtrToStructure<NativeWifi.WLAN_PROFILE_INFO_LIST>(listPtr);
                    int size = Marshal.SizeOf<NativeWifi.WLAN_PROFILE_INFO>();
                    for (uint i = 0; i < list.dwNumberOfItems; i++)
                    {
                        var profile = Marshal.PtrToStructure<NativeWifi.WLAN_PROFILE_INFO>(
                            IntPtr.Add(list.ProfileInfo, (int)(i * size)));
                        result.Add(profile.strProfileName);
                    }
                }
                finally
                {
                    NativeWifi.WlanFreeMemory(listPtr);
                }
            }
            catch { }
            return result;
        }

        /// <summary>Имя текущей сети (null, если не подключены).</summary>
        public string GetCurrentSsid()
        {
            if (!Available) return null;
            try
            {
                Guid iface = GetInterfaceGuid();
                if (iface == Guid.Empty) return null;
                const uint WLAN_INTF_OPCODE_CURRENT_CONNECTION = 7;
                if (NativeWifi.WlanQueryInterface(_client, ref iface,
                        WLAN_INTF_OPCODE_CURRENT_CONNECTION, IntPtr.Zero,
                        out _, out IntPtr data, IntPtr.Zero) != 0)
                    return null;
                try
                {
                    var attrs = Marshal.PtrToStructure<NativeWifi.WLAN_CONNECTION_ATTRIBUTES>(data);
                    if (attrs.isState != NativeWifi.WLAN_INTERFACE_STATE.Connected)
                        return null;
                    string ssid = GetSsid(attrs.dot11Ssid);
                    return string.IsNullOrEmpty(ssid) ? attrs.strProfileName : ssid;
                }
                finally
                {
                    NativeWifi.WlanFreeMemory(data);
                }
            }
            catch { }
            return null;
        }

        /// <summary>Подключиться к открытой сети.</summary>
        public bool ConnectOpen(string ssid)
        {
            if (!Available) return false;
            return ConnectCore(ssid, null, IntPtr.Zero);
        }

        /// <summary>Подключиться к защищённой сети с паролем.</summary>
        public bool ConnectSecured(string ssid, string password, string authName)
        {
            if (!Available) return false;
            string auth = MapAuth(authName);
            string xml =
                "<?xml version=\"1.0\"?>" +
                "<WLANProfile xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v1\">" +
                $"<name>{Escape(ssid)}</name>" +
                "<SSIDConfig><SSID>" +
                $"<name>{Escape(ssid)}</name>" +
                "</SSID></SSIDConfig>" +
                "<connectionType>ESS</connectionType>" +
                "<connectionMode>auto</connectionMode>" +
                "<MSM><security>" +
                "<authEncryption>" +
                $"<authentication>{auth}</authentication>" +
                "<encryption>AES</encryption>" +
                "<useOneX>false</useOneX>" +
                "</authEncryption>" +
                "<sharedKey>" +
                "<keyType>passPhrase</keyType>" +
                "<protected>false</protected>" +
                $"<keyMaterial>{Escape(password)}</keyMaterial>" +
                "</sharedKey>" +
                "</security></MSM>" +
                "<MacRandomization xmlns=\"http://www.microsoft.com/networking/WLAN/profile/v3\">" +
                "<enableRandomization>false</enableRandomization>" +
                "</MacRandomization>" +
                "</WLANProfile>";
            return ConnectCore(ssid, xml, IntPtr.Zero);
        }

        private bool ConnectCore(string ssid, string profileXml, IntPtr ssidPtr)
        {
            try
            {
                Guid iface = GetInterfaceGuid();
                if (iface == Guid.Empty) return false;

                var parameters = new NativeWifi.WLAN_CONNECTION_PARAMETERS
                {
                    dot11BssType = NativeWifi.DOT11_BSS_TYPE.infrastructure,
                    dwFlags = 0,
                };

                if (profileXml != null)
                {
                    parameters.wlanConnectionMode = NativeWifi.WLAN_CONNECTION_MODE.temporary_profile;
                    parameters.strProfile = profileXml;
                    parameters.pDot11Ssid = IntPtr.Zero;
                }
                else
                {
                    parameters.wlanConnectionMode = NativeWifi.WLAN_CONNECTION_MODE.discovery_unsecure;
                    parameters.strProfile = null;
                    var dot11Ssid = new NativeWifi.DOT11_SSID
                    {
                        uSSIDLength = (uint)Math.Min(32, Encoding.UTF8.GetByteCount(ssid)),
                        ucSSID = new byte[32],
                    };
                    byte[] bytes = Encoding.UTF8.GetBytes(ssid);
                    Array.Copy(bytes, dot11Ssid.ucSSID, dot11Ssid.uSSIDLength);
                    IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf<NativeWifi.DOT11_SSID>());
                    try
                    {
                        Marshal.StructureToPtr(dot11Ssid, ptr, false);
                        parameters.pDot11Ssid = ptr;
                        return NativeWifi.WlanConnect(_client, ref iface, ref parameters, IntPtr.Zero) == 0;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(ptr);
                    }
                }

                return NativeWifi.WlanConnect(_client, ref iface, ref parameters, IntPtr.Zero) == 0;
            }
            catch { return false; }
        }

        public void Disconnect()
        {
            try
            {
                Guid iface = GetInterfaceGuid();
                if (iface == Guid.Empty) return;
                NativeWifi.WlanDisconnect(_client, ref iface, IntPtr.Zero);
            }
            catch { }
        }

        public void Forget(string profileName)
        {
            try
            {
                Guid iface = GetInterfaceGuid();
                if (iface == Guid.Empty) return;
                NativeWifi.WlanDeleteProfile(_client, ref iface, profileName, IntPtr.Zero);
            }
            catch { }
        }

        private static string GetSsid(NativeWifi.DOT11_SSID ssid)
        {
            try
            {
                if (ssid.uSSIDLength == 0 || ssid.uSSIDLength > 32) return null;
                return Encoding.UTF8.GetString(ssid.ucSSID, 0, (int)ssid.uSSIDLength);
            }
            catch { return null; }
        }

        private static string AuthNameOf(NativeWifi.DOT11_AUTH_ALGORITHM auth, bool secure)
        {
            if (!secure) return "Открытая";
            return auth switch
            {
                NativeWifi.DOT11_AUTH_ALGORITHM.WPA_PSK => "WPA-PSK",
                NativeWifi.DOT11_AUTH_ALGORITHM.RSNA_PSK => "WPA2-PSK",
                NativeWifi.DOT11_AUTH_ALGORITHM.WPA3_SAE => "WPA3",
                NativeWifi.DOT11_AUTH_ALGORITHM.WPA3 => "WPA3",
                NativeWifi.DOT11_AUTH_ALGORITHM.OpenSystem => "WEP",
                NativeWifi.DOT11_AUTH_ALGORITHM.SharedKey => "WEP",
                _ => "WPA2-PSK",
            };
        }

        public static string SignalBars(uint quality)
        {
            if (quality >= 75) return "▂▄▆█";
            if (quality >= 50) return "▂▄▆▂";
            if (quality >= 25) return "▂▄▂▂";
            return "▂▂▂▂";
        }

        private static string MapAuth(string authName) => authName switch
        {
            "WPA3" => "WPA3SAE",
            "WPA-PSK" => "WPA2PSK",
            _ => "WPA2PSK",
        };

        private static string Escape(string s)
            => (s ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
                .Replace("\"", "&quot;").Replace("'", "&apos;");
    }

    /// <summary>P/Invoke обёртка wlanapi.dll.</summary>
    internal static class NativeWifi
    {
        public const uint ERROR_SUCCESS = 0;

        public enum WLAN_INTERFACE_STATE
        {
            NotReady = 0, Connected = 1, AdHocNetworkFormed = 2, Disconnecting = 3,
            Disconnected = 4, Associating = 5, Discovering = 6, Authenticating = 7,
        }

        public enum WLAN_CONNECTION_MODE
        {
            profile = 0, temporary_profile = 1, discovery_secure = 2,
            discovery_unsecure = 3, auto = 4, invalid = 5,
        }

        public enum DOT11_BSS_TYPE { infrastructure = 1, independent = 2, any = 3 }

        public enum DOT11_AUTH_ALGORITHM
        {
            OpenSystem = 1, SharedKey = 2, WPA = 3, WPA_PSK = 4, WPA_None = 5,
            RSNA = 6, RSNA_PSK = 7, WPA3 = 8, WPA3_SAE = 9, OWE = 10, WPA3_ENT_192 = 11,
        }

        public enum DOT11_CIPHER_ALGORITHM
        {
            None = 0, WEP40 = 1, TKIP = 2, CCMP = 4, WEP104 = 5,
            WPA_UseGroup = 256, RSN_UseGroup = 256, WEP = 257,
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DOT11_SSID
        {
            public uint uSSIDLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] ucSSID;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WLAN_INTERFACE_INFO_LIST
        {
            public uint dwNumberOfItems;
            public uint dwIndex;
            public IntPtr InterfaceInfo;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WLAN_INTERFACE_INFO
        {
            public Guid InterfaceGuid;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strInterfaceDescription;
            public WLAN_INTERFACE_STATE isState;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct WLAN_AVAILABLE_NETWORK_LIST
        {
            public uint dwNumberOfItems;
            public uint dwIndex;
            public IntPtr Network;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WLAN_AVAILABLE_NETWORK
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strProfileName;
            public DOT11_SSID dot11Ssid;
            public DOT11_BSS_TYPE dot11BssType;
            public uint uNumberOfBssids;
            [MarshalAs(UnmanagedType.Bool)]
            public bool bNetworkConnectable;
            public uint wlanNotConnectableReason;
            public uint uNumberOfPhyTypes;
            public IntPtr dot11PhyTypes;
            [MarshalAs(UnmanagedType.Bool)]
            public bool bMorePhyTypes;
            public uint wlanSignalQuality;
            [MarshalAs(UnmanagedType.Bool)]
            public bool bSecurityEnabled;
            public DOT11_AUTH_ALGORITHM dot11DefaultAuthAlgorithm;
            public DOT11_CIPHER_ALGORITHM dot11DefaultCipherAlgorithm;
            public uint dwFlags;
            public uint dwReserved;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WLAN_CONNECTION_PARAMETERS
        {
            public WLAN_CONNECTION_MODE wlanConnectionMode;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string strProfile;
            public IntPtr pDot11Ssid;
            public IntPtr pDesiredBssidList;
            public DOT11_BSS_TYPE dot11BssType;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WLAN_PROFILE_INFO_LIST
        {
            public uint dwNumberOfItems;
            public uint dwIndex;
            public IntPtr ProfileInfo;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WLAN_PROFILE_INFO
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strProfileName;
            public uint dwFlags;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WLAN_CONNECTION_ATTRIBUTES
        {
            public WLAN_INTERFACE_STATE isState;
            public WLAN_CONNECTION_MODE wlanConnectionMode;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string strProfileName;
            public DOT11_SSID dot11Ssid;
            public DOT11_BSS_TYPE dot11BssType;
            public uint uNumberOfBSSIDs;
            public IntPtr pDot11BssidList;
            public DOT11_AUTH_ALGORITHM dot11DefaultAuthAlgorithm;
            public DOT11_CIPHER_ALGORITHM dot11DefaultCipherAlgorithm;
            public uint wlanAssociationAttributes;
            public uint wlanSecurityAttributes;
        }

        [DllImport("wlanapi.dll")]
        public static extern uint WlanOpenHandle(uint dwClientVersion, IntPtr pReserved, out uint pdwNegotiatedVersion, out IntPtr phClientHandle);

        [DllImport("wlanapi.dll")]
        public static extern uint WlanCloseHandle(IntPtr hClientHandle, IntPtr pReserved);

        [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
        public static extern uint WlanEnumInterfaces(IntPtr hClientHandle, IntPtr pReserved, out IntPtr ppInterfaceList);

        [DllImport("wlanapi.dll")]
        public static extern uint WlanGetAvailableNetworkList(IntPtr hClientHandle, ref Guid pInterfaceGuid, uint dwFlags, IntPtr pReserved, out IntPtr ppAvailableNetworkList);

        [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
        public static extern uint WlanGetProfileList(IntPtr hClientHandle, ref Guid pInterfaceGuid, IntPtr pReserved, out IntPtr ppProfileList);

        [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
        public static extern uint WlanConnect(IntPtr hClientHandle, ref Guid pInterfaceGuid, ref WLAN_CONNECTION_PARAMETERS pConnectionParameters, IntPtr pReserved);

        [DllImport("wlanapi.dll")]
        public static extern uint WlanDisconnect(IntPtr hClientHandle, ref Guid pInterfaceGuid, IntPtr pReserved);

        [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
        public static extern uint WlanDeleteProfile(IntPtr hClientHandle, ref Guid pInterfaceGuid, [MarshalAs(UnmanagedType.LPWStr)] string strProfileName, IntPtr pReserved);

        [DllImport("wlanapi.dll", CharSet = CharSet.Unicode)]
        public static extern uint WlanQueryInterface(IntPtr hClientHandle, ref Guid pInterfaceGuid, uint opCode, IntPtr pReserved, out uint pdwDataSize, out IntPtr ppData, IntPtr pWlanOpcodeValueType);

        [DllImport("wlanapi.dll")]
        public static extern uint WlanFreeMemory(IntPtr pMemory);
    }
}
