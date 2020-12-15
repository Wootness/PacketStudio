﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows.Controls;
using System.Xml;
using PacketStudio.DataAccess;
using PacketStudio.DataAccess.Json;

namespace PacketStudio.NewGUI.PacketTemplatesControls
{
    /// <summary>
    /// Interaction logic for UdpTemplateControl.xaml
    /// </summary>
    [DisplayName("Raw Frame")]
    [Order(0)]
    [HexStreamType(HexStreamType.Raw)]
    public partial class RawTemplateControl : UserControl, IPacketTemplateControl
    {
        private static Dictionary<string, LinkLayerType> _singletonMap = null;
        private static Dictionary<string, LinkLayerType> _map
        {
            get
            {
                if (_singletonMap == null)
                {
                    _singletonMap = new Dictionary<string, LinkLayerType>();
                    for (int i = 0; i < ushort.MaxValue; i++)
                    {
                        if (Enum.IsDefined(typeof(LinkLayerType), i))
                        {
                            LinkLayerType type = (LinkLayerType) i;
                            string name = type.ToString();
                            _singletonMap[name] = type;
                        }
                    }
                }
                return _singletonMap;
            }
        }

        public RawTemplateControl()
        {
            InitializeComponent();

            foreach (KeyValuePair<string, LinkLayerType> nameAndType in _map)
            {
                LinkLayerType type = nameAndType.Value;
                string name = nameAndType.Key;
                string label = $"{(int) type} {name}";
                linkLayersBox.Items.Add(label);
            }

            // Setting default to Ethernet
            linkLayersBox.SelectedIndex = (int)LinkLayerType.Ethernet;
        }

        public event EventHandler Changed;

        public bool IsValidWithPayload(byte[] raw) => true;

        public (bool success, TempPacketSaveData packet, string error) GeneratePacket(byte[] rawHex)
        {
            string name = linkLayersBox.SelectedItem as string;
            name = name.Substring(name.IndexOf(" ", StringComparison.Ordinal)).Trim();
            if (_map.TryGetValue(name, out LinkLayerType type))
                return (true, new TempPacketSaveData(rawHex, type), null);
            return (false, null, "Couldn't result Link Layer type");
        }

        public string GenerateSaveDataJson()
        {
            var saveData = new Dictionary<string, object>();
            saveData["EtherType"] = linkLayersBox.Text;
            DictJsonSerializer s = new DictJsonSerializer();
            return s.Serialize(saveData);
        }

        public void LoadSaveDataJson(string json)
        {
            throw new NotImplementedException();
        }

        private void LinkLayersBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
            this.Changed?.Invoke(this, e);
    }
}
