﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using CliWrap.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PacketStudio.DataAccess;

namespace PacketStudio.Core
{
    [SuppressMessage("ReSharper", "InconsistentNaming")]
    public class TSharkInterop
    {
        private string _tsharkPath;
        private TempPacketsSaver _packetsSaver;
        private string _version;

        public string Version
        {
            get => _version;
            private set
            {
                _version = value;

                VersionMajor = 0;
                VersionMinor = 0;
                string[] splitted = Version?.Split('.');
                if (splitted != null && splitted.Length >= 2)
                {
                    int major = 0;
                    int minor = 0;
                    int.TryParse(splitted[0], out major);
                    int.TryParse(splitted[0], out minor);
                    VersionMajor = major;
                    VersionMinor = minor;
                }
            }
        }

        private int VersionMajor { get; set; }
        private int VersionMinor { get; set; }

        public string TsharkPath
        {
            get => _tsharkPath;
            set
            {
                if (!File.Exists(value))
                {
                    throw new FileNotFoundException("Couldn't find TShark at the given location. Path: " + value);
                }
                FileVersionInfo fvi = FileVersionInfo.GetVersionInfo(value);
                Version = fvi.ProductVersion;


                _tsharkPath = value;
            }
        }

        public TSharkInterop(string tsharkPath)
        {
            TsharkPath = tsharkPath; // This also checks if the file exists
            _packetsSaver = new TempPacketsSaver();
        }

        public async Task<XElement> GetPdmlAsync(byte[] packetBytes)
        {
            return await GetPdmlAsync(packetBytes, CancellationToken.None);
        }

        public async Task<XElement> GetPdmlAsync(IEnumerable<byte[]> packets, int packetIndex)
        {
            return await GetPdmlAsync(packets, packetIndex, CancellationToken.None);
        }


        public async Task<XElement> GetPdmlAsync(byte[] packetBytes, CancellationToken token)
        {
            return await GetPdmlAsync(new List<byte[]>() { packetBytes }, 0, token);
        }

        public Task<XElement> GetPdmlAsync(IEnumerable<byte[]> packets, int packetIndex, CancellationToken token)
        {
            return Task.Run((() =>
            {
                string pcapPath = _packetsSaver.WritePackets(packets);
                token.ThrowIfCancellationRequested();

                string args = GetPdmlArgs(pcapPath);
                ProcessStartInfo psi = new ProcessStartInfo(_tsharkPath, args);
                psi.UseShellExecute = false;
                psi.RedirectStandardError = true;
                psi.RedirectStandardOutput = true;
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Minimized;
                Process p = Process.Start(psi);

                StreamReader errorStream = p.StandardError;
                StreamReader standardStream = p.StandardOutput;
                StringBuilder output = new StringBuilder();
                StringBuilder error = new StringBuilder();
                while (!p.WaitForExit(1000))
                {
                    output.Append(standardStream.ReadToEnd());
                    error.Append(errorStream.ReadToEnd());
                }
                output.Append(standardStream.ReadToEnd());
                error.Append(errorStream.ReadToEnd());
                int x = 0;

                // TODO: Using CliWrap seems to hang if TShark complains about config in the Standard Error stream 
                // (maybe? For the 'NPF error' it doesn't?)
                // Uncomment when solved

                //CliWrap.Cli cli = new CliWrap.Cli(_tsharkPath);
                //ExecutionOutput a = cli.Execute(new ExecutionInput(args));

                // ExecutionOutput res = a;
                //if (res.ExitCode != 0) // TShark returned an error exit code
                //{
                //// If we are cancelled, we don't actually care about the exit code
                //token.ThrowIfCancellationRequested();

                //// Show the exit code + errors
                //throw new Exception($"TShark returned with exit code: {res.ExitCode}\r\n{res.StandardError}");
                //}

                //string xml = res.StandardOutput;

                if (p.ExitCode != 0)
                {
                    if (output.Length > 0)
                    {
                        // Exit code isn't 0 but we have output so dismissing for now...
                    }
                    else
                    {
                        // No output, show exit code and error
                        throw new Exception($"TShark returned with exit code: {p.ExitCode}\r\n{error}");
                    }

                }

                string xml = output.ToString();

                XElement element = ParsePdmlAsync(xml, packetIndex, token).Result;
                xml = null; //Hoping GC will get the que
                token.ThrowIfCancellationRequested();

                return element;
            }), token);
        }

        private string GetPdmlArgs(string pcapPath)
        {
            string oldVersionArgs = $"-r {pcapPath} -T pdml";
            string newVersionArgs = $"-r {pcapPath} -2 -T pdml --enable-heuristic fp_udp";

            if (isNewVersion())
            {
                return newVersionArgs;
            }
            return oldVersionArgs;
        }

        private bool isNewVersion()
        {
            if (VersionMajor == 2)
            {
                if (VersionMinor > 4)
                {
                    return true;
                }
            }
            return false;
        }


        public async Task<JObject> GetJsonRawAsync(byte[] packetBytes)
        {
            return await GetJsonRawAsync(packetBytes, CancellationToken.None);
        }

        public async Task<JObject> GetJsonRawAsync(IEnumerable<byte[]> packets, int packetIndex)
        {
            return await GetJsonRawAsync(packets, packetIndex, CancellationToken.None);
        }


        public async Task<JObject> GetJsonRawAsync(byte[] packetBytes, CancellationToken token)
        {
            return await GetJsonRawAsync(new List<byte[]>() { packetBytes }, 0, token);
        }

        public Task<JObject> GetJsonRawAsync(IEnumerable<byte[]> packets, int packetIndex, CancellationToken token)
        {
            if (!isNewVersion())
            {
                return Task.FromResult((JObject)null);
            }

            return Task.Run((() =>
            {
                string pcapPath = _packetsSaver.WritePackets(packets);
                token.ThrowIfCancellationRequested();

                CliWrap.Cli cli = new CliWrap.Cli(_tsharkPath);
                Task<ExecutionOutput> tsharkTask = cli.ExecuteAsync($"-r {pcapPath} -2 -T jsonraw --no-duplicate-keys --enable-heuristic fp_udp", token);
                bool timedOut = !tsharkTask.Wait(5_000);

                if (timedOut)
                    throw new TaskCanceledException();

                if (tsharkTask.IsCanceled) // task was canceled
                    throw new TaskCanceledException();

                ExecutionOutput res = tsharkTask.Result;
                if (res.ExitCode != 0) // TShark returned an error exit code
                {
                    // If we are cancelled, we don't actually care about the exit code
                    token.ThrowIfCancellationRequested();

                    // Show the exit code + errors
                    throw new Exception($"TShark returned with exit code: {res.ExitCode}\r\n{tsharkTask.Result.StandardError}");
                }

                string jsonArray = res.StandardOutput;

                JObject jsonPacket = ParseJsonAsync(jsonArray, packetIndex, token).Result;

                jsonArray = null; //Hoping GC will get the que
                token.ThrowIfCancellationRequested();

                return jsonPacket;
            }));
        }

        private Task<XElement> ParsePdmlAsync(string xml, int packetIndex, CancellationToken token)
        {
            return Task.Run((() =>
            {
                token.ThrowIfCancellationRequested();

                // This is most likely the heavy operation in this method. So checking the token right before and after
                XDocument xdoc = XDocument.Parse(xml);

                token.ThrowIfCancellationRequested();

                // Get the right packet according to the given index (parameter)

                // New PDML Style
                XContainer skippedHeaderNode = (XContainer)xdoc.FirstNode?.NextNode?.NextNode;
                if (skippedHeaderNode == null)
                {
                    // Old PDML Style
                    skippedHeaderNode = ((XContainer)xdoc.FirstNode);
                }

                XElement packetElement =
                    skippedHeaderNode.Elements("packet").ElementAt(packetIndex); // Getting the x-th 'packet' node

                string singlePacketXml = packetElement.ToString();
                XElement elem = XElement.Parse(singlePacketXml);

                // Hoping GC will get the cue
                singlePacketXml = null;
                packetElement = null;
                xdoc = null;


                return elem;
            }), token);
        }
        private Task<JObject> ParseJsonAsync(string json, int packetIndex, CancellationToken token)
        {
            return Task.Run((() =>
            {
                token.ThrowIfCancellationRequested();

                // This is most likely the heavy operation in this method. So checking the token right before and after
                JsonSerializer serializer = JsonSerializer.CreateDefault();
                JsonReader reader = new JsonTextReader(new StringReader(json));
                object deserialized = serializer.Deserialize(reader);
                JArray asArray = deserialized as JArray;


                token.ThrowIfCancellationRequested();

                // Get the right packet according to the given index (parameter)
                JObject packet = asArray.ElementAt(packetIndex) as JObject;

                // Hoping GC will get the cue
                reader.Close();
                reader = null;
                asArray = null;


                return packet;
            }), token);
        }

        public Task<TSharkCombinedResults> GetPdmlAndJsonAsync(IEnumerable<byte[]> packets, int packetIndex,
            CancellationToken token)
        {
            Task<XElement> pdmlTask = GetPdmlAsync(packets, packetIndex, token);
            Task<JObject> jsonTask = GetJsonRawAsync(packets, packetIndex, token);

            return Task.Factory.StartNew(() =>
            {
                XElement pdml = null;
                JObject json = null;

                try
                {
                    pdml = pdmlTask.Result;
                }
                catch (Exception)
                {
                    // Don't care
                }
                try
                {
                    json = jsonTask.Result;
                }
                catch (Exception)
                {
                    // Don't care
                }
                return new TSharkCombinedResults(pdml, json);
            }, token);
        }
    }
}
