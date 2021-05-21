﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using FastPcapng;
using Haukcode.PcapngUtils.Common;
using Haukcode.PcapngUtils.PcapNG.BlockTypes;
using PacketStudio.Core;
using PacketStudio.DataAccess;
using PacketStudio.DataAccess.SaveData;
using Syncfusion.Windows.Shared;
using Task = System.Threading.Tasks.Task;

namespace PacketStudio.NewGUI.ViewModels
{
    public class MainViewModel : NotificationObject
    {
        private const string TAB_HEADER_PREFIX = "Packet";
        private int nextTabNumber = 1;

        private ObservableCollection<SessionPacketViewModel> _modifiedPackets;
        private SessionPacketViewModel _currentSessionPacket;


        private MemoryPcapng _backingPcapng = new MemoryPcapng();
        private ObservableCollection<string> _packetsDescs = new(new string[0]);
        private int _selectedPacketIndex = 0;

        public ObservableCollection<SessionPacketViewModel> ModifiedPackets
        {
            get { return _modifiedPackets; }
            set
            {
                _modifiedPackets = value;
                this.RaisePropertyChanged(nameof(ModifiedPackets));
            }
        }

        public SessionPacketViewModel CurrentSessionPacket
        {
            get { return _currentSessionPacket; }
            set
            {
                // Handle last packet
                if (_currentSessionPacket != null)
                {
                    if (_currentSessionPacket.IsModified)
                    {
                        Debug.WriteLine(" ### Packet was modified!!!");
                        // ??
                    }
                    else
                    {
                        Debug.WriteLine(" ### Packet was NOT modified");
                        ModifiedPackets.Remove(_currentSessionPacket);
                    }
                }

                // Handle new packet
                _currentSessionPacket = value;
                ModifiedPackets.Add(_currentSessionPacket);
                this.RaisePropertyChanged(nameof(CurrentSessionPacket));
            }
        }

        public ObservableCollection<string> PacketsDescriptions
        {
            get { return _packetsDescs; }
            set
            {
                _packetsDescs = value;
                this.RaisePropertyChanged(nameof(PacketsDescriptions));
            }
        }

        public int PacketsCount => PacketsDescriptions.Count;

        public int SelectedPacketIndex
        {
            get { return _selectedPacketIndex; }
            set
            {
                Debug.WriteLine($" XXX SelectedIndex changed to: {value}");
                if (value == -1)
                {
                    // We say no to '-1's from the gui!
                    return;
                }
                _selectedPacketIndex = value;
                this.RaisePropertyChanged(nameof(SelectedPacketIndex));

                if (_selectedPacketIndex == -1)
                    return;

                // TODO: if _selected too big what happens? throws?
                // TODO: Use link layer
                var packetBlock = _backingPcapng.GetPacket(_selectedPacketIndex);

                var iface = _backingPcapng.Interfaces[packetBlock.InterfaceID];
                var ifaceLinkType = iface.LinkType;

                // TODO: Remvoe this 'tab number' header thingy
                var sessionPacketObj = _modifiedPackets.FirstOrDefault((SessionPacketViewModel viewModel) => viewModel.Header == _selectedPacketIndex);
                if (sessionPacketObj != null)
                {
                    CurrentSessionPacket = sessionPacketObj;
                }
                else
                {
                    CurrentSessionPacket = new SessionPacketViewModel()
                    {
                        Header = _selectedPacketIndex,
                    };
                    CurrentSessionPacket.LoadInitialState(new PacketSaveDataNG(HexStreamType.Raw, packetBlock.Data.ToHex())
                    {
                        Details = new Dictionary<string, string>()
                    {
                        {PacketSaveDataNGProtoFields.ENCAPS_TYPE, ((int)ifaceLinkType).ToString()},
                    }
                    });
                }
            }
        }

        public MemoryPcapng BackingPcapng => _backingPcapng;

        public void AddNewPacket(PacketSaveDataNG psdng = null)
        {
            if (psdng == null)
            {
                psdng = new PacketSaveDataNG(HexStreamType.Raw, String.Empty);
            }

            int tabNumber = nextTabNumber++;
            SessionPacketViewModel new_model1 = new SessionPacketViewModel()
            {
                Header = tabNumber,
                Content = $"",
            };
            new_model1.LoadInitialState(psdng);
            _modifiedPackets.Add(new_model1);
        }


        public void ResetItemsCollection()
        {
            _modifiedPackets.Clear();
            // Add first packet
            nextTabNumber = 1;
            AddNewPacket(null);

        }
        public MainViewModel()
        {
            _modifiedPackets = new ObservableCollection<SessionPacketViewModel>();
            CurrentSessionPacket = new SessionPacketViewModel();

            // Register self with parent MainWindow
            MainWindow.SessionViewModel = this;
        }

        private TSharkInterop _tshark = new TSharkInterop(SharksFinder.GetDirectories().First().TsharkPath);

        public Task LoadFileAsync(string path, CancellationToken token)
        {
            _backingPcapng = MemoryPcapng.ParsePcapng(path);

            _modifiedPackets.Clear();

            var updateDescsTask = UpdatePacketsDescriptions(token);
            return updateDescsTask.ContinueWith(task =>
            {
                this.SelectedPacketIndex = 0;
                UpdatePacketsDescriptions(token);
            }, token);
        }


        public Task UpdatePacketsDescriptions(CancellationToken token)
        {
            Debug.WriteLine(" @@@ List Update: Entered UpdatePacketsDescriptions");
            ApplyModifications();

            WiresharkPipeSender sender = new WiresharkPipeSender();
            string pipeName = "ps_2_ws_pipe" + (new Random()).Next();
            sender.SendPcapngAsync(pipeName, _backingPcapng);

            Debug.WriteLine($" @@@ List Update: Entered Clearing old ObsCollection (had {PacketsDescriptions.Count} items)");
            var newCollection =  new ObservableCollection<string>();
            int DEBUG_HOW_MANY_TIMES_RAN = 0;

            void HandleNewTSharkTextLine(string line)
            {
                DEBUG_HOW_MANY_TIMES_RAN++;
                if (!string.IsNullOrWhiteSpace(line))
                {
                    // On first valid packet update the collection
                    if (this.PacketsDescriptions != newCollection)
                    {
                        this.PacketsDescriptions = newCollection;
                    }

                    App.Current.Dispatcher.Invoke(() => { newCollection.Add(line); });
                }
            }


            Debug.WriteLine($" @@@ List Update: Calling TSark");
            var tsharkTask = _tshark.GetTextOutputAsync(@"\\.\pipe\" + pipeName, token, HandleNewTSharkTextLine);
            tsharkTask.ContinueWith(task =>
            {
                    Debug.WriteLine($" @@@ List Update: Did TShark fail us? {task.Status}, Our function was invoked {DEBUG_HOW_MANY_TIMES_RAN} times");
            });

            return tsharkTask;
        }

        public Task MovePacket(int newIndex)
        {
            int currectIndex = CurrentSessionPacket.Header;
            Debug.WriteLine($" @@@ Trying to move packet #{currectIndex} to Index #{newIndex}");
            if (currectIndex == newIndex)
            {
                // Trying to move to same place
                return Task.CompletedTask;
            }

            _backingPcapng.MovePacket(currectIndex, newIndex);
            CurrentSessionPacket.Header = newIndex;
            // Update other modified packets
            if (currectIndex > newIndex)
            {
                foreach (var pktVm in ModifiedPackets.Where(pkt => pkt.Header >= newIndex && pkt.Header < currectIndex))
                {
                    pktVm.Header++;
                }
            }
            else if (currectIndex < newIndex)
            {
                foreach (var pktVm in ModifiedPackets.Where(pkt => pkt.Header > newIndex && pkt.Header < currectIndex))
                {
                    pktVm.Header--;
                }
            }

            return UpdatePacketsDescriptions(CancellationToken.None);
        }

        public void ApplyModifications()
        {
            Debug.WriteLine(" @@@ ====== Applying Modifications ======");
            foreach (SessionPacketViewModel packetVm in ModifiedPackets.Where(packetVm => packetVm.IsModified).ToList())
            {
                int index = packetVm.Header;
                Debug.WriteLine($" @@@ Applying modification for packet #{index}");
                TempPacketSaveData packet = packetVm.ExportPacket;

                EnhancedPacketBlock oldEpb = _backingPcapng.GetPacket(index);
                oldEpb.Data = packet.Data;
                if (_backingPcapng.Interfaces[oldEpb.InterfaceID].LinkType != (LinkTypes)packet.LinkLayer)
                {
                    // Mismatch link type, find other interface
                    var matchingInterface =
                        _backingPcapng.Interfaces.FirstOrDefault(iface =>
                            iface.LinkType == (LinkTypes)packet.LinkLayer);
                    if (matchingInterface == null)
                    {
                        throw new NotImplementedException("No interface for the linklayer of the packet");
                    }
                    var matchingIfaceId = _backingPcapng.Interfaces.IndexOf(matchingInterface);
                    oldEpb.InterfaceID = matchingIfaceId;
                }

                _backingPcapng.UpdatePacket(index, oldEpb);

                ModifiedPackets.Remove(packetVm);
            }
        }
    }
}