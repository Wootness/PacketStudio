﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Be.Windows.Forms;
using PacketStudio.Core;
using PacketStudio.DataAccess;
using PacketStudio.DataAccess.SaveData;

namespace PacketStudio.Controls.PacketsDef
{
    public partial class PacketDefineControl : UserControl
    {
        private HexDeserializer _deserializer;
        private HexTypeWrapper _packetType;

        public event EventHandler<EventArgs> ContentChanged;

        public override string Text
        {
            get => hexBox.Text;
            set => hexBox.Text = value;
        }

        public int CaretPosition
        {
            set
            {
                hexBox.Select(value,0);
            }
        }

        public (int start, int len) GetSelection => (hexBox.SelectionStart, hexBox.SelectionLength);

        public HexTypeWrapper PacketType
        {
            get { return _packetType; }
            private set
            {
                _packetType = value;
                packetTypeListBox.SelectedItem = value;
            }
        }

        public bool IsHexStream => _deserializer.IsHexStream(Text);

        // Ctor
        public PacketDefineControl() : this(null)
        {
        }

        public PacketDefineControl(PacketSaveData data)
        {
            _deserializer = new HexDeserializer();
            InitializeComponent();

            IEnumerable<HexStreamType> supportedTypes = PacketsDefinersDictionaries.SupportedTypes.ToArray();
            foreach (HexStreamType hexStreamType in Enum.GetValues(typeof(HexStreamType)))
            {
                // Check if Packet Define Control exists for this type
                bool isSupported = supportedTypes.Contains(hexStreamType);
                if (isSupported)
                {
                    // Supported  - Add to list box
                    HexTypeWrapper wrapper = new HexTypeWrapper(hexStreamType);
                    packetTypeListBox.Items.Add(wrapper);
                }
            }

            hexBox.Text = data?.Text ?? "";
            HexStreamType type = data?.Type ?? HexStreamType.Raw;
            HexTypeWrapper wrapped = new HexTypeWrapper(type);
            packetTypeListBox.SelectedItem = wrapped;
            PacketType = wrapped;

            IPacketDefiner definer = GetCurrentDefiner();

            if (data != null)
            {
                definer.LoadSaveData(data);
            }
        }

        // Get data for temp saver
        public TempPacketSaveData GetPacket()
        {
            byte[] bytes;
            string rawHex = hexBox.Text;
            try
            {
                bytes = _deserializer.Deserialize(rawHex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed deserializing Input:\r\n{ex.Message}");
            }


            IPacketDefiner definer = GetCurrentDefiner();

            byte[] data =  definer.Generate(bytes);
            LinkLayerType llt = definer.LinkLayer;
            TempPacketSaveData output = new TempPacketSaveData(data, (byte)llt);
            return output;
        }

        // Get data for real saver
        public PacketSaveData GetSaveData()
        {
            IPacketDefiner definer = GetCurrentDefiner();

            return definer.GetSaveData(hexBox.Text);
        }


        
        public void SetSelection(int firstNibbleIndex, int bytesLength)
        {
            // Can only show if this is a hex stream
            if (!IsHexStream)
                return;

            bool isSecondNibble = firstNibbleIndex % 2 == 1;
            int firstByteIndex = firstNibbleIndex / 2;
            if (IsHexStream)
            {
                int headersLengthToRemove = PacketsDefinersDictionaries.StreamTypeToFirstOffset[PacketType.Type];
                firstByteIndex -= headersLengthToRemove;

                if (firstByteIndex < 0 || bytesLength <= 0)
                {
                    hexBox.SelectionStart = 0;
                    hexBox.SelectionLength = 0;
                    return;
                }
                hexBox.SelectionStart = firstByteIndex * 2 + (isSecondNibble ? 1 : 0);
                hexBox.SelectionLength = bytesLength * 2;
            }
            hexBox.Select();
        }

        public void NormalizeHex()
        {
            byte[] bytes;
            string rawHex = hexBox.Text;
            try
            {
                bytes = _deserializer.Deserialize(rawHex);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed deserializing Input:\r\n{ex.Message}");
            }

            StringBuilder sb = new StringBuilder();
            foreach (byte b in bytes)
            {
                sb.Append($"{b:x2}");
            }
            string finalHex = sb.ToString();
            this.hexBox.Text = finalHex;
        }

        public void FlattenProtoStack()
        {
            byte[] bytes;
            try
            {
                bytes = GetPacket().Data;
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed deserializing Input:\r\n{ex.Message}");
            }

            StringBuilder sb = new StringBuilder();
            foreach (byte b in bytes)
            {
                sb.Append($"{b:x2}");
            }
            string finalHex = sb.ToString();
            this.PacketType = HexStreamType.Raw;
            this.hexBox.Text = finalHex;
        }

        // Hack to allow CTRL+A to select all
        private void textBox1_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.A)
            {
                hexBox.SelectAll();
                // These prevent from the control to make the annoying 'error' sound
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        private void InvokeContentChanged() => ContentChanged?.Invoke(this, new EventArgs());

        private void textBox1_TextChanged(object sender, EventArgs e)
        {
            InvokeContentChanged();
        }

        private void listBox1_SelectedIndexChanged(object sender, EventArgs e)
        {
            PacketType = (HexTypeWrapper)packetTypeListBox.SelectedItem;
            IPacketDefiner lastDefiner = GetCurrentDefiner();
            lastDefiner.PacketChanged -= PacketDefiner_PacketChanged;
            packetDefPanel.Controls.Clear();
            Control packetDefControl;
            Func<Control> controlCreatorFunc;
            if (!PacketsDefinersDictionaries.StreamTypeToPacketDefineControlFactory.TryGetValue(PacketType.Type, out controlCreatorFunc))
            {
                // Couldn't find a creation function
                throw new ArgumentException($"Can't find creation method for packets of type '{PacketType}'.\r\n" +
                    $"This could be the result of sloppy addition of new encapsulation\r\n" +
                    $"type without updating the {nameof(PacketDefineControl)} class.");
            }
            // Calling C'tor of the desired packet def control
            packetDefControl = controlCreatorFunc();

            packetDefPanel.Controls.Add(packetDefControl);
            packetDefControl.Dock = DockStyle.Fill;
            ((IPacketDefiner)packetDefControl).PacketChanged += PacketDefiner_PacketChanged;

            InvokeContentChanged();
        }

        private void PacketDefiner_PacketChanged(object sender, EventArgs eventArgs)
        {
            InvokeContentChanged();
        }

        private IPacketDefiner GetCurrentDefiner()
        {
            IPacketDefiner definer = packetDefPanel.Controls?[0] as IPacketDefiner;
            if (definer == null)
            {
                throw new Exception("Can't find packet definer control???");
            }

            return definer;
        }

    }
}
